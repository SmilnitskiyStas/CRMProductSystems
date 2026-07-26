using ShelfGuard.Application.Features.MarketingAnalytics.Dtos;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics;

/// <summary>
/// Thin orchestration over <see cref="IMarketingAnalyticsRepository"/> (raw SQL scoring/
/// aggregation), <see cref="RfmSegmentClassifier"/> (pure classification),
/// <see cref="RecommendationTemplates"/> (pure copy generation), <see cref="IMarketingAdvisor"/>
/// (optional Claude "explain more"), and <see cref="Application.Common.IExcelExportService"/>
/// (exports) — same "thin service → repository" shape as <c>IAnalyticsService</c>/
/// <c>AnalyticsService</c>, the direct architectural model named in this task's brief.
///
/// Every method recomputes R/F/M scoring + classification from scratch for the given filter —
/// nothing is cached across calls (plan's explicit requirement: quintiles/segments are always
/// recomputed for the exact filter combination in play).
/// </summary>
public interface IMarketingAnalyticsService
{
    Task<RfmOverviewDto> GetOverviewAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Never null/404 — a segment with zero customers still returns a valid DTO with
    /// zeroed-out numbers (QA checklist: an empty segment must not break the page).</summary>
    Task<RfmSegmentDetailDto> GetSegmentDetailAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key,
        CancellationToken ct = default);

    Task<RfmAffinityResultDto> GetAffinityAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key,
        string productName, int topN = 10, CancellationToken ct = default);

    Task<RfmBasketResultDto> GetBasketAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key,
        string productName, int topN = 10, CancellationToken ct = default);

    /// <summary>Claude "Пояснити детальніше" — always for exactly one segment, only on explicit
    /// user request (never called from GetSegmentDetailAsync itself).</summary>
    Task<ExplainRfmSegmentResultDto> ExplainSegmentAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, RfmSegmentKey key,
        CancellationToken ct = default);

    /// <summary><paramref name="userId"/> is the acting staff user — written to the ActivityLog
    /// entry this export produces (Action + Meta = filter snapshot + row count + PII-masked
    /// flag), per the brief's audit requirement.</summary>
    Task<RfmExportResult> ExportSegmentAsync(Guid tenantId, Guid userId, ExportRfmFilterRequest request, CancellationToken ct = default);

    Task<RfmExportResult> ExportProductBuyersAsync(Guid tenantId, Guid userId, ExportRfmProductRequest request, CancellationToken ct = default);

    Task<RfmExportResult> ExportProductPairBuyersAsync(Guid tenantId, Guid userId, ExportRfmProductPairRequest request, CancellationToken ct = default);
}
