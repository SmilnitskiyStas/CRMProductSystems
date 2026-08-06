using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign.Dtos;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;

/// <summary>
/// Thin orchestration over <see cref="IPostCampaignRepository"/> (EF Core LINQ),
/// <see cref="IMarketingAnalyticsRepository"/> (REUSED, not reimplemented — the RFM migration
/// matrix), <see cref="RfmSegmentClassifier"/> (REUSED), <see cref="PostCampaignBehaviorClassifier"/>
/// (new, pure), <see cref="PostCampaignRecommendationTemplates"/> (new, pure),
/// <see cref="Domain.Interfaces.IPostCampaignAdvisor"/> (optional Claude "explain more"), and
/// <see cref="Common.IExcelExportService"/>/<see cref="Common.IExcelImportService"/> (exports/
/// import) — same "thin service → repository" shape as every sibling MarketingAnalytics service.
///
/// Every report method (Summary/DailyTurnover/RfmActivity/Customers/Migration) recomputes fully
/// from <c>pos_transactions</c> on every call — nothing is cached beyond the frozen window dates
/// themselves already stored on the segment (Фаза 1's "never cache quintiles globally" principle,
/// carried over unchanged).
///
/// Every segment-id-scoped method returns a <c>(Result, Error)</c> tuple, NOT a bare DTO —
/// unlike Фаза 1-3's filter-only GETs (which can never fail: any date/enum combination is valid),
/// a <c>segmentId</c> can genuinely not exist/not belong to the caller's tenant/not be analyzed
/// yet, so this follows this codebase's existing CRUD-style error convention instead (e.g.
/// <c>CustomerService.CreateAsync</c>) — the controller maps an error ending in "not found." to
/// 404, anything else to 400, matching <c>DailySalesController.ImportCsv</c>'s existing convention.
/// </summary>
public interface IPostCampaignService
{
    Task<IReadOnlyList<PostCampaignSegmentListItemDto>> ListSegmentsAsync(Guid tenantId, CancellationToken ct = default);

    Task<(PostCampaignImportResultDto? Result, string? Error)> ImportAsync(
        Guid tenantId, Guid userId, PostCampaignImportRequest request, CancellationToken ct = default);

    Task<(PostCampaignAnalyzeResultDto? Result, string? Error)> AnalyzeAsync(
        Guid tenantId, Guid segmentId, DateOnly afterStart, DateOnly afterEnd, CancellationToken ct = default);

    Task<(PostCampaignSummaryDto? Result, string? Error)> GetSummaryAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    Task<(PostCampaignDailyTurnoverDto? Result, string? Error)> GetDailyTurnoverAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    Task<(PostCampaignRfmActivityDto? Result, string? Error)> GetRfmActivityAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>Full server pagination/sort over the matched-customer table — never a Top-200
    /// cap (brief's explicit fix over the competitor). Sorted/paginated IN MEMORY, not via SQL
    /// OFFSET/LIMIT like Фаза 2/3's tables — see <c>PostCampaignService</c>'s remarks for why
    /// (bounded at the 20,000-row import cap, and needs the pure C# RfmSegmentClassifier applied
    /// per row, which has no SQL equivalent).</summary>
    Task<(PostCampaignCustomerTableDto? Result, string? Error)> GetCustomersAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, int page, int pageSize,
        string? sortBy, bool sortDescending, bool canViewUnmaskedPii, CancellationToken ct = default);

    Task<(PostCampaignMigrationDto? Result, string? Error)> GetMigrationAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>Claude "Пояснити детальніше" for the Summary tab's KPIs — user-triggered only.</summary>
    Task<(ExplainPostCampaignResultDto? Result, string? Error)> ExplainAsync(
        Guid tenantId, Guid segmentId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary><paramref name="userId"/> is written to the ActivityLog entry this export
    /// produces, same audit contract Фаза 1/2/3 exports already follow.</summary>
    Task<(PostCampaignExportResult? Result, string? Error)> ExportCustomersAsync(
        Guid tenantId, Guid userId, Guid segmentId, IReadOnlyList<Guid>? storeIds, bool unmaskPii, CancellationToken ct = default);

    /// <summary>Downloadable error report of unmatched/invalid tokens (brief §8.2's required
    /// "downloadable error report") — no PII gate: these are uploader-supplied raw tokens, never
    /// resolved Customer PII, but still ActivityLog-audited for consistency. Returns
    /// <see cref="Dtos.PostCampaignUnknownTokensExportResult"/> rather than the generic
    /// <see cref="Dtos.PostCampaignExportResult"/> other exports use (TASK-478 fix) — see that
    /// type's own doc comment for why.</summary>
    Task<(PostCampaignUnknownTokensExportResult? Result, string? Error)> ExportUnknownTokensAsync(
        Guid tenantId, Guid userId, Guid segmentId, CancellationToken ct = default);
}
