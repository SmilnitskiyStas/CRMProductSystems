using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments.Dtos;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;

/// <summary>
/// Thin orchestration over <see cref="IPriceSegmentsRepository"/> (raw SQL), the 3 pure
/// classifiers (<see cref="PriceSegmentClassifier"/>/<see cref="PriceAudienceClassifier"/>/
/// <see cref="FrequencyAudienceClassifier"/>), <see cref="PriceSegmentRecommendationTemplates"/>,
/// <see cref="Interfaces.IPriceSegmentAdvisor"/> (optional Claude "explain more"), and
/// <see cref="Common.IExcelExportService"/> — same "thin service → repository" shape as
/// <see cref="IMarketingAnalyticsService"/>, this task's named architectural model (design doc
/// §0). One service covers all three Фаза 2 modes (comparison / all-time / frequency) plus
/// per-tenant settings, per design doc item 9 ("Один контролер/сервіс/репозиторій на всю Фазу 2").
///
/// Boundaries and network figures are recomputed from scratch on every call — nothing here is
/// cached across requests (same rule Фаза 1's R/F/M quintiles follow).
/// </summary>
public interface IPriceSegmentsService
{
    // ── Comparison mode ──────────────────────────────────────────────────────────────────────

    Task<PriceSegmentsOverviewDto> GetOverviewAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Never throws for an empty audience — a zero-customer audience still returns a
    /// valid DTO with an empty row list and zeroed KPIs (same "empty segment must not break the
    /// page" rule Фаза 1's QA checklist set).</summary>
    Task<PriceAudienceTableDto> GetAudienceTableAsync(
        Guid tenantId, PriceAudienceTableRequest request, CancellationToken ct = default);

    /// <summary>Claude "Пояснити детальніше" for one comparison-mode audience — user-triggered only.</summary>
    Task<ExplainPriceSegmentResultDto> ExplainAudienceAsync(
        Guid tenantId, PriceAudienceKey audience, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to,
        CancellationToken ct = default);

    /// <summary><paramref name="userId"/> is written to the ActivityLog entry this export
    /// produces (Action + filter snapshot + row count + PII-masked flag), same audit contract
    /// Фаза 1 exports already follow.</summary>
    Task<PriceSegmentExportResult> ExportAudienceAsync(
        Guid tenantId, Guid userId, ExportPriceAudienceRequest request, CancellationToken ct = default);

    // ── All-time mode ────────────────────────────────────────────────────────────────────────

    Task<PriceSegmentsAllTimeOverviewDto> GetAllTimeOverviewAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, PriceSegmentKey? selectedSegment, CancellationToken ct = default);

    Task<AllTimeCustomerTableDto> GetAllTimeCustomerTableAsync(
        Guid tenantId, AllTimeCustomerTableRequest request, CancellationToken ct = default);

    Task<ExplainPriceSegmentResultDto> ExplainAllTimeSegmentAsync(
        Guid tenantId, PriceSegmentKey segment, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    Task<PriceSegmentExportResult> ExportAllTimeAsync(
        Guid tenantId, Guid userId, ExportAllTimeRequest request, CancellationToken ct = default);

    // ── Frequency & reactivation mode ────────────────────────────────────────────────────────

    /// <summary><paramref name="declineThresholdPercent"/> null means "use the tenant's
    /// persisted <see cref="Domain.Entities.PriceSegmentSettings.DefaultFrequencyDeclineThresholdPercent"/>"
    /// (design doc item 7) — resolved once per call, never cached across requests.</summary>
    Task<FrequencyOverviewDto> GetFrequencyOverviewAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to,
        decimal? declineThresholdPercent, CancellationToken ct = default);

    Task<FrequencyAudienceTableDto> GetFrequencyAudienceTableAsync(
        Guid tenantId, FrequencyAudienceTableRequest request, CancellationToken ct = default);

    Task<ExplainPriceSegmentResultDto> ExplainFrequencyAudienceAsync(
        Guid tenantId, FrequencyAudienceKey audience, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to,
        decimal? declineThresholdPercent, CancellationToken ct = default);

    Task<PriceSegmentExportResult> ExportFrequencyAudienceAsync(
        Guid tenantId, Guid userId, ExportFrequencyAudienceRequest request, CancellationToken ct = default);

    // ── Settings (enterprise_admin) ──────────────────────────────────────────────────────────

    /// <summary>Returns proposed defaults (30%, no minimum receipts) when the tenant has never saved a row.</summary>
    Task<PriceSegmentSettingsDto> GetSettingsAsync(Guid tenantId, CancellationToken ct = default);

    Task<(PriceSegmentSettingsDto? Settings, string? Error)> UpsertSettingsAsync(
        Guid tenantId, UpsertPriceSegmentSettingsRequest request, CancellationToken ct = default);
}
