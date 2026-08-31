using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
// PiiMasking (internal static, same assembly) lives one namespace up — shared by RFM/PriceSegments/
// AudienceBuilder alike (TASK-420's move made it a common MarketingAnalytics-wide helper). Explicit
// using kept for clarity even though C# would also resolve it via enclosing-namespace fallback.
using ShelfGuard.Application.Features.MarketingAnalytics;

namespace ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;

/// <summary>
/// Фаза 3 AudienceBuilder orchestration (TASK-429, design doc §1-§9). See
/// <see cref="IAudienceBuilderService"/> for the overall shape; this class additionally owns
/// splitting each request's <see cref="AudienceTermRequest"/> list into the parallel index/value
/// arrays <see cref="IAudienceBuilderRepository"/>'s UNNEST-based SQL consumes.
/// </summary>
public sealed class AudienceBuilderService : IAudienceBuilderService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 200;
    private const int ExportMaxRows = 50_000;
    private const int DefaultCategoryLimit = 20;
    private const int MaxCategoryLimit = 100;

    private readonly IAudienceBuilderRepository _repo;
    private readonly IExcelExportService _excel;
    private readonly IActivityLogRepository _activityLogs;

    public AudienceBuilderService(IAudienceBuilderRepository repo, IExcelExportService excel, IActivityLogRepository activityLogs)
    {
        _repo = repo;
        _excel = excel;
        _activityLogs = activityLogs;
    }

    public async Task<IReadOnlyList<AudienceCategoryOptionDto>> SearchCategoriesAsync(
        Guid tenantId, string? search, int limit, CancellationToken ct = default)
    {
        var safeLimit = limit is < 1 or > MaxCategoryLimit ? DefaultCategoryLimit : limit;
        var rows = await _repo.SearchCategoriesAsync(tenantId, search?.Trim(), safeLimit, ct);
        return rows.Select(r => new AudienceCategoryOptionDto(r.CategoryId, r.Name, r.ItemCount)).ToList();
    }

    // ── Own-product audience ─────────────────────────────────────────────────────────────────

    public async Task<AudienceOverviewDto> GetOverviewAsync(Guid tenantId, AudienceBuildRequest request, CancellationToken ct = default)
    {
        var hash = ComputeHash(tenantId, request);
        var query = ResolveAudience(tenantId, request.From, request.To, request.StoreIds, request.Terms, request.Mode,
            request.MinQuantity, request.MinAmount, request.ExcludedItemIds);
        if (query is null)
            return new AudienceOverviewDto(0, 0, 0m, 0m, hash, DateTimeOffset.UtcNow);

        var row = await _repo.GetOverviewAsync(query, ct);
        return new AudienceOverviewDto(row.ParticipantsCount, row.ItemsInSelectionCount, row.UnitsPurchased, row.TotalSpend, hash, DateTimeOffset.UtcNow);
    }

    public async Task<AudienceBuyerTableDto> GetBuyersAsync(Guid tenantId, AudienceBuildRequest request, CancellationToken ct = default)
    {
        var hash = ComputeHash(tenantId, request);
        var (page, pageSize) = NormalizePaging(request.Page, request.PageSize);
        var sortBy = AudienceBuilderSortKeys.NormalizeBuyers(request.SortBy);
        var query = ResolveAudience(tenantId, request.From, request.To, request.StoreIds, request.Terms, request.Mode,
            request.MinQuantity, request.MinAmount, request.ExcludedItemIds);

        if (query is null)
            return new AudienceBuyerTableDto(0, 0, [], page, pageSize, 0, sortBy, request.SortDescending, hash, DateTimeOffset.UtcNow);

        var rows = await _repo.GetBuyersAsync(query, page, pageSize, request.SortBy, request.SortDescending, ct);
        var first = rows.FirstOrDefault();

        var dtoRows = rows.Select(r => new AudienceBuyerRowDto(
            r.CustomerId, r.Name, MaskPhoneUnlessAuthorized(r.Phone, request.CanViewUnmaskedPii),
            r.QuantityPurchased, r.ReceiptCount, r.TotalAmount)).ToList();

        return new AudienceBuyerTableDto(
            first?.TotalCount ?? 0, first?.WithPhoneCount ?? 0, dtoRows,
            page, pageSize, TotalPages(first?.TotalCount ?? 0, pageSize),
            sortBy, request.SortDescending, hash, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<Guid>> ResolveCustomerIdsAsync(
        Guid tenantId, AudienceBuildRequest request, CancellationToken ct = default)
    {
        var query = ResolveAudience(tenantId, request.From, request.To, request.StoreIds, request.Terms, request.Mode,
            request.MinQuantity, request.MinAmount, request.ExcludedItemIds);
        if (query is null) return [];

        var result = new List<Guid>();
        for (var page = 1; ; page++)
        {
            var rows = await _repo.GetBuyersAsync(query, page, MaxPageSize, "name", false, ct);
            result.AddRange(rows.Select(x => x.CustomerId));
            var total = rows.FirstOrDefault()?.TotalCount ?? 0;
            if (rows.Count == 0 || result.Count >= total) break;
        }
        return result.Distinct().ToArray();
    }

    public async Task<MatchedItemsTableDto> GetMatchedItemsAsync(Guid tenantId, AudienceBuildRequest request, CancellationToken ct = default)
    {
        var hash = ComputeHash(tenantId, request);
        var (page, pageSize) = NormalizePaging(request.Page, request.PageSize);
        var sortBy = AudienceBuilderSortKeys.NormalizeMatchedItems(request.SortBy);
        var query = ResolveAudience(tenantId, request.From, request.To, request.StoreIds, request.Terms, request.Mode,
            request.MinQuantity, request.MinAmount, request.ExcludedItemIds);

        if (query is null)
            return new MatchedItemsTableDto(0, [], page, pageSize, 0, sortBy, request.SortDescending, hash, DateTimeOffset.UtcNow);

        var rows = await _repo.GetMatchedItemsAsync(query, page, pageSize, request.SortBy, request.SortDescending, ct);
        var first = rows.FirstOrDefault();

        var dtoRows = rows.Select(r => new MatchedItemRowDto(
            r.ItemId, r.Name, r.BarcodesJoined, r.IsExcluded, r.QuantitySold, r.ReceiptCount, r.BuyerCount)).ToList();

        return new MatchedItemsTableDto(
            first?.TotalCount ?? 0, dtoRows, page, pageSize, TotalPages(first?.TotalCount ?? 0, pageSize),
            sortBy, request.SortDescending, hash, DateTimeOffset.UtcNow);
    }

    public async Task<AudienceBuilderExportResult> ExportBuyersAsync(
        Guid tenantId, Guid userId, ExportAudienceBuyersRequest request, CancellationToken ct = default)
    {
        var query = ResolveAudience(tenantId, request.From, request.To, request.StoreIds, request.Terms, request.Mode,
            request.MinQuantity, request.MinAmount, request.ExcludedItemIds);

        var rows = query is null
            ? []
            : await _repo.GetBuyerReceiptsAsync(query, ExportMaxRows, ct);

        var result = BuildBuyersExcel(rows, request.UnmaskPii);

        await LogExportAsync(
            tenantId, userId, "marketing_analytics.audience_builder.export_buyers",
            $"terms={request.Terms.Count}; mode={request.Mode}; from={request.From:yyyy-MM-dd}; to={request.To:yyyy-MM-dd}; " +
            $"stores={StoresLabel(request.StoreIds)}; rows={result.RowCount}; truncated={result.Truncated}; " +
            $"piiMasked={!request.UnmaskPii}", ct);

        return result;
    }

    // ── Competitor audience ──────────────────────────────────────────────────────────────────

    public async Task<CompetitorOverviewDto> GetCompetitorOverviewAsync(Guid tenantId, CompetitorAudienceRequest request, CancellationToken ct = default)
    {
        var hash = ComputeCompetitorHash(tenantId, request);
        var query = ResolveCompetitor(tenantId, request.From, request.To, request.StoreIds,
            request.OwnTerms, request.OwnExcludedItemIds, request.CompetitorTerms, request.Horizon);

        if (query is null)
            return new CompetitorOverviewDto(0, 0, 0m, 0m, hash, DateTimeOffset.UtcNow);

        var row = await _repo.GetCompetitorOverviewAsync(query, ct);
        return new CompetitorOverviewDto(row.NewAudienceCount, row.CompetitorItemsCount, row.UnitsPurchased, row.TotalSpend, hash, DateTimeOffset.UtcNow);
    }

    public async Task<CompetitorBuyerTableDto> GetCompetitorBuyersAsync(Guid tenantId, CompetitorAudienceRequest request, CancellationToken ct = default)
    {
        var hash = ComputeCompetitorHash(tenantId, request);
        var (page, pageSize) = NormalizePaging(request.Page, request.PageSize);
        var sortBy = AudienceBuilderSortKeys.NormalizeCompetitorBuyers(request.SortBy);
        var query = ResolveCompetitor(tenantId, request.From, request.To, request.StoreIds,
            request.OwnTerms, request.OwnExcludedItemIds, request.CompetitorTerms, request.Horizon);

        if (query is null)
            return new CompetitorBuyerTableDto(0, 0, [], page, pageSize, 0, sortBy, request.SortDescending, hash, DateTimeOffset.UtcNow);

        var rows = await _repo.GetCompetitorBuyersAsync(query, page, pageSize, request.SortBy, request.SortDescending, ct);
        var first = rows.FirstOrDefault();

        var dtoRows = rows.Select(r => new CompetitorBuyerRowDto(
            r.CustomerId, r.Name, MaskPhoneUnlessAuthorized(r.Phone, request.CanViewUnmaskedPii),
            r.QuantityPurchased, r.ReceiptCount, r.TotalAmount)).ToList();

        return new CompetitorBuyerTableDto(
            first?.TotalCount ?? 0, first?.WithPhoneCount ?? 0, dtoRows,
            page, pageSize, TotalPages(first?.TotalCount ?? 0, pageSize),
            sortBy, request.SortDescending, hash, DateTimeOffset.UtcNow);
    }

    public async Task<AudienceBuilderExportResult> ExportCompetitorBuyersAsync(
        Guid tenantId, Guid userId, ExportCompetitorBuyersRequest request, CancellationToken ct = default)
    {
        var query = ResolveCompetitor(tenantId, request.From, request.To, request.StoreIds,
            request.OwnTerms, request.OwnExcludedItemIds, request.CompetitorTerms, request.Horizon);

        // Reuses the paginated table method at page=1/ExportMaxRows — same shape as
        // PriceSegmentsService.ExportAllTimeAsync reusing GetAllTimeCustomerTableAsync.
        var rows = query is null
            ? []
            : await _repo.GetCompetitorBuyersAsync(query, 1, ExportMaxRows, null, true, ct);

        var result = BuildCompetitorExcel(rows, request.UnmaskPii);

        await LogExportAsync(
            tenantId, userId, "marketing_analytics.audience_builder.export_competitor_buyers",
            $"competitorTerms={request.CompetitorTerms.Count}; horizon={request.Horizon}; from={request.From:yyyy-MM-dd}; to={request.To:yyyy-MM-dd}; " +
            $"stores={StoresLabel(request.StoreIds)}; rows={result.RowCount}; truncated={result.Truncated}; " +
            $"piiMasked={!request.UnmaskPii}", ct);

        return result;
    }

    // ── query resolution (Terms -> parallel arrays for the UNNEST-based SQL) ─────────────────

    private static ResolvedAudienceQuery? ResolveAudience(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyList<Guid>? storeIds,
        IReadOnlyList<AudienceTermRequest> terms, AudienceCombineMode mode,
        decimal? minQuantity, decimal? minAmount, IReadOnlyList<Guid>? excludedItemIds)
    {
        var (textIdx, textVals, catIdx, catIds, termCount) = SplitTerms(terms);
        if (termCount == 0) return null;

        var (fromDt, toDt) = ToUtcRange(from, to);
        return new ResolvedAudienceQuery(
            tenantId, fromDt, toDt, Normalize(storeIds),
            textIdx, textVals, catIdx, catIds, termCount,
            mode == AudienceCombineMode.All, minQuantity, minAmount, Normalize(excludedItemIds));
    }

    private static ResolvedCompetitorQuery? ResolveCompetitor(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyList<Guid>? storeIds,
        IReadOnlyList<AudienceTermRequest> ownTerms, IReadOnlyList<Guid>? ownExcludedItemIds,
        IReadOnlyList<AudienceTermRequest> competitorTerms, CompetitorHorizon horizon)
    {
        var (compTextIdx, compTextVals, compCatIdx, compCatIds, compCount) = SplitTerms(competitorTerms);
        var (ownTextIdx, ownTextVals, ownCatIdx, ownCatIds, ownCount) = SplitTerms(ownTerms);
        if (compCount == 0 || ownCount == 0) return null;

        var (fromDt, toDt) = ToUtcRange(from, to);
        return new ResolvedCompetitorQuery(
            tenantId, fromDt, toDt, Normalize(storeIds),
            compTextIdx, compTextVals, compCatIdx, compCatIds,
            ownTextIdx, ownTextVals, ownCatIdx, ownCatIds,
            Normalize(ownExcludedItemIds), horizon == CompetitorHorizon.AllTime);
    }

    /// <summary>
    /// Splits a term list into the parallel (index, value) arrays the repository's UNNEST-based
    /// SQL needs — each term's own 0-based position in <paramref name="terms"/> is preserved as
    /// term_index even when text and category terms are interleaved, so AND-mode's
    /// <c>covered_terms = termCount</c> comparison stays correct regardless of term order or kind
    /// mix. A term missing its own Kind's required value (e.g. Category with no CategoryId, or
    /// Text with blank text) is silently skipped — never throws, never counts toward
    /// <c>TermCount</c>.
    /// </summary>
    private static (int[] TextIdx, string[] TextVals, int[] CatIdx, Guid[] CatIds, int TermCount) SplitTerms(
        IReadOnlyList<AudienceTermRequest>? terms)
    {
        if (terms is null || terms.Count == 0) return ([], [], [], [], 0);

        var textIdx = new List<int>();
        var textVals = new List<string>();
        var catIdx = new List<int>();
        var catIds = new List<Guid>();

        for (var i = 0; i < terms.Count; i++)
        {
            var term = terms[i];
            switch (term.Kind)
            {
                case AudienceTermKind.Category when term.CategoryId is { } categoryId:
                    catIdx.Add(i);
                    catIds.Add(categoryId);
                    break;
                case AudienceTermKind.Text when !string.IsNullOrWhiteSpace(term.Text):
                    textIdx.Add(i);
                    textVals.Add(term.Text.Trim());
                    break;
            }
        }

        return (textIdx.ToArray(), textVals.ToArray(), catIdx.ToArray(), catIds.ToArray(), textIdx.Count + catIdx.Count);
    }

    private static string ComputeHash(Guid tenantId, AudienceBuildRequest r) => AudienceBuilderFilterHash.Compute(
        tenantId, r.StoreIds, r.From, r.To, TermsLabel(r.Terms), r.Mode, r.MinQuantity, r.MinAmount, (IEnumerable<Guid>?)r.ExcludedItemIds);

    private static string ComputeCompetitorHash(Guid tenantId, CompetitorAudienceRequest r) => AudienceBuilderFilterHash.Compute(
        tenantId, r.StoreIds, r.From, r.To, TermsLabel(r.OwnTerms), (IEnumerable<Guid>?)r.OwnExcludedItemIds, TermsLabel(r.CompetitorTerms), r.Horizon);

    private static string TermsLabel(IReadOnlyList<AudienceTermRequest> terms) =>
        string.Join(',', terms.Select(t => $"{t.Kind}:{t.Text ?? t.CategoryId?.ToString() ?? string.Empty}"));

    // ── shared internals ─────────────────────────────────────────────────────────────────────

    private static (int Page, int PageSize) NormalizePaging(int page, int pageSize) =>
        (Math.Max(1, page), pageSize is < 1 or > MaxPageSize ? DefaultPageSize : pageSize);

    private static int TotalPages(int totalCount, int pageSize) =>
        pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

    private static Guid[] Normalize(IReadOnlyList<Guid>? ids) => ids is null or { Count: 0 } ? [] : ids.Distinct().ToArray();

    // Npgsql rejects DateTime with Kind=Unspecified as a parameter for timestamptz columns —
    // mirrors AnalyticsRepository/MarketingAnalyticsRepository/PriceSegmentsRepository's own
    // ToUtcRange exactly.
    private static (DateTime FromDt, DateTime ToDt) ToUtcRange(DateOnly from, DateOnly to) => (
        from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));

    private static string StoresLabel(IReadOnlyList<Guid>? storeIds) =>
        storeIds is { Count: > 0 } ? string.Join(",", storeIds) : "all";

    /// <summary>
    /// Design doc §9 — PII masking applied from day 0, not patched in later (TASK-425 was a later
    /// fix for Фаза 2; this feature ships with the same rule from the start). Masking happens in
    /// the SERVICE when mapping row -> DTO, gated by the SAME server-resolved capability flag
    /// (<c>MarketingAnalyticsAuthorization.CanExportPii</c>, resolved by the controller) the export
    /// path also checks — reuses <see cref="PiiMasking"/> verbatim, no second copy of the masking
    /// rule.
    /// </summary>
    private static string? MaskPhoneUnlessAuthorized(string? phone, bool canViewUnmaskedPii) =>
        canViewUnmaskedPii ? phone : PiiMasking.MaskPhone(phone);

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

    // ── Excel builders (IExcelExportService reused as-is — the formula-injection guard lives
    // centrally in ExcelExportService.SetCellValue, nothing to duplicate here) ─────────────────

    private AudienceBuilderExportResult BuildBuyersExcel(IReadOnlyList<AudienceReceiptExportRowRaw> rows, bool unmaskPii)
    {
        string[] headers = ["Ім'я", "Телефон", "№ чека", "Дата", "Заклад", "Куплено, шт", "Сума, ₴"];

        var excelRows = rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Name,
            unmaskPii ? r.Phone : PiiMasking.MaskPhone(r.Phone),
            r.ReceiptNumber,
            r.CreatedAt,
            r.StoreName ?? "—",
            r.Quantity,
            r.Amount,
        }).ToList();

        var excelResult = _excel.Export(new ExcelExportRequest("Покупці товару", headers, excelRows, ExportMaxRows));
        var fileName = $"audience_buyers_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return new AudienceBuilderExportResult(excelResult.FileBytes, fileName, excelResult.RowCount, excelResult.Truncated);
    }

    private AudienceBuilderExportResult BuildCompetitorExcel(IReadOnlyList<CompetitorBuyerRowRaw> rows, bool unmaskPii)
    {
        string[] headers = ["Ім'я", "Телефон", "Куплено, шт", "Чеків", "Сума, ₴"];

        var excelRows = rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Name,
            unmaskPii ? r.Phone : PiiMasking.MaskPhone(r.Phone),
            r.QuantityPurchased,
            r.ReceiptCount,
            r.TotalAmount,
        }).ToList();

        var excelResult = _excel.Export(new ExcelExportRequest("Конкурентна аудиторія", headers, excelRows, ExportMaxRows));
        var fileName = $"audience_competitor_buyers_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return new AudienceBuilderExportResult(excelResult.FileBytes, fileName, excelResult.RowCount, excelResult.Truncated);
    }
}
