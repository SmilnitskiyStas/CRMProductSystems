using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder.Dtos;

namespace ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;

/// <summary>
/// Thin orchestration over <see cref="IAudienceBuilderRepository"/> (raw SQL) +
/// <see cref="Common.IExcelExportService"/> + <see cref="Domain.Interfaces.IActivityLogRepository"/>
/// — same "thin service → repository" shape as <c>MarketingAnalyticsService</c> /
/// <c>PriceSegments.PriceSegmentsService</c> (design doc §0/§1). Splits each request's
/// <c>AudienceTermRequest</c> list into the parallel index/value arrays the repository's
/// UNNEST-based SQL needs (see <see cref="ResolvedAudienceQuery"/>/
/// <see cref="ResolvedCompetitorQuery"/>) and never queries the database at all for a request with
/// no valid terms — matches the competitor's own "formation button disabled until a term exists"
/// rule, enforced defensively server-side too.
/// </summary>
public interface IAudienceBuilderService
{
    Task<IReadOnlyList<AudienceCategoryOptionDto>> SearchCategoriesAsync(
        Guid tenantId, string? search, int limit, CancellationToken ct = default);

    Task<AudienceOverviewDto> GetOverviewAsync(Guid tenantId, AudienceBuildRequest request, CancellationToken ct = default);

    /// <summary>Never throws for an empty audience — a zero-match request still returns a valid
    /// DTO with an empty row list and zeroed KPIs (same "empty result must not break the page"
    /// rule Фаза 1/2's own QA checklists already set).</summary>
    Task<AudienceBuyerTableDto> GetBuyersAsync(Guid tenantId, AudienceBuildRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ResolveCustomerIdsAsync(
        Guid tenantId, AudienceBuildRequest request, CancellationToken ct = default);

    Task<MatchedItemsTableDto> GetMatchedItemsAsync(Guid tenantId, AudienceBuildRequest request, CancellationToken ct = default);

    /// <summary><paramref name="userId"/> is written to the ActivityLog entry this export
    /// produces (Action + filter snapshot + row count + PII-masked flag), same audit contract
    /// Фаза 1/2 exports already follow (competitor-analysis doc §25's журнал експорту requirement).</summary>
    Task<AudienceBuilderExportResult> ExportBuyersAsync(
        Guid tenantId, Guid userId, ExportAudienceBuyersRequest request, CancellationToken ct = default);

    Task<CompetitorOverviewDto> GetCompetitorOverviewAsync(Guid tenantId, CompetitorAudienceRequest request, CancellationToken ct = default);

    Task<CompetitorBuyerTableDto> GetCompetitorBuyersAsync(Guid tenantId, CompetitorAudienceRequest request, CancellationToken ct = default);

    Task<AudienceBuilderExportResult> ExportCompetitorBuyersAsync(
        Guid tenantId, Guid userId, ExportCompetitorBuyersRequest request, CancellationToken ct = default);
}
