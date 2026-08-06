using System.Text;
using ShelfGuard.Application.Common;
// RfmScoredCustomerRow/IMarketingAnalyticsRepository/RfmSegmentClassifier/RfmSegmentCatalog/
// PiiMasking live one namespace up (ShelfGuard.Application.Features.MarketingAnalytics) — C#'s
// enclosing-namespace fallback would resolve them regardless, but an explicit using is kept for
// clarity, same practice AudienceBuilderService.cs already established for the identical reason.
using ShelfGuard.Application.Features.MarketingAnalytics;
using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;

public sealed class PostCampaignService : IPostCampaignService
{
    /// <summary>v1 cap on total submitted tokens per import — more conservative than the
    /// competitor's own unconfirmed 50,000 (brief's explicit choice). Exceeding it is a clear
    /// error, never a silent truncation.</summary>
    public const int MaxAcceptedRows = 20_000;

    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;
    private const int ExportMaxRows = 50_000;

    /// <summary>TASK-478 fix — see <see cref="SegmentImportParser"/>'s own <c>InvalidSampleCap</c>
    /// doc comment for the full rationale (was 20, silently capped the downloadable error report
    /// with no truncation signal). Kept at the same value by convention, not a shared constant —
    /// this caps well-formed-but-unmatched ("unknown") tokens, a conceptually different thing
    /// from that class's invalid-FORMAT tokens.</summary>
    private const int UnknownSampleCap = 500;

    // TASK-474/477 (security review finding A/C): user-facing messages for the two new failure
    // shapes on the import path — kept as named constants since both are returned from more than
    // one branch of ImportAsync below.
    private const string TooLargeImportMessage =
        "Файл або список ID завеликий для обробки. Зменшіть кількість рядків і спробуйте ще раз.";
    private const string UnreadableFileMessage =
        "Не вдалося прочитати файл. Перевірте, що це дійсний файл .xlsx або .csv, і спробуйте ще раз.";

    private readonly IPostCampaignRepository _repo;
    private readonly IMarketingAnalyticsRepository _marketingAnalytics;
    private readonly IPostCampaignAdvisor _advisor;
    private readonly IExcelExportService _excelExport;
    private readonly IExcelImportService _excelImport;
    private readonly IActivityLogRepository _activityLogs;

    public PostCampaignService(
        IPostCampaignRepository repo,
        IMarketingAnalyticsRepository marketingAnalytics,
        IPostCampaignAdvisor advisor,
        IExcelExportService excelExport,
        IExcelImportService excelImport,
        IActivityLogRepository activityLogs)
    {
        _repo = repo;
        _marketingAnalytics = marketingAnalytics;
        _advisor = advisor;
        _excelExport = excelExport;
        _excelImport = excelImport;
        _activityLogs = activityLogs;
    }

    // ── List ─────────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PostCampaignSegmentListItemDto>> ListSegmentsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var segments = await _repo.ListSegmentsAsync(tenantId, ct);
        return segments
            .Select(s => new PostCampaignSegmentListItemDto(
                s.Id, s.Name, s.CreatedAt, s.UploadedCount, s.MatchedCount,
                IsAnalyzed(s), s.AfterStart, s.AfterEnd, s.AnalyzedAt))
            .ToList();
    }

    // ── Import ───────────────────────────────────────────────────────────────────────────────

    public async Task<(PostCampaignImportResultDto? Result, string? Error)> ImportAsync(
        Guid tenantId, Guid userId, PostCampaignImportRequest request, CancellationToken ct = default)
    {
        SegmentImportParseResult tokens;
        DelimitedRowsParseResult? fileResult = null;

        if (request.FileBytes is { Length: > 0 })
        {
            IReadOnlyList<IReadOnlyList<string?>> rows;
            try
            {
                rows = IsXlsx(request.FileName)
                    ? _excelImport.ParseXlsx(new MemoryStream(request.FileBytes)).Rows
                    : SegmentImportParser.ParseCsvText(Encoding.UTF8.GetString(request.FileBytes));
            }
            catch (ImportTooLargeException)
            {
                // Finding A: ParseXlsx/ParseCsvText now reject an oversized upload BEFORE fully
                // materializing it — translate to the same clean (null, error) shape every other
                // validation failure in this method already uses.
                return (null, TooLargeImportMessage);
            }
            catch (Exception ex) when (ex is FormatException or NullReferenceException)
            {
                // Finding C: a corrupt/non-XLSX byte stream (e.g. a .txt renamed to .xlsx) throws
                // from inside ClosedXML/the OOXML packaging layer it depends on — empirically
                // confirmed (see ExcelImportServiceTests) to surface as either FormatException's
                // own System.IO.FileFormatException subtype (corrupt/empty zip) or a bare
                // NullReferenceException (a well-formed zip that isn't a valid OOXML spreadsheet
                // package) — never let either reach the caller as an unhandled 500.
                return (null, UnreadableFileMessage);
            }

            fileResult = SegmentImportParser.ParseDelimitedRows(rows, request.ColumnIndex);
            tokens = fileResult.Tokens;
        }
        else if (!string.IsNullOrWhiteSpace(request.RawText))
        {
            try
            {
                tokens = SegmentImportParser.ParseTextList(request.RawText);
            }
            catch (ImportTooLargeException)
            {
                return (null, TooLargeImportMessage);
            }
        }
        else
        {
            return (null, "Provide either rawText or a file.");
        }

        if (tokens.UploadedCount > MaxAcceptedRows)
            return (null, $"Максимум {MaxAcceptedRows:N0} ID за одне завантаження. Отримано {tokens.UploadedCount:N0}.");

        // ── Bulk-resolve unique valid tokens against real Customer rows ────────────────────────
        var guidCandidates = tokens.UniqueValidTokens
            .Where(t => t.Kind == ImportTokenKind.Guid).Select(t => t.CustomerId!.Value).Distinct().ToList();
        var phoneCandidates = tokens.UniqueValidTokens
            .Where(t => t.Kind == ImportTokenKind.Phone).Select(t => t.NormalizedPhone!).Distinct().ToList();

        var found = (guidCandidates.Count == 0 && phoneCandidates.Count == 0)
            ? []
            : await _repo.FindCustomersByIdsOrPhonesAsync(tenantId, guidCandidates, phoneCandidates, ct);

        var byId = found.ToDictionary(c => c.Id);
        var byPhone = found.Where(c => c.Phone is not null)
            .GroupBy(c => c.Phone!)
            .ToDictionary(g => g.Key, g => g.First());

        var matchedIds = new List<Guid>();
        var seenCustomerIds = new HashSet<Guid>();
        var unknownSample = new List<string>();
        var unknownCount = 0;
        var entityDuplicateCount = 0;

        foreach (var token in tokens.UniqueValidTokens)
        {
            var resolved = token.Kind == ImportTokenKind.Guid
                ? (byId.ContainsKey(token.CustomerId!.Value) ? token.CustomerId : null)
                : (byPhone.TryGetValue(token.NormalizedPhone!, out var c) ? c.Id : (Guid?)null);

            if (resolved is null)
            {
                unknownCount++;
                if (unknownSample.Count < UnknownSampleCap) unknownSample.Add(token.RawText);
                continue;
            }

            // Two different valid tokens (e.g. a GUID and that same person's phone) resolving to
            // the SAME customer — collapse to one member row, count the extra as a duplicate, so
            // MatchedCount always equals the real inserted-member-row count (balance identity).
            if (!seenCustomerIds.Add(resolved.Value))
            {
                entityDuplicateCount++;
                continue;
            }

            matchedIds.Add(resolved.Value);
        }

        var segment = new PostCampaignSegment
        {
            TenantId = tenantId,
            CreatedByUserId = userId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            UploadedCount = tokens.UploadedCount,
            MatchedCount = matchedIds.Count,
            DuplicateCount = tokens.DuplicateCount + entityDuplicateCount,
            UnknownCount = unknownCount,
            InvalidCount = tokens.InvalidCount,
            UnknownTokensSample = unknownSample,
            InvalidTokensSample = tokens.InvalidTokensSample.ToList(),
            SegmentHash = string.Empty, // stamped only when Analyze first runs (draft state)
        };

        await _repo.AddSegmentAsync(segment, ct);
        await _repo.AddMembersAsync(
            matchedIds.Select(id => new PostCampaignSegmentMember { TenantId = tenantId, SegmentId = segment.Id, CustomerId = id }).ToList(),
            ct);
        await _repo.SaveChangesAsync(ct);

        await LogAsync(
            tenantId, userId, "marketing_analytics.post_campaign.import",
            $"segmentId={segment.Id}; uploaded={segment.UploadedCount}; matched={segment.MatchedCount}; " +
            $"unknown={segment.UnknownCount}; invalid={segment.InvalidCount}; duplicate={segment.DuplicateCount}",
            ct, entityType: "marketing_analytics_import");

        var preview = fileResult is null
            ? null
            : new PostCampaignImportColumnPreviewDto(
                fileResult.Headers, fileResult.DetectedColumnIndex, fileResult.DetectedColumnHeader, fileResult.PreviewRows);

        return (new PostCampaignImportResultDto(
            segment.Id, segment.Name, segment.UploadedCount, segment.MatchedCount, segment.DuplicateCount,
            segment.UnknownCount, segment.InvalidCount, unknownSample, tokens.InvalidTokensSample, preview, segment.CreatedAt), null);
    }

    private static bool IsXlsx(string? fileName) =>
        fileName is not null && fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    // ── Analyze ──────────────────────────────────────────────────────────────────────────────

    public async Task<(PostCampaignAnalyzeResultDto? Result, string? Error)> AnalyzeAsync(
        Guid tenantId, Guid segmentId, DateOnly afterStart, DateOnly afterEnd, CancellationToken ct = default)
    {
        var segment = await _repo.GetSegmentAsync(tenantId, segmentId, ct);
        if (segment is null) return (null, "Segment not found.");
        if (afterStart > afterEnd) return (null, "afterStart must be on or before afterEnd.");

        // Source doc §10.1's exact formula. DateOnly.DayNumber (proleptic Gregorian day count)
        // gives exact inclusive-day arithmetic without any AddDays/leap-year edge cases.
        var windowDays = afterEnd.DayNumber - afterStart.DayNumber + 1;
        var beforeEnd = afterStart.AddDays(-1);
        var beforeStart = beforeEnd.AddDays(-(windowDays - 1));

        segment.AfterStart = afterStart;
        segment.AfterEnd = afterEnd;
        segment.BeforeStart = beforeStart;
        segment.BeforeEnd = beforeEnd;
        segment.SegmentHash = PostCampaignSegmentHash.Compute(tenantId, segmentId, afterStart, afterEnd);
        segment.AnalyzedAt = DateTimeOffset.UtcNow;

        await _repo.SaveChangesAsync(ct);

        return (new PostCampaignAnalyzeResultDto(
            segment.Id, afterStart, afterEnd, beforeStart, beforeEnd, segment.SegmentHash, segment.AnalyzedAt.Value), null);
    }

    // ── Summary ──────────────────────────────────────────────────────────────────────────────

    public async Task<(PostCampaignSummaryDto? Result, string? Error)> GetSummaryAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var (segment, customerIds, error) = await LoadAnalyzedAsync(tenantId, segmentId, ct);
        if (error is not null) return (null, error);

        var (beforeMetrics, afterMetrics) = await LoadPeriodMetricsAsync(tenantId, segment!, customerIds, storeIds, ct);
        var beforeById = beforeMetrics.ToDictionary(m => m.CustomerId);
        var afterById = afterMetrics.ToDictionary(m => m.CustomerId);

        int reactivated = 0, retained = 0, dropped = 0, notReturned = 0;
        decimal moneyBefore = 0m, moneyAfter = 0m;

        foreach (var id in customerIds)
        {
            var beforeCount = beforeById.TryGetValue(id, out var bm) ? bm.PurchaseCount : 0;
            var afterCount = afterById.TryGetValue(id, out var am) ? am.PurchaseCount : 0;
            moneyBefore += bm?.Turnover ?? 0m;
            moneyAfter += am?.Turnover ?? 0m;

            switch (PostCampaignBehaviorClassifier.Classify(beforeCount, afterCount))
            {
                case PostCampaignBehaviorStatus.Reactivated: reactivated++; break;
                case PostCampaignBehaviorStatus.Retained: retained++; break;
                case PostCampaignBehaviorStatus.Dropped: dropped++; break;
                case PostCampaignBehaviorStatus.NotReturned: notReturned++; break;
            }
        }

        var inactiveBefore = reactivated + notReturned;
        var activeBefore = retained + dropped;
        var buyersAfter = reactivated + retained;

        var recInput = new PostCampaignSummaryRecommendationInputDto(
            customerIds.Count, moneyAfter,
            PostCampaignBehaviorClassifier.MoneyDeltaPercent(moneyBefore, moneyAfter),
            reactivated, PostCampaignBehaviorClassifier.ReactivationRatePercent(reactivated, inactiveBefore),
            dropped, buyersAfter);

        var dto = new PostCampaignSummaryDto(
            MatchedCount: customerIds.Count,
            MoneyBefore: moneyBefore,
            MoneyAfter: moneyAfter,
            MoneyDeltaPercent: PostCampaignBehaviorClassifier.MoneyDeltaPercent(moneyBefore, moneyAfter),
            BuyersAfter: buyersAfter,
            Reactivated: reactivated,
            ReactivationRatePercent: PostCampaignBehaviorClassifier.ReactivationRatePercent(reactivated, inactiveBefore),
            InactiveBefore: inactiveBefore,
            Retained: retained,
            RetentionRatePercent: PostCampaignBehaviorClassifier.RetentionRatePercent(retained, activeBefore),
            ActiveBefore: activeBefore,
            Dropped: dropped,
            ChurnRatePercent: PostCampaignBehaviorClassifier.ChurnRatePercent(dropped, activeBefore),
            NotReturned: notReturned,
            Recommendation: PostCampaignRecommendationTemplates.BuildSummary(recInput),
            SegmentHash: segment!.SegmentHash,
            AfterStart: segment.AfterStart!.Value,
            AfterEnd: segment.AfterEnd!.Value,
            BeforeStart: segment.BeforeStart!.Value,
            BeforeEnd: segment.BeforeEnd!.Value,
            CalculatedAt: DateTimeOffset.UtcNow);

        return (dto, null);
    }

    // ── Daily turnover ───────────────────────────────────────────────────────────────────────

    public async Task<(PostCampaignDailyTurnoverDto? Result, string? Error)> GetDailyTurnoverAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var (segment, customerIds, error) = await LoadAnalyzedAsync(tenantId, segmentId, ct);
        if (error is not null) return (null, error);

        var windowDays = segment!.AfterEnd!.Value.DayNumber - segment.AfterStart!.Value.DayNumber + 1;

        IReadOnlyDictionary<int, decimal> beforeDaily = customerIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await _repo.GetDailyTurnoverByOrdinalDayAsync(
                tenantId, customerIds, storeIds, segment.BeforeStart!.Value, segment.BeforeEnd!.Value, ct);
        IReadOnlyDictionary<int, decimal> afterDaily = customerIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await _repo.GetDailyTurnoverByOrdinalDayAsync(
                tenantId, customerIds, storeIds, segment.AfterStart!.Value, segment.AfterEnd!.Value, ct);

        var points = Enumerable.Range(0, windowDays)
            .Select(dayIndex => new PostCampaignDailyTurnoverPointDto(
                dayIndex + 1,
                segment.AfterStart.Value.AddDays(dayIndex),
                beforeDaily.GetValueOrDefault(dayIndex, 0m),
                afterDaily.GetValueOrDefault(dayIndex, 0m)))
            .ToList();

        return (new PostCampaignDailyTurnoverDto(
            points, points.Sum(p => p.BeforeAmount), points.Sum(p => p.AfterAmount),
            segment.SegmentHash, segment.AfterStart.Value, segment.AfterEnd.Value,
            segment.BeforeStart!.Value, segment.BeforeEnd!.Value, DateTimeOffset.UtcNow), null);
    }

    // ── RFM activity (checks / turnover / average check / recency) ─────────────────────────────

    public async Task<(PostCampaignRfmActivityDto? Result, string? Error)> GetRfmActivityAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var (segment, customerIds, error) = await LoadAnalyzedAsync(tenantId, segmentId, ct);
        if (error is not null) return (null, error);

        var (beforeMetrics, afterMetrics) = await LoadPeriodMetricsAsync(tenantId, segment!, customerIds, storeIds, ct);

        var checksBefore = beforeMetrics.Sum(m => m.PurchaseCount);
        var checksAfter = afterMetrics.Sum(m => m.PurchaseCount);
        var turnoverBefore = beforeMetrics.Sum(m => m.Turnover);
        var turnoverAfter = afterMetrics.Sum(m => m.Turnover);
        decimal? avgCheckBefore = checksBefore > 0 ? Math.Round(turnoverBefore / checksBefore, 2) : null;
        decimal? avgCheckAfter = checksAfter > 0 ? Math.Round(turnoverAfter / checksAfter, 2) : null;

        // Recency (source doc §22.1's recommended precise formula): period_end minus the last
        // purchase date on/before period_end, averaged ONLY over customers who purchased at all
        // in that window — excluded from the denominator otherwise, never defaulted to 0 (the
        // "show the denominator" transparency rule this method's own DTO also exposes).
        decimal? AvgRecency(IReadOnlyList<CustomerPeriodMetricsRow> metrics, DateOnly periodEnd) =>
            metrics.Count == 0
                ? null
                : Math.Round(metrics.Average(m => (decimal)(periodEnd.DayNumber - DateOnly.FromDateTime(m.LastPurchaseDate).DayNumber)), 2);

        var recencyBefore = AvgRecency(beforeMetrics, segment!.BeforeEnd!.Value);
        var recencyAfter = AvgRecency(afterMetrics, segment.AfterEnd!.Value);

        return (new PostCampaignRfmActivityDto(
            ChecksBefore: checksBefore,
            ChecksAfter: checksAfter,
            ChecksDeltaPercent: PostCampaignBehaviorClassifier.PercentDelta(checksBefore, checksAfter),
            TurnoverBefore: turnoverBefore,
            TurnoverAfter: turnoverAfter,
            TurnoverDeltaPercent: PostCampaignBehaviorClassifier.MoneyDeltaPercent(turnoverBefore, turnoverAfter),
            AverageCheckBefore: avgCheckBefore,
            AverageCheckAfter: avgCheckAfter,
            AverageCheckDeltaPercent: PostCampaignBehaviorClassifier.PercentDelta(avgCheckBefore, avgCheckAfter),
            RecencyBeforeDays: recencyBefore,
            RecencyAfterDays: recencyAfter,
            RecencyDeltaPercent: PostCampaignBehaviorClassifier.PercentDelta(recencyBefore, recencyAfter),
            RecencyDenominatorBefore: beforeMetrics.Count,
            RecencyDenominatorAfter: afterMetrics.Count,
            SegmentHash: segment.SegmentHash,
            AfterStart: segment.AfterStart!.Value,
            AfterEnd: segment.AfterEnd!.Value,
            BeforeStart: segment.BeforeStart!.Value,
            BeforeEnd: segment.BeforeEnd!.Value,
            CalculatedAt: DateTimeOffset.UtcNow), null);
    }

    // ── Customers table ──────────────────────────────────────────────────────────────────────

    public async Task<(PostCampaignCustomerTableDto? Result, string? Error)> GetCustomersAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, int page, int pageSize,
        string? sortBy, bool sortDescending, bool canViewUnmaskedPii, CancellationToken ct = default)
    {
        var (segment, customerIds, error) = await LoadAnalyzedAsync(tenantId, segmentId, ct);
        if (error is not null) return (null, error);

        var (safePage, safePageSize) = NormalizePaging(page, pageSize);
        var sortKey = PostCampaignSortKeys.NormalizeCustomers(sortBy);

        if (customerIds.Count == 0)
            return (new PostCampaignCustomerTableDto(0, [], safePage, safePageSize, 0, sortKey, sortDescending, segment!.SegmentHash, DateTimeOffset.UtcNow), null);

        var (beforeMetrics, afterMetrics) = await LoadPeriodMetricsAsync(tenantId, segment!, customerIds, storeIds, ct);
        var beforeById = beforeMetrics.ToDictionary(m => m.CustomerId);
        var afterById = afterMetrics.ToDictionary(m => m.CustomerId);

        var names = await _repo.GetCustomerNamesAsync(tenantId, customerIds, ct);
        var namesById = names.ToDictionary(n => n.CustomerId);

        var (beforeKeys, afterKeys) = await ComputeRfmKeysAsync(tenantId, storeIds, segment!, customerIds, ct);

        var rows = customerIds.Select(id =>
        {
            var b = beforeById.GetValueOrDefault(id);
            var a = afterById.GetValueOrDefault(id);
            namesById.TryGetValue(id, out var nameRow);
            var beforeKey = beforeKeys[id];
            var afterKey = afterKeys[id];

            return new PostCampaignCustomerRowDto(
                id,
                nameRow?.Name ?? "—",
                canViewUnmaskedPii ? nameRow?.Phone : PiiMasking.MaskPhone(nameRow?.Phone),
                b?.PurchaseCount ?? 0,
                a?.PurchaseCount ?? 0,
                b?.Turnover ?? 0m,
                a?.Turnover ?? 0m,
                beforeKey,
                LabelOf(beforeKey),
                afterKey,
                LabelOf(afterKey));
        }).ToList();

        rows = ApplySort(rows, sortKey, sortDescending);

        var totalCount = rows.Count;
        var paged = rows.Skip((safePage - 1) * safePageSize).Take(safePageSize).ToList();

        return (new PostCampaignCustomerTableDto(
            totalCount, paged, safePage, safePageSize, TotalPages(totalCount, safePageSize),
            sortKey, sortDescending, segment!.SegmentHash, DateTimeOffset.UtcNow), null);
    }

    private static List<PostCampaignCustomerRowDto> ApplySort(List<PostCampaignCustomerRowDto> rows, string sortKey, bool descending)
    {
        Func<PostCampaignCustomerRowDto, IComparable> keySelector = sortKey switch
        {
            "checksbefore" => r => r.ChecksBefore,
            "checksafter" => r => r.ChecksAfter,
            "turnoverbefore" => r => r.TurnoverBefore,
            "transition" => r => RankOf(r.SegmentBefore) - RankOf(r.SegmentAfter), // higher = bigger improvement
            _ => r => r.TurnoverAfter,
        };

        return (descending ? rows.OrderByDescending(keySelector) : rows.OrderBy(keySelector))
            .ThenBy(r => r.CustomerId)
            .ToList();
    }

    // ── RFM migration matrix ─────────────────────────────────────────────────────────────────

    public async Task<(PostCampaignMigrationDto? Result, string? Error)> GetMigrationAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var (segment, customerIds, error) = await LoadAnalyzedAsync(tenantId, segmentId, ct);
        if (error is not null) return (null, error);

        if (customerIds.Count == 0)
        {
            var emptyRec = PostCampaignRecommendationTemplates.BuildMigration(new PostCampaignMigrationRecommendationInputDto(0, 0, 0));
            return (new PostCampaignMigrationDto(
                [], [], [], 0, 0, 0, emptyRec, segment!.SegmentHash,
                segment.AfterStart!.Value, segment.AfterEnd!.Value, segment.BeforeStart!.Value, segment.BeforeEnd!.Value,
                DateTimeOffset.UtcNow), null);
        }

        var (beforeKeys, afterKeys) = await ComputeRfmKeysAsync(tenantId, storeIds, segment!, customerIds, ct);

        // Keyed by RANK (a plain, always-non-null int; RfmSegmentKey? itself can't be a Dictionary
        // key — Dictionary<TKey,TValue> requires TKey : notnull, and a nullable value type doesn't
        // satisfy that even though it would work fine at runtime) rather than the nullable
        // RfmSegmentKey? directly. KeyOfRank maps back for display once aggregation is done.
        var beforeDistribution = new Dictionary<int, int>();
        var afterDistribution = new Dictionary<int, int>();
        var matrix = new Dictionary<(int BeforeRank, int AfterRank), int>();
        int up = 0, stable = 0, down = 0;

        foreach (var id in customerIds)
        {
            var rankBefore = RankOf(beforeKeys[id]);
            var rankAfter = RankOf(afterKeys[id]);

            beforeDistribution[rankBefore] = beforeDistribution.GetValueOrDefault(rankBefore) + 1;
            afterDistribution[rankAfter] = afterDistribution.GetValueOrDefault(rankAfter) + 1;
            var cell = (rankBefore, rankAfter);
            matrix[cell] = matrix.GetValueOrDefault(cell) + 1;

            if (rankAfter < rankBefore) up++;
            else if (rankAfter == rankBefore) stable++;
            else down++;
        }

        var total = customerIds.Count;

        PostCampaignSegmentDistributionItemDto DistributionItem(KeyValuePair<int, int> kv)
        {
            var key = KeyOfRank(kv.Key);
            return new PostCampaignSegmentDistributionItemDto(key, LabelOf(key), kv.Value, SharePercent(kv.Value, total));
        }

        var beforeDist = beforeDistribution.Select(DistributionItem).OrderBy(d => RankOf(d.Segment)).ToList();
        var afterDist = afterDistribution.Select(DistributionItem).OrderBy(d => RankOf(d.Segment)).ToList();
        var matrixRows = matrix
            .Select(kv =>
            {
                var beforeKey = KeyOfRank(kv.Key.BeforeRank);
                var afterKey = KeyOfRank(kv.Key.AfterRank);
                return new PostCampaignMigrationCellDto(beforeKey, LabelOf(beforeKey), afterKey, LabelOf(afterKey), kv.Value);
            })
            .OrderBy(c => RankOf(c.Before)).ThenBy(c => RankOf(c.After))
            .ToList();

        var recommendation = PostCampaignRecommendationTemplates.BuildMigration(new PostCampaignMigrationRecommendationInputDto(up, stable, down));

        return (new PostCampaignMigrationDto(
            beforeDist, afterDist, matrixRows, up, stable, down, recommendation,
            segment!.SegmentHash, segment.AfterStart!.Value, segment.AfterEnd!.Value,
            segment.BeforeStart!.Value, segment.BeforeEnd!.Value, DateTimeOffset.UtcNow), null);
    }

    /// <summary>
    /// Shared RFM-before/after classification for both <see cref="GetCustomersAsync"/> and
    /// <see cref="GetMigrationAsync"/> — each still calls this independently (no cross-request
    /// caching, consistent with this whole feature area's "always recompute" rule); this only
    /// avoids duplicating the classification LOGIC across two large method bodies within the
    /// same request-handling code.
    ///
    /// Calls the EXISTING <see cref="IMarketingAnalyticsRepository.GetScoredCustomersAsync"/>
    /// THREE times: before-window, after-window, and — new to this feature — an ALL-TIME call
    /// (<see cref="DateOnly.MinValue"/> through the after-window's end, the exact same "all"
    /// period convention <c>MarketingAnalyticsController.ResolvePeriod</c> already uses) whose
    /// only purpose is telling apart, for a customer absent from a given window's scored rows,
    /// (a) genuinely zero all-time purchases — Фаза 1's dedicated "no purchase" bucket (null key),
    /// from (b) non-zero all-time history that simply falls outside that specific window — "just
    /// an ordinary low-R classification" per this task's brief, produced by feeding the SAME
    /// <see cref="RfmSegmentClassifier.Classify"/> sentinel worst-case R=F=M=1 scores together
    /// with the customer's REAL lifetime facts (borrowed from the all-time row — FirstPurchaseDateEver/
    /// LifetimeReceiptCount/LastPurchaseDateEver are window-independent lifetime facts per
    /// <see cref="RfmScoredCustomerRow"/>'s own doc comment, so any call's row is an equally valid
    /// source for them). No modification to <c>IMarketingAnalyticsRepository</c> itself — this is
    /// three ordinary calls to its existing, unmodified public method.
    /// </summary>
    private async Task<(IReadOnlyDictionary<Guid, RfmSegmentKey?> Before, IReadOnlyDictionary<Guid, RfmSegmentKey?> After)> ComputeRfmKeysAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, PostCampaignSegment segment, IReadOnlyList<Guid> customerIds, CancellationToken ct)
    {
        var customerIdSet = customerIds.ToHashSet();

        var beforeScored = await _marketingAnalytics.GetScoredCustomersAsync(tenantId, storeIds, segment.BeforeStart!.Value, segment.BeforeEnd!.Value, ct);
        var afterScored = await _marketingAnalytics.GetScoredCustomersAsync(tenantId, storeIds, segment.AfterStart!.Value, segment.AfterEnd!.Value, ct);
        var allTimeScored = await _marketingAnalytics.GetScoredCustomersAsync(tenantId, storeIds, DateOnly.MinValue, segment.AfterEnd!.Value, ct);

        var beforeById = beforeScored.Where(r => customerIdSet.Contains(r.CustomerId)).ToDictionary(r => r.CustomerId);
        var afterById = afterScored.Where(r => customerIdSet.Contains(r.CustomerId)).ToDictionary(r => r.CustomerId);
        var allTimeById = allTimeScored.Where(r => customerIdSet.Contains(r.CustomerId)).ToDictionary(r => r.CustomerId);

        var beforeKeys = new Dictionary<Guid, RfmSegmentKey?>();
        var afterKeys = new Dictionary<Guid, RfmSegmentKey?>();
        foreach (var id in customerIds)
        {
            beforeKeys[id] = ClassifyForWindow(id, beforeById, allTimeById, segment.BeforeEnd!.Value);
            afterKeys[id] = ClassifyForWindow(id, afterById, allTimeById, segment.AfterEnd!.Value);
        }

        return (beforeKeys, afterKeys);
    }

    private static RfmSegmentKey? ClassifyForWindow(
        Guid customerId,
        IReadOnlyDictionary<Guid, RfmScoredCustomerRow> windowedById,
        IReadOnlyDictionary<Guid, RfmScoredCustomerRow> allTimeById,
        DateOnly windowEnd)
    {
        if (windowedById.TryGetValue(customerId, out var row))
        {
            return RfmSegmentClassifier.Classify(
                row.RecencyScore, row.FrequencyScore, row.MonetaryScore,
                row.FirstPurchaseDateEver, row.LifetimeReceiptCount, row.LastPurchaseDateEver, windowEnd);
        }

        // Zero purchases in THIS window. Never appears in ANY window's scored rows -> zero
        // all-time purchases ever -> Фаза 1's "Без покупок" special case (null).
        if (!allTimeById.TryGetValue(customerId, out var allTime))
            return null;

        // Non-zero all-time history, just none in this specific window — "an ordinary low-R
        // classification" (brief's explicit instruction), fed through the SAME classifier with
        // sentinel worst-case R=F=M=1 and this customer's real lifetime facts.
        return RfmSegmentClassifier.Classify(1, 1, 1, allTime.FirstPurchaseDateEver, allTime.LifetimeReceiptCount, allTime.LastPurchaseDateEver, windowEnd);
    }

    private static readonly IReadOnlyDictionary<RfmSegmentKey, int> PriorityRank =
        RfmSegmentCatalog.AllKeysInPriorityOrder.Select((key, idx) => (key, idx)).ToDictionary(t => t.key, t => t.idx);

    /// <summary>"Без покупок" ranks worse than every named segment — going from no classification
    /// to any real one always reads as "up"; the reverse always reads as "down".</summary>
    private static int RankOf(RfmSegmentKey? key) => key is null ? RfmSegmentCatalog.AllKeysInPriorityOrder.Count : PriorityRank[key.Value];

    /// <summary>Inverse of <see cref="RankOf"/> — maps the plain-int rank used as a Dictionary key
    /// (see <see cref="GetMigrationAsync"/>'s remarks) back to the nullable wire-facing key.</summary>
    private static RfmSegmentKey? KeyOfRank(int rank) =>
        rank < RfmSegmentCatalog.AllKeysInPriorityOrder.Count ? RfmSegmentCatalog.AllKeysInPriorityOrder[rank] : null;

    private static string LabelOf(RfmSegmentKey? key) => key is null ? RfmSegmentCatalog.NoPurchaseLabelUa : RfmSegmentCatalog.LabelUa(key.Value);

    // ── Explain (AI) ─────────────────────────────────────────────────────────────────────────

    public async Task<(ExplainPostCampaignResultDto? Result, string? Error)> ExplainAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var (summary, error) = await GetSummaryAsync(tenantId, segmentId, storeIds, ct);
        if (error is not null) return (null, error);

        var segment = await _repo.GetSegmentAsync(tenantId, segmentId, ct);
        var context = new PostCampaignAdvisorContext(
            TitleUa: $"Пост-кампанійний сегмент \"{segment?.Name ?? "без назви"}\"",
            MatchedCount: summary!.MatchedCount,
            ReactivationRatePercent: summary.ReactivationRatePercent,
            RetentionRatePercent: summary.RetentionRatePercent,
            MoneyDeltaPercent: summary.MoneyDeltaPercent,
            ExtraContextUa: $"Утримано: {summary.Retained}, відпали: {summary.Dropped}, не повернулись: {summary.NotReturned}.",
            TemplateTriggerUa: summary.Recommendation.TriggerUa,
            TemplateActionUa: summary.Recommendation.ActionUa,
            TemplateOfferUa: summary.Recommendation.OfferUa,
            TemplateCautionUa: summary.Recommendation.CautionUa);

        var result = await _advisor.ExplainAsync(context, ct);
        return (new ExplainPostCampaignResultDto(result.ExplanationUa, result.Model, result.TokensUsed), null);
    }

    // ── Exports ──────────────────────────────────────────────────────────────────────────────

    public async Task<(PostCampaignExportResult? Result, string? Error)> ExportCustomersAsync(
        Guid tenantId, Guid userId, Guid segmentId, IReadOnlyList<Guid>? storeIds, bool unmaskPii, CancellationToken ct = default)
    {
        var (table, error) = await GetCustomersAsync(tenantId, segmentId, storeIds, 1, ExportMaxRows, null, true, unmaskPii, ct);
        if (error is not null) return (null, error);

        string[] headers = ["Ім'я", "Телефон", "Чеки до", "Чеки після", "Оборот до", "Оборот після", "Сегмент до", "Сегмент після"];
        var rows = table!.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Name, r.Phone, r.ChecksBefore, r.ChecksAfter, r.TurnoverBefore, r.TurnoverAfter,
            r.SegmentBeforeLabelUa, r.SegmentAfterLabelUa,
        }).ToList();

        var excelResult = _excelExport.Export(new ExcelExportRequest("Пост-кампанія", headers, rows, ExportMaxRows));
        var fileName = $"post_campaign_customers_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";

        await LogAsync(
            tenantId, userId, "marketing_analytics.post_campaign.export_customers",
            $"segmentId={segmentId}; rows={excelResult.RowCount}; truncated={excelResult.Truncated}; piiMasked={!unmaskPii}", ct);

        return (new PostCampaignExportResult(excelResult.FileBytes, fileName, excelResult.RowCount, excelResult.Truncated), null);
    }

    public async Task<(PostCampaignUnknownTokensExportResult? Result, string? Error)> ExportUnknownTokensAsync(
        Guid tenantId, Guid userId, Guid segmentId, CancellationToken ct = default)
    {
        var segment = await _repo.GetSegmentAsync(tenantId, segmentId, ct);
        if (segment is null) return (null, "Segment not found.");

        // TASK-478 fix (bug writeup: bug-task476-unknown-tokens-export-capped-at-20):
        // UnknownTokensSample/InvalidTokensSample are themselves capped at IMPORT time (see
        // SegmentImportParser.InvalidSampleCap / this class's own UnknownSampleCap) — a segment
        // whose real UnknownCount/InvalidCount exceeds that cap has fewer sample rows than the
        // true count, and this export must say so rather than silently look complete.
        var sampleTruncated =
            segment.UnknownTokensSample.Count < segment.UnknownCount ||
            segment.InvalidTokensSample.Count < segment.InvalidCount;

        string[] headers = ["Токен", "Тип"];
        var rows = new List<IReadOnlyList<object?>>();

        if (sampleTruncated)
        {
            // Mirrors IExcelExportService.Export's own truncation-banner convention (inserted as
            // the very first data row, right after the header, so it's the first thing a reader
            // sees) — that generic service only ever compares against its own 50k-row ceiling
            // (irrelevant here; this export is at most ~1,000 rows even at the new caps), so this
            // feature-specific sample-cap truncation has to be surfaced here instead, one layer
            // up, not inside the shared exporter.
            var note =
                $"Показано {segment.UnknownTokensSample.Count:N0} з {segment.UnknownCount:N0} невідомих та " +
                $"{segment.InvalidTokensSample.Count:N0} з {segment.InvalidCount:N0} некоректних токенів — " +
                "решта не збереглася (ліміт зразків при імпорті). Точні загальні кількості див. вище.";
            rows.Add(new object?[] { note, "Примітка" });
        }

        rows.AddRange(segment.UnknownTokensSample.Select(t => (IReadOnlyList<object?>)new object?[] { t, "Невідомий (не знайдено в системі)" }));
        rows.AddRange(segment.InvalidTokensSample.Select(t => (IReadOnlyList<object?>)new object?[] { t, "Некоректний формат" }));

        var excelResult = _excelExport.Export(new ExcelExportRequest("Помилки імпорту", headers, rows, ExportMaxRows));
        var fileName = $"post_campaign_import_errors_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";

        // No PII gate — these are uploader-supplied raw tokens (never resolved Customer PII),
        // but still ActivityLog-audited for consistency with every other export in this feature.
        await LogAsync(
            tenantId, userId, "marketing_analytics.post_campaign.export_unknown_tokens",
            $"segmentId={segmentId}; unknownSampleRows={segment.UnknownTokensSample.Count}; invalidSampleRows={segment.InvalidTokensSample.Count}; " +
            $"totalUnknown={segment.UnknownCount}; totalInvalid={segment.InvalidCount}; sampleTruncated={sampleTruncated}", ct);

        return (new PostCampaignUnknownTokensExportResult(
            excelResult.FileBytes, fileName, excelResult.RowCount,
            segment.UnknownCount, segment.InvalidCount, sampleTruncated || excelResult.Truncated), null);
    }

    // ── shared internals ─────────────────────────────────────────────────────────────────────

    private static bool IsAnalyzed(PostCampaignSegment s) => s.AnalyzedAt is not null;

    private async Task<(PostCampaignSegment? Segment, IReadOnlyList<Guid> CustomerIds, string? Error)> LoadAnalyzedAsync(
        Guid tenantId, Guid segmentId, CancellationToken ct)
    {
        var segment = await _repo.GetSegmentAsync(tenantId, segmentId, ct);
        if (segment is null) return (null, [], "Segment not found.");
        if (!IsAnalyzed(segment)) return (null, [], "Segment has not been analyzed yet. Call analyze first.");

        var customerIds = await _repo.GetMemberCustomerIdsAsync(tenantId, segmentId, ct);
        return (segment, customerIds, null);
    }

    private async Task<(IReadOnlyList<CustomerPeriodMetricsRow> Before, IReadOnlyList<CustomerPeriodMetricsRow> After)> LoadPeriodMetricsAsync(
        Guid tenantId, PostCampaignSegment segment, IReadOnlyList<Guid> customerIds, IReadOnlyList<Guid>? storeIds, CancellationToken ct)
    {
        if (customerIds.Count == 0) return ([], []);

        var (beforeFromUtc, beforeToUtc) = ToUtcRange(segment.BeforeStart!.Value, segment.BeforeEnd!.Value);
        var (afterFromUtc, afterToUtc) = ToUtcRange(segment.AfterStart!.Value, segment.AfterEnd!.Value);

        var before = await _repo.GetCustomerPeriodMetricsAsync(tenantId, customerIds, storeIds, beforeFromUtc, beforeToUtc, ct);
        var after = await _repo.GetCustomerPeriodMetricsAsync(tenantId, customerIds, storeIds, afterFromUtc, afterToUtc, ct);
        return (before, after);
    }

    // Npgsql rejects DateTime with Kind=Unspecified as a parameter for timestamptz columns —
    // mirrors every sibling MarketingAnalytics repository/service's own ToUtcRange exactly.
    private static (DateTime FromUtc, DateTime ToUtc) ToUtcRange(DateOnly from, DateOnly to) => (
        from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize) =>
        (Math.Max(1, page), pageSize is < 1 or > MaxPageSize ? DefaultPageSize : pageSize);

    private static int TotalPages(int totalCount, int pageSize) =>
        pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

    private static decimal SharePercent(int numerator, int denominator) =>
        denominator <= 0 ? 0m : Math.Round(numerator / (decimal)denominator * 100m, 2);

    private async Task LogAsync(Guid tenantId, Guid userId, string action, string meta, CancellationToken ct,
        string entityType = "marketing_analytics_export")
    {
        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            Meta = meta,
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);
    }
}
