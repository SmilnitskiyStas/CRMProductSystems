using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
// PiiMasking (internal static, same assembly) lives one namespace up — TASK-420's move made it
// shared between MarketingAnalyticsService and this service; explicit using kept for clarity
// even though C# would also resolve it via enclosing-namespace fallback.
using ShelfGuard.Application.Features.MarketingAnalytics;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;

/// <summary>
/// Thin orchestration service for Фаза 2 (TASK-420, design doc — plan
/// `C:\Users\stass\.claude\plans\deep-cooking-nygaard.md` §"Фази 2-4") — same "thin service →
/// repository" shape as <see cref="MarketingAnalyticsService"/>. Overview/KPI endpoints fetch the
/// FULL filtered customer population and classify/aggregate in C# via the pure classifiers
/// (mirrors Фаза 1's <c>ClassifyAllAsync</c> — acceptable here too since only aggregated numbers
/// go over the wire); the three paginated TABLE endpoints never do this — they delegate real
/// classification/filtering/sorting/pagination to the repository's SQL (design doc §2's
/// explicit Фаза-1-vs-Фаза-2 distinction) and this service only maps rows to DTOs.
/// </summary>
public sealed class PriceSegmentsService : IPriceSegmentsService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;
    private const int ExportMaxRows = 50_000;

    private readonly IPriceSegmentsRepository _repo;
    private readonly IPriceSegmentAdvisor _advisor;
    private readonly IExcelExportService _excel;
    private readonly IActivityLogRepository _activityLogs;

    public PriceSegmentsService(
        IPriceSegmentsRepository repo, IPriceSegmentAdvisor advisor, IExcelExportService excel, IActivityLogRepository activityLogs)
    {
        _repo = repo;
        _advisor = advisor;
        _excel = excel;
        _activityLogs = activityLogs;
    }

    // ── Comparison mode ──────────────────────────────────────────────────────────────────────

    public async Task<PriceSegmentsOverviewDto> GetOverviewAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var (previousFrom, previousTo) = ComputePreviousPeriod(from, to);
        var boundaries = await _repo.GetBoundariesAsync(tenantId, storeIds, ct);

        var currentMetrics = await _repo.GetCustomerPeriodMetricsAsync(tenantId, storeIds, from, to, ct);
        var previousMetrics = await _repo.GetCustomerPeriodMetricsAsync(tenantId, storeIds, previousFrom, previousTo, ct);

        var currentMap = currentMetrics.ToDictionary(r => r.CustomerId);
        var previousMap = previousMetrics.ToDictionary(r => r.CustomerId);

        // Comparison cohort ("Проаналізовано") = bought in BOTH windows (analysis doc §5.3).
        var cohort = currentMap.Keys.Intersect(previousMap.Keys)
            .Select(id =>
            {
                var cur = currentMap[id];
                var prev = previousMap[id];
                var curSeg = PriceSegmentClassifier.Classify(cur.MedianCheck, boundaries);
                var prevSeg = PriceSegmentClassifier.Classify(prev.MedianCheck, boundaries);
                var audience = PriceAudienceClassifier.Classify(prevSeg, curSeg, prev.ItemsPerReceipt, cur.ItemsPerReceipt);
                return (CustomerId: id, Audience: audience);
            })
            .ToList();

        var raisedCount = cohort.Count(r => r.Audience is PriceAudienceKey.RealGrowth or PriceAudienceKey.PriceGrowth);
        var declinedCount = cohort.Count(r => r.Audience == PriceAudienceKey.Declining);
        var stableCount = cohort.Count(r => r.Audience == PriceAudienceKey.Stable);

        // Distribution chart: ALL active buyers of EACH period, not just the cohort — analysis
        // doc §6.2 confirms this is a deliberately different (larger) denominator than "Проаналізовано".
        var currentDistribution = PriceSegmentCatalog.AllTiersInOrder.ToDictionary(t => t, _ => 0);
        foreach (var row in currentMetrics)
            currentDistribution[PriceSegmentClassifier.Classify(row.MedianCheck, boundaries)]++;
        var previousDistribution = PriceSegmentCatalog.AllTiersInOrder.ToDictionary(t => t, _ => 0);
        foreach (var row in previousMetrics)
            previousDistribution[PriceSegmentClassifier.Classify(row.MedianCheck, boundaries)]++;

        var distribution = PriceSegmentCatalog.AllTiersInOrder
            .Select(t => new PriceSegmentDistributionPointDto(
                t, PriceSegmentCatalog.RangeLabelUa(t, boundaries), currentDistribution[t], previousDistribution[t]))
            .ToList();

        var cohortIds = cohort.Select(c => c.CustomerId).ToList();
        var ltvRows = cohortIds.Count == 0 ? [] : await _repo.GetLtvMapAsync(tenantId, cohortIds, storeIds, ct);
        var ltvMap = ltvRows.ToDictionary(r => r.CustomerId, r => r.Ltv);

        var audiences = PriceSegmentCatalog.AllPriceAudiences.Select(a =>
        {
            var group = cohort.Where(r => r.Audience == a).ToList();
            var avgLtv = group.Count == 0 ? 0m : group.Average(r => ltvMap.GetValueOrDefault(r.CustomerId, 0m));
            return new PriceAudienceSummaryDto(
                a, PriceSegmentCatalog.LabelUa(a), group.Count, SharePercent(group.Count, cohortIds.Count), Math.Round(avgLtv, 2));
        }).ToList();

        var currentAvgUnitPrice = await _repo.GetAverageUnitPriceAsync(tenantId, storeIds, from, to, ct);
        var previousAvgUnitPrice = await _repo.GetAverageUnitPriceAsync(tenantId, storeIds, previousFrom, previousTo, ct);
        var priceIndex = previousAvgUnitPrice <= 0 ? 0m : Math.Round((currentAvgUnitPrice / previousAvgUnitPrice - 1) * 100m, 2);

        return new PriceSegmentsOverviewDto(
            from, to, previousFrom, previousTo,
            cohortIds.Count, currentMetrics.Count, previousMetrics.Count,
            raisedCount, declinedCount, stableCount, priceIndex,
            distribution, audiences,
            PriceSegmentFilterHash.Compute(tenantId, storeIds, from, to, previousFrom, previousTo),
            DateTimeOffset.UtcNow);
    }

    public async Task<PriceAudienceTableDto> GetAudienceTableAsync(
        Guid tenantId, PriceAudienceTableRequest request, CancellationToken ct = default)
    {
        var (previousFrom, previousTo) = ComputePreviousPeriod(request.From, request.To);
        var boundaries = await _repo.GetBoundariesAsync(tenantId, request.StoreIds, ct);
        var (page, pageSize) = NormalizePaging(request.Page, request.PageSize);

        var rows = await _repo.GetPriceAudienceTableAsync(
            tenantId, request.StoreIds, request.From, request.To, previousFrom, previousTo,
            boundaries, (int)request.Audience, page, pageSize, request.SortBy, request.SortDescending, ct);

        var (_, recommendation) = BuildPriceAudienceRecommendation(request.Audience, rows);
        var first = rows.FirstOrDefault();

        var dtoRows = rows.Select(r =>
        {
            var prevSeg = PriceSegmentCatalog.FromOrdinal(r.PreviousSegmentOrdinal);
            var curSeg = PriceSegmentCatalog.FromOrdinal(r.CurrentSegmentOrdinal);
            return new PriceAudienceRowDto(
                r.CustomerId, r.Name, MaskPhoneUnlessAuthorized(r.Phone, request.CanViewUnmaskedPii),
                prevSeg, PriceSegmentCatalog.RangeLabelUa(prevSeg, boundaries),
                curSeg, PriceSegmentCatalog.RangeLabelUa(curSeg, boundaries),
                r.ItemsPerReceiptPrevious, r.ItemsPerReceiptCurrent, r.TypicalCheckCurrent, r.Ltv);
        }).ToList();

        return new PriceAudienceTableDto(
            request.Audience, PriceSegmentCatalog.LabelUa(request.Audience),
            first?.TotalCount ?? 0, first?.WithPhoneCount ?? 0, dtoRows,
            page, pageSize, TotalPages(first?.TotalCount ?? 0, pageSize),
            PriceSegmentSortKeys.NormalizePriceAudience(request.SortBy), request.SortDescending,
            recommendation,
            PriceSegmentFilterHash.Compute(tenantId, request.StoreIds, request.Audience, request.From, request.To),
            DateTimeOffset.UtcNow);
    }

    public async Task<ExplainPriceSegmentResultDto> ExplainAudienceAsync(
        Guid tenantId, PriceAudienceKey audience, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        var (previousFrom, previousTo) = ComputePreviousPeriod(from, to);
        var boundaries = await _repo.GetBoundariesAsync(tenantId, storeIds, ct);

        // Probe (pageSize=1) purely for the window-aggregate KPIs — the same aggregates repeat
        // on every row regardless of page, so we never need more than 1 to read them.
        var probe = await _repo.GetPriceAudienceTableAsync(
            tenantId, storeIds, from, to, previousFrom, previousTo, boundaries, (int)audience, 1, 1, null, false, ct);
        var (input, recommendation) = BuildPriceAudienceRecommendation(audience, probe);

        var context = new PriceSegmentAdvisorContext(
            PriceSegmentCatalog.LabelUa(audience), input.CustomerCount, input.SharePercentOfAnalyzed, input.AverageLtv, null,
            recommendation.TriggerUa, recommendation.ActionUa, recommendation.OfferUa, recommendation.CautionUa);

        var result = await _advisor.ExplainAsync(context, ct);
        return new ExplainPriceSegmentResultDto(result.ExplanationUa, result.Model, result.TokensUsed);
    }

    public async Task<PriceSegmentExportResult> ExportAudienceAsync(
        Guid tenantId, Guid userId, ExportPriceAudienceRequest request, CancellationToken ct = default)
    {
        var (previousFrom, previousTo) = ComputePreviousPeriod(request.From, request.To);
        var boundaries = await _repo.GetBoundariesAsync(tenantId, request.StoreIds, ct);

        var rows = await _repo.GetPriceAudienceTableAsync(
            tenantId, request.StoreIds, request.From, request.To, previousFrom, previousTo,
            boundaries, (int)request.Audience, 1, ExportMaxRows, null, false, ct);

        var result = BuildPriceAudienceExcel(request.Audience, rows, boundaries, request.UnmaskPii);

        await LogExportAsync(
            tenantId, userId, "marketing_analytics.price_segments.export_audience",
            $"audience={request.Audience}; from={request.From:yyyy-MM-dd}; to={request.To:yyyy-MM-dd}; " +
            $"stores={StoresLabel(request.StoreIds)}; rows={result.RowCount}; truncated={result.Truncated}; " +
            $"piiMasked={!request.UnmaskPii}", ct);

        return result;
    }

    // ── All-time mode ────────────────────────────────────────────────────────────────────────

    public async Task<PriceSegmentsAllTimeOverviewDto> GetAllTimeOverviewAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, PriceSegmentKey? selectedSegment, CancellationToken ct = default)
    {
        var (kpi, _, distribution) = await BuildAllTimeDistributionAsync(tenantId, storeIds, ct);
        var monthly = await _repo.GetMonthlyTrendAsync(tenantId, storeIds, ct);
        var insights = BuildInsights(monthly);

        PriceSegmentRecommendationDto? recommendation = null;
        if (selectedSegment is { } seg)
        {
            var segRow = distribution.First(d => d.Segment == seg);
            var input = new AllTimeSegmentRecommendationInputDto(seg, segRow.RangeLabelUa, segRow.CustomerCount, segRow.AverageLtv);
            recommendation = PriceSegmentRecommendationTemplates.BuildAllTimeSegment(input);
        }

        return new PriceSegmentsAllTimeOverviewDto(
            kpi.CustomersInBase, Math.Round(kpi.NetworkAverageCheck, 2), kpi.PurchasesTotal, kpi.TurnoverTotal,
            monthly.Select(m => new MedianCheckMonthPointDto(m.Year, m.Month, m.MedianCheck, Math.Round(m.ItemsPerReceipt, 2))).ToList(),
            insights, distribution, selectedSegment, recommendation,
            PriceSegmentFilterHash.Compute(tenantId, storeIds, "all-time", selectedSegment),
            DateTimeOffset.UtcNow);
    }

    public async Task<AllTimeCustomerTableDto> GetAllTimeCustomerTableAsync(
        Guid tenantId, AllTimeCustomerTableRequest request, CancellationToken ct = default)
    {
        var boundaries = await _repo.GetBoundariesAsync(tenantId, request.StoreIds, ct);
        var segmentOrdinal = request.Segment is { } s ? (int)s : (int?)null;
        var (page, pageSize) = NormalizePaging(request.Page, request.PageSize);

        var rows = await _repo.GetAllTimeCustomerTableAsync(
            tenantId, request.StoreIds, boundaries, segmentOrdinal, page, pageSize, request.SortBy, request.SortDescending, ct);

        var dtoRows = rows.Select(r =>
        {
            var seg = PriceSegmentCatalog.FromOrdinal(r.SegmentOrdinal);
            return new AllTimeCustomerRowDto(
                r.CustomerId, r.Name, MaskPhoneUnlessAuthorized(r.Phone, request.CanViewUnmaskedPii),
                seg, PriceSegmentCatalog.RangeLabelUa(seg, boundaries),
                r.ItemsPerReceipt, r.TypicalCheck, r.PurchaseCount, r.Ltv);
        }).ToList();

        var totalCount = rows.FirstOrDefault()?.TotalCount ?? 0;

        return new AllTimeCustomerTableDto(
            request.Segment, totalCount, dtoRows, page, pageSize, TotalPages(totalCount, pageSize),
            PriceSegmentSortKeys.NormalizeAllTime(request.SortBy), request.SortDescending,
            PriceSegmentFilterHash.Compute(tenantId, request.StoreIds, "all-time-customers", request.Segment),
            DateTimeOffset.UtcNow);
    }

    public async Task<ExplainPriceSegmentResultDto> ExplainAllTimeSegmentAsync(
        Guid tenantId, PriceSegmentKey segment, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var (kpi, _, distribution) = await BuildAllTimeDistributionAsync(tenantId, storeIds, ct);
        var segRow = distribution.First(d => d.Segment == segment);
        var input = new AllTimeSegmentRecommendationInputDto(segment, segRow.RangeLabelUa, segRow.CustomerCount, segRow.AverageLtv);
        var recommendation = PriceSegmentRecommendationTemplates.BuildAllTimeSegment(input);

        var context = new PriceSegmentAdvisorContext(
            segRow.RangeLabelUa, segRow.CustomerCount, SharePercent(segRow.CustomerCount, kpi.CustomersInBase), segRow.AverageLtv, null,
            recommendation.TriggerUa, recommendation.ActionUa, recommendation.OfferUa, recommendation.CautionUa);

        var result = await _advisor.ExplainAsync(context, ct);
        return new ExplainPriceSegmentResultDto(result.ExplanationUa, result.Model, result.TokensUsed);
    }

    public async Task<PriceSegmentExportResult> ExportAllTimeAsync(
        Guid tenantId, Guid userId, ExportAllTimeRequest request, CancellationToken ct = default)
    {
        var boundaries = await _repo.GetBoundariesAsync(tenantId, request.StoreIds, ct);
        var segmentOrdinal = request.Segment is { } s ? (int)s : (int?)null;

        var rows = await _repo.GetAllTimeCustomerTableAsync(
            tenantId, request.StoreIds, boundaries, segmentOrdinal, 1, ExportMaxRows, null, false, ct);

        var result = BuildAllTimeExcel(rows, boundaries, request.UnmaskPii);

        await LogExportAsync(
            tenantId, userId, "marketing_analytics.price_segments.export_all_time",
            $"segment={(request.Segment is null ? "all" : request.Segment.ToString())}; stores={StoresLabel(request.StoreIds)}; " +
            $"rows={result.RowCount}; truncated={result.Truncated}; piiMasked={!request.UnmaskPii}", ct);

        return result;
    }

    // ── Frequency & reactivation mode ────────────────────────────────────────────────────────

    public async Task<FrequencyOverviewDto> GetFrequencyOverviewAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to,
        decimal? declineThresholdPercent, CancellationToken ct = default)
    {
        var threshold = await ResolveDeclineThresholdAsync(tenantId, declineThresholdPercent, ct);
        var (previousFrom, previousTo) = ComputePreviousPeriod(from, to);

        var currentMetrics = await _repo.GetCustomerPeriodMetricsAsync(tenantId, storeIds, from, to, ct);
        var previousMetrics = await _repo.GetCustomerPeriodMetricsAsync(tenantId, storeIds, previousFrom, previousTo, ct);

        var currentMap = currentMetrics.ToDictionary(r => r.CustomerId);
        var previousMap = previousMetrics.ToDictionary(r => r.CustomerId);
        var unionIds = currentMap.Keys.Union(previousMap.Keys).ToList();

        var classified = unionIds.Select(id =>
        {
            var curFreq = currentMap.TryGetValue(id, out var c) ? c.ReceiptCount : 0;
            var prevFreq = previousMap.TryGetValue(id, out var p) ? p.ReceiptCount : 0;
            return (CustomerId: id, Audience: FrequencyAudienceClassifier.Classify(prevFreq, curFreq, threshold));
        }).ToList();

        // "Активна" (current-buyer) population is the base for the frequency/spend averages —
        // analysis doc §16.1 pairs "Активних покупців" with "Середня частота"/"Було" under the
        // SAME denominator; a customer absent from the current window contributes 0 to "Було"
        // rather than being excluded (design doc §30: competitor doesn't disclose this, documented
        // product decision).
        var activeCurrentIds = currentMap.Keys.ToList();
        var activeCurrentCount = activeCurrentIds.Count;
        var averageFrequencyCurrent = activeCurrentCount == 0 ? 0m : (decimal)activeCurrentIds.Average(id => currentMap[id].ReceiptCount);
        var averageFrequencyPrevious = activeCurrentCount == 0
            ? 0m
            : (decimal)activeCurrentIds.Average(id => previousMap.TryGetValue(id, out var p) ? p.ReceiptCount : 0);
        var averageSpendCurrent = activeCurrentCount == 0 ? 0m : activeCurrentIds.Average(id => currentMap[id].TotalSpend);

        var activePreviousCount = previousMap.Count;
        var activeChangePercent = activePreviousCount <= 0
            ? 0m
            : Math.Round((activeCurrentCount / (decimal)activePreviousCount - 1) * 100m, 2);

        var atRiskCount = classified.Count(c => c.Audience is FrequencyAudienceKey.Sleeping or FrequencyAudienceKey.Declining);

        var ltvRows = unionIds.Count == 0 ? [] : await _repo.GetLtvMapAsync(tenantId, unionIds, storeIds, ct);
        var ltvMap = ltvRows.ToDictionary(r => r.CustomerId, r => r.Ltv);

        var audiences = PriceSegmentCatalog.AllFrequencyAudiences.Select(a =>
        {
            var group = classified.Where(c => c.Audience == a).ToList();
            var avgLtv = group.Count == 0 ? 0m : group.Average(c => ltvMap.GetValueOrDefault(c.CustomerId, 0m));
            return new FrequencyAudienceSummaryDto(
                a, PriceSegmentCatalog.LabelUa(a), group.Count, SharePercent(group.Count, unionIds.Count), Math.Round(avgLtv, 2));
        }).ToList();

        return new FrequencyOverviewDto(
            from, to, previousFrom, previousTo,
            activeCurrentCount, activeChangePercent, Math.Round(averageFrequencyCurrent, 2), Math.Round(averageFrequencyPrevious, 2),
            unionIds.Count, atRiskCount, SharePercent(atRiskCount, unionIds.Count), SharePercent(atRiskCount, activeCurrentCount),
            Math.Round(averageSpendCurrent, 2), audiences,
            PriceSegmentFilterHash.Compute(tenantId, storeIds, from, to, threshold),
            DateTimeOffset.UtcNow);
    }

    public async Task<FrequencyAudienceTableDto> GetFrequencyAudienceTableAsync(
        Guid tenantId, FrequencyAudienceTableRequest request, CancellationToken ct = default)
    {
        var threshold = await ResolveDeclineThresholdAsync(tenantId, request.DeclineThresholdPercent, ct);
        var (previousFrom, previousTo) = ComputePreviousPeriod(request.From, request.To);
        var boundaries = await _repo.GetBoundariesAsync(tenantId, request.StoreIds, ct);
        var useCurrentBasis = request.Audience != FrequencyAudienceKey.Sleeping;
        var (page, pageSize) = NormalizePaging(request.Page, request.PageSize);
        var segmentOrdinal = request.PriceSegmentFilter is { } seg ? (int)seg : (int?)null;

        var rows = await _repo.GetFrequencyAudienceTableAsync(
            tenantId, request.StoreIds, request.From, request.To, previousFrom, previousTo,
            threshold, boundaries, (int)request.Audience, useCurrentBasis,
            request.MinSpend, request.MaxSpend, segmentOrdinal,
            page, pageSize, request.SortBy, request.SortDescending, ct);

        var (_, recommendation) = BuildFrequencyAudienceRecommendation(request.Audience, rows);
        var first = rows.FirstOrDefault();

        var dtoRows = rows.Select(r => new FrequencyAudienceRowDto(
            r.CustomerId, r.Name, MaskPhoneUnlessAuthorized(r.Phone, request.CanViewUnmaskedPii),
            r.PreviousFrequency, r.CurrentFrequency, r.FrequencyDeltaAbsolute,
            r.FrequencyDeltaPercent is { } p ? Math.Round(p, 2) : null, r.TypicalCheckCurrent, r.SpendCurrentPeriod, r.Ltv))
            .ToList();

        return new FrequencyAudienceTableDto(
            request.Audience, PriceSegmentCatalog.LabelUa(request.Audience),
            first?.TotalCount ?? 0, first?.WithPhoneCount ?? 0, dtoRows,
            page, pageSize, TotalPages(first?.TotalCount ?? 0, pageSize),
            PriceSegmentSortKeys.NormalizeFrequency(request.SortBy), request.SortDescending,
            recommendation,
            PriceSegmentFilterHash.Compute(
                tenantId, request.StoreIds, request.Audience, request.From, request.To, threshold,
                request.MinSpend, request.MaxSpend, request.PriceSegmentFilter),
            DateTimeOffset.UtcNow);
    }

    public async Task<ExplainPriceSegmentResultDto> ExplainFrequencyAudienceAsync(
        Guid tenantId, FrequencyAudienceKey audience, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to,
        decimal? declineThresholdPercent, CancellationToken ct = default)
    {
        var threshold = await ResolveDeclineThresholdAsync(tenantId, declineThresholdPercent, ct);
        var (previousFrom, previousTo) = ComputePreviousPeriod(from, to);
        var boundaries = await _repo.GetBoundariesAsync(tenantId, storeIds, ct);
        var useCurrentBasis = audience != FrequencyAudienceKey.Sleeping;

        var probe = await _repo.GetFrequencyAudienceTableAsync(
            tenantId, storeIds, from, to, previousFrom, previousTo, threshold, boundaries, (int)audience,
            useCurrentBasis, null, null, null, 1, 1, null, false, ct);

        var (input, recommendation) = BuildFrequencyAudienceRecommendation(audience, probe);

        var context = new PriceSegmentAdvisorContext(
            PriceSegmentCatalog.LabelUa(audience), input.CustomerCount, input.SharePercent, input.AverageLtv,
            $"Частота: {input.AverageFrequencyPrevious:0.#} \u2192 {input.AverageFrequencyCurrent:0.#} покупок за період",
            recommendation.TriggerUa, recommendation.ActionUa, recommendation.OfferUa, recommendation.CautionUa);

        var result = await _advisor.ExplainAsync(context, ct);
        return new ExplainPriceSegmentResultDto(result.ExplanationUa, result.Model, result.TokensUsed);
    }

    public async Task<PriceSegmentExportResult> ExportFrequencyAudienceAsync(
        Guid tenantId, Guid userId, ExportFrequencyAudienceRequest request, CancellationToken ct = default)
    {
        var threshold = await ResolveDeclineThresholdAsync(tenantId, request.DeclineThresholdPercent, ct);
        var (previousFrom, previousTo) = ComputePreviousPeriod(request.From, request.To);
        var boundaries = await _repo.GetBoundariesAsync(tenantId, request.StoreIds, ct);
        var useCurrentBasis = request.Audience != FrequencyAudienceKey.Sleeping;
        var segmentOrdinal = request.PriceSegmentFilter is { } seg ? (int)seg : (int?)null;

        var rows = await _repo.GetFrequencyAudienceTableAsync(
            tenantId, request.StoreIds, request.From, request.To, previousFrom, previousTo,
            threshold, boundaries, (int)request.Audience, useCurrentBasis,
            request.MinSpend, request.MaxSpend, segmentOrdinal, 1, ExportMaxRows, null, false, ct);

        var result = BuildFrequencyExcel(request.Audience, rows, request.UnmaskPii);

        await LogExportAsync(
            tenantId, userId, "marketing_analytics.price_segments.export_frequency_audience",
            $"audience={request.Audience}; from={request.From:yyyy-MM-dd}; to={request.To:yyyy-MM-dd}; " +
            $"stores={StoresLabel(request.StoreIds)}; declineThreshold={threshold}; rows={result.RowCount}; " +
            $"truncated={result.Truncated}; piiMasked={!request.UnmaskPii}", ct);

        return result;
    }

    // ── Settings ──────────────────────────────────────────────────────────────────────────────

    public async Task<PriceSegmentSettingsDto> GetSettingsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var settings = await _repo.GetSettingsAsync(tenantId, ct);
        var isNew = settings is null;
        settings ??= new PriceSegmentSettings { TenantId = tenantId };
        return ToSettingsDto(settings, isNew);
    }

    public async Task<(PriceSegmentSettingsDto? Settings, string? Error)> UpsertSettingsAsync(
        Guid tenantId, UpsertPriceSegmentSettingsRequest request, CancellationToken ct = default)
    {
        if (request.DefaultFrequencyDeclineThresholdPercent < 0 || request.DefaultFrequencyDeclineThresholdPercent > 100)
            return (null, "DefaultFrequencyDeclineThresholdPercent must be between 0 and 100.");
        if (request.MinReceiptsForBoundaries is < 0)
            return (null, "MinReceiptsForBoundaries cannot be negative.");

        var settings = await _repo.GetSettingsAsync(tenantId, ct);
        if (settings is null)
        {
            settings = new PriceSegmentSettings { TenantId = tenantId };
            ApplyRequest(settings, request);
            await _repo.AddSettingsAsync(settings, ct);
        }
        else
        {
            ApplyRequest(settings, request);
            _repo.UpdateSettings(settings);
        }

        await _repo.SaveChangesAsync(ct);
        return (ToSettingsDto(settings), null);
    }

    // ── shared internals ─────────────────────────────────────────────────────────────────────

    /// <summary>Design doc §4.3: the previous window is the same length, immediately preceding
    /// (no gap, no overlap) — <c>previous_end = current_start - 1 day; previous_start =
    /// previous_end - (duration - 1) days</c>.</summary>
    private static (DateOnly PreviousFrom, DateOnly PreviousTo) ComputePreviousPeriod(DateOnly from, DateOnly to)
    {
        var durationDays = to.DayNumber - from.DayNumber + 1;
        var previousTo = from.AddDays(-1);
        var previousFrom = previousTo.AddDays(-(durationDays - 1));
        return (previousFrom, previousTo);
    }

    private async Task<decimal> ResolveDeclineThresholdAsync(Guid tenantId, decimal? requested, CancellationToken ct)
    {
        if (requested is { } r) return r;
        var settings = await _repo.GetSettingsAsync(tenantId, ct);
        return settings?.DefaultFrequencyDeclineThresholdPercent ?? new PriceSegmentSettings().DefaultFrequencyDeclineThresholdPercent;
    }

    private async Task<(AllTimeKpiRow Kpi, PriceSegmentBoundariesRow Boundaries, IReadOnlyList<AllTimeSegmentDistributionDto> Distribution)>
        BuildAllTimeDistributionAsync(Guid tenantId, IReadOnlyList<Guid>? storeIds, CancellationToken ct)
    {
        var boundaries = await _repo.GetBoundariesAsync(tenantId, storeIds, ct);
        var kpi = await _repo.GetAllTimeKpiAsync(tenantId, storeIds, ct);
        var distributionRows = await _repo.GetAllTimeDistributionAsync(tenantId, storeIds, boundaries, ct);
        var distributionMap = distributionRows.ToDictionary(r => PriceSegmentCatalog.FromOrdinal(r.SegmentOrdinal));

        var distribution = PriceSegmentCatalog.AllTiersInOrder.Select(t =>
        {
            var row = distributionMap.GetValueOrDefault(t);
            return new AllTimeSegmentDistributionDto(
                t, PriceSegmentCatalog.RangeLabelUa(t, boundaries), row?.CustomerCount ?? 0, Math.Round(row?.AverageLtv ?? 0m, 2));
        }).ToList();

        return (kpi, boundaries, distribution);
    }

    /// <summary>Design doc §12 insights — all null when there isn't enough monthly history yet
    /// (never a divide-by-zero, never a misleading 0%). YoY and items/receipt-change compare the
    /// latest month against the SAME calendar month one year back (calendar-matched, not index-
    /// offset — correct even if some month in between had zero receipts and is therefore absent
    /// from <paramref name="monthly"/>); the "last 3 months" trend follows the identical
    /// calendar-matched approach for the exact same reason. The competitor never discloses its
    /// own precise math here (design doc §30) — this is a documented, defensible product choice,
    /// not a reverse-engineered fact.</summary>
    private static AllTimeInsightsDto BuildInsights(IReadOnlyList<MedianCheckMonthRow> monthly)
    {
        if (monthly.Count == 0)
            return new AllTimeInsightsDto(null, null, null, null, null);

        var ordered = monthly.OrderBy(m => m.Year).ThenBy(m => m.Month).ToList();
        var latest = ordered[^1];

        var yoyBase = FindMonth(ordered, latest.Year - 1, latest.Month);
        decimal? yoyPercent = yoyBase is null || yoyBase.MedianCheck <= 0
            ? null : Math.Round((latest.MedianCheck / yoyBase.MedianCheck - 1) * 100m, 2);
        decimal? itemsChangePercent = yoyBase is null || yoyBase.ItemsPerReceipt <= 0
            ? null : Math.Round((latest.ItemsPerReceipt / yoyBase.ItemsPerReceipt - 1) * 100m, 2);

        var threeMonthsAgo = new DateOnly(latest.Year, latest.Month, 1).AddMonths(-3);
        var threeBack = FindMonth(ordered, threeMonthsAgo.Year, threeMonthsAgo.Month);
        decimal? last3MonthsTrendPercent = threeBack is null || threeBack.MedianCheck <= 0
            ? null : Math.Round((latest.MedianCheck / threeBack.MedianCheck - 1) * 100m, 2);

        var peak = ordered.Max(m => m.MedianCheck);
        decimal? belowPeakPercent = peak <= 0 ? null : Math.Round((latest.MedianCheck / peak - 1) * 100m, 2);

        return new AllTimeInsightsDto(yoyPercent, last3MonthsTrendPercent, belowPeakPercent, peak, itemsChangePercent);
    }

    private static MedianCheckMonthRow? FindMonth(IReadOnlyList<MedianCheckMonthRow> ordered, int year, int month) =>
        ordered.FirstOrDefault(m => m.Year == year && m.Month == month);

    private static (PriceAudienceRecommendationInputDto Input, PriceSegmentRecommendationDto Recommendation) BuildPriceAudienceRecommendation(
        PriceAudienceKey audience, IReadOnlyList<PriceAudienceTableRowRaw> rows)
    {
        var first = rows.FirstOrDefault();
        var input = new PriceAudienceRecommendationInputDto(
            audience, first?.TotalCount ?? 0,
            SharePercent(first?.TotalCount ?? 0, first?.AnalyzedCount ?? 0),
            Math.Round(first?.AverageLtvOverall ?? 0m, 2));
        return (input, PriceSegmentRecommendationTemplates.BuildPriceAudience(input));
    }

    private static (FrequencyAudienceRecommendationInputDto Input, PriceSegmentRecommendationDto Recommendation) BuildFrequencyAudienceRecommendation(
        FrequencyAudienceKey audience, IReadOnlyList<FrequencyAudienceTableRowRaw> rows)
    {
        var first = rows.FirstOrDefault();
        var input = new FrequencyAudienceRecommendationInputDto(
            audience, first?.TotalCount ?? 0,
            SharePercent(first?.TotalCount ?? 0, first?.UnionPopulationCount ?? 0),
            Math.Round(first?.AverageLtvOverall ?? 0m, 2),
            Math.Round(first?.AveragePreviousFrequencyOverall ?? 0m, 2),
            Math.Round(first?.AverageCurrentFrequencyOverall ?? 0m, 2));
        return (input, PriceSegmentRecommendationTemplates.BuildFrequencyAudience(input));
    }

    private static void ApplyRequest(PriceSegmentSettings settings, UpsertPriceSegmentSettingsRequest request)
    {
        settings.DefaultFrequencyDeclineThresholdPercent = request.DefaultFrequencyDeclineThresholdPercent;
        settings.MinReceiptsForBoundaries = request.MinReceiptsForBoundaries;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static PriceSegmentSettingsDto ToSettingsDto(PriceSegmentSettings settings, bool isNew = false) =>
        new(settings.DefaultFrequencyDeclineThresholdPercent, settings.MinReceiptsForBoundaries, isNew ? null : settings.UpdatedAt);

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize) =>
        (Math.Max(1, page), pageSize is < 1 or > MaxPageSize ? DefaultPageSize : pageSize);

    private static int TotalPages(int totalCount, int pageSize) =>
        pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

    private static string StoresLabel(IReadOnlyList<Guid>? storeIds) =>
        storeIds is { Count: > 0 } ? string.Join(",", storeIds) : "all";

    /// <summary>
    /// TASK-425 (QA finding, TASK-424): the 3 paginated on-screen tables (comparison audience /
    /// all-time customer / frequency audience) were returning <c>Phone</c> unmasked
    /// unconditionally — masking previously existed only inside the 3 Excel export builders below.
    /// This mirrors that same masking, applied by default to every GET-table row too, gated by the
    /// SAME capability the export path already checks (<c>MarketingAnalyticsAuthorization.
    /// CanExportPii</c>, resolved server-side by the controller and threaded through as
    /// <c>CanViewUnmaskedPii</c> on the request — see that field's doc for why it can't be checked
    /// here directly: Application has no reference to Infrastructure).
    /// </summary>
    private static string? MaskPhoneUnlessAuthorized(string? phone, bool canViewUnmaskedPii) =>
        canViewUnmaskedPii ? phone : PiiMasking.MaskPhone(phone);

    private static decimal SharePercent(int numerator, int denominator) =>
        denominator <= 0 ? 0m : Math.Round(numerator / (decimal)denominator * 100m, 2);

    private async Task LogExportAsync(Guid tenantId, Guid userId, string action, string meta, CancellationToken ct)
    {
        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            EntityType = "marketing_analytics_export",
            Meta = meta,
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);
    }

    // ── Excel builders (IExcelExportService reused as-is — TASK-414 formula-injection guard
    // lives centrally in ExcelExportService.SetCellValue, nothing to duplicate here) ──────────

    private PriceSegmentExportResult BuildPriceAudienceExcel(
        PriceAudienceKey audience, IReadOnlyList<PriceAudienceTableRowRaw> rows, PriceSegmentBoundariesRow boundaries, bool unmaskPii)
    {
        string[] headers =
        [
            "Ім'я", "Телефон", "Сегмент (було)", "Сегмент (стало)",
            "Товарів/чек (було)", "Товарів/чек (стало)", "Тип. чек, ₴", "LTV, ₴",
        ];

        var excelRows = rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Name,
            unmaskPii ? r.Phone : PiiMasking.MaskPhone(r.Phone),
            PriceSegmentCatalog.RangeLabelUa(PriceSegmentCatalog.FromOrdinal(r.PreviousSegmentOrdinal), boundaries),
            PriceSegmentCatalog.RangeLabelUa(PriceSegmentCatalog.FromOrdinal(r.CurrentSegmentOrdinal), boundaries),
            r.ItemsPerReceiptPrevious,
            r.ItemsPerReceiptCurrent,
            r.TypicalCheckCurrent,
            r.Ltv,
        }).ToList();

        var excelResult = _excel.Export(new ExcelExportRequest($"Аудиторія {PriceSegmentCatalog.LabelUa(audience)}", headers, excelRows, ExportMaxRows));
        var fileName = $"price_audience_{audience}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return new PriceSegmentExportResult(excelResult.FileBytes, fileName, excelResult.RowCount, excelResult.Truncated);
    }

    private PriceSegmentExportResult BuildAllTimeExcel(
        IReadOnlyList<AllTimeCustomerRowRaw> rows, PriceSegmentBoundariesRow boundaries, bool unmaskPii)
    {
        string[] headers = ["Ім'я", "Телефон", "Ціновий сегмент", "Товарів/чек", "Тип. чек, ₴", "Покупок", "LTV, ₴"];

        var excelRows = rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Name,
            unmaskPii ? r.Phone : PiiMasking.MaskPhone(r.Phone),
            PriceSegmentCatalog.RangeLabelUa(PriceSegmentCatalog.FromOrdinal(r.SegmentOrdinal), boundaries),
            r.ItemsPerReceipt,
            r.TypicalCheck,
            r.PurchaseCount,
            r.Ltv,
        }).ToList();

        var excelResult = _excel.Export(new ExcelExportRequest("Цінові сегменти (весь час)", headers, excelRows, ExportMaxRows));
        var fileName = $"price_segments_all_time_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return new PriceSegmentExportResult(excelResult.FileBytes, fileName, excelResult.RowCount, excelResult.Truncated);
    }

    private PriceSegmentExportResult BuildFrequencyExcel(
        FrequencyAudienceKey audience, IReadOnlyList<FrequencyAudienceTableRowRaw> rows, bool unmaskPii)
    {
        string[] headers =
        [
            "Ім'я", "Телефон", "Покупок було", "Покупок стало", "Зміна, шт", "Зміна, %",
            "Тип. чек, ₴", "Витрати, ₴", "LTV, ₴",
        ];

        // "—" mirrors the on-screen table convention for a customer with no current-period
        // receipts (analysis doc §21) — the export must not silently show a misleading blank
        // where the on-screen table shows an explicit placeholder.
        var excelRows = rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Name,
            unmaskPii ? r.Phone : PiiMasking.MaskPhone(r.Phone),
            r.PreviousFrequency,
            r.CurrentFrequency,
            r.FrequencyDeltaAbsolute,
            r.FrequencyDeltaPercent.HasValue ? Math.Round(r.FrequencyDeltaPercent.Value, 2) : (object)"\u2014",
            r.TypicalCheckCurrent.HasValue ? r.TypicalCheckCurrent.Value : (object)"\u2014",
            r.SpendCurrentPeriod,
            r.Ltv,
        }).ToList();

        var excelResult = _excel.Export(new ExcelExportRequest($"Частота {PriceSegmentCatalog.LabelUa(audience)}", headers, excelRows, ExportMaxRows));
        var fileName = $"frequency_{audience}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return new PriceSegmentExportResult(excelResult.FileBytes, fileName, excelResult.RowCount, excelResult.Truncated);
    }
}
