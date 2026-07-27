using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments.Dtos;

// ── Recommendation — shared shape across comparison audiences / all-time tiers / frequency
// audiences (design doc §7 item 11: "новий тип, ідентичний формою RfmRecommendationDto, не той
// самий тип" — same Тригер/Дія/Оффер/Застереження shape, no ProductsForPromo: unlike RFM, Фаза 2
// has no per-audience top-products query to anchor a cross-sell list against). ────────────────

public sealed record PriceSegmentRecommendationDto(string TriggerUa, string ActionUa, string OfferUa, string CautionUa);

// ── Comparison mode: GET price-segments/overview ────────────────────────────────────────────

public sealed record PriceSegmentDistributionPointDto(
    PriceSegmentKey Segment, string RangeLabelUa, int CurrentCount, int PreviousCount);

public sealed record PriceAudienceSummaryDto(
    PriceAudienceKey Audience, string LabelUa, int CustomerCount, decimal SharePercentOfAnalyzed, decimal AverageLtv);

/// <summary>
/// <see cref="AnalyzedCount"/> (comparison cohort = bought in BOTH windows),
/// <see cref="CurrentPeriodBuyerCount"/> and <see cref="PreviousPeriodBuyerCount"/> (each
/// window's own active-buyer count, i.e. the chart's own two series totals) are THREE different
/// denominators and are never equal — analysis doc §6.2/§25.1 documents the competitor's own UI
/// blurring this distinction; this DTO keeps all three explicit and separately labeled so the
/// frontend never has to guess which one backs a given percentage.
/// </summary>
public sealed record PriceSegmentsOverviewDto(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateOnly PreviousPeriodFrom,
    DateOnly PreviousPeriodTo,
    int AnalyzedCount,
    int CurrentPeriodBuyerCount,
    int PreviousPeriodBuyerCount,
    int RaisedCount,
    int DeclinedCount,
    int StableCount,
    decimal PriceIndexPercent,
    IReadOnlyList<PriceSegmentDistributionPointDto> Distribution,
    IReadOnlyList<PriceAudienceSummaryDto> Audiences,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── Comparison mode: GET price-segments/audiences/{audience} ────────────────────────────────

/// <summary>
/// <see cref="CanViewUnmaskedPii"/> (TASK-425) is resolved server-side by the controller via
/// <c>MarketingAnalyticsAuthorization.CanExportPii(User)</c> — the SAME capability gate already
/// used for exports — and is never bound from client input: there is no "unmask" query parameter
/// on this GET endpoint at all, unlike the export requests where a client-supplied
/// <c>UnmaskPii</c> flag is re-derived/neutralized against the caller's role/capability. Defaults
/// to <c>false</c> (masked) so any call site that forgets to set it explicitly fails closed.
/// </summary>
public sealed record PriceAudienceTableRequest(
    PriceAudienceKey Audience, DateOnly From, DateOnly To, IReadOnlyList<Guid>? StoreIds,
    int Page, int PageSize, string? SortBy, bool SortDescending, bool CanViewUnmaskedPii = false);

public sealed record PriceAudienceRowDto(
    Guid CustomerId,
    string Name,
    string? Phone,
    PriceSegmentKey PreviousSegment,
    string PreviousSegmentLabelUa,
    PriceSegmentKey CurrentSegment,
    string CurrentSegmentLabelUa,
    decimal ItemsPerReceiptPrevious,
    decimal ItemsPerReceiptCurrent,
    decimal TypicalCheckCurrent,
    decimal Ltv);

public sealed record PriceAudienceTableDto(
    PriceAudienceKey Audience,
    string LabelUa,
    int TotalCount,
    int WithPhoneCount,
    IReadOnlyList<PriceAudienceRowDto> Rows,
    int Page,
    int PageSize,
    int TotalPages,
    string SortBy,
    bool SortDescending,
    PriceSegmentRecommendationDto Recommendation,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── Explain — shared result shape across all 3 explain flows (audience / all-time tier /
// frequency audience) ─────────────────────────────────────────────────────────────────────────

public sealed record ExplainPriceSegmentResultDto(string ExplanationUa, string Model, int TokensUsed);

// ── Exports ──────────────────────────────────────────────────────────────────────────────────

public sealed record ExportPriceAudienceRequest(
    PriceAudienceKey Audience, DateOnly From, DateOnly To, IReadOnlyList<Guid>? StoreIds, bool UnmaskPii);

public sealed record ExportAllTimeRequest(PriceSegmentKey? Segment, IReadOnlyList<Guid>? StoreIds, bool UnmaskPii);

/// <summary>Result of a completed export — bytes are the finished .xlsx file, streamed straight
/// into the HTTP response by the controller, same shape as Фаза 1's RfmExportResult.</summary>
public sealed record PriceSegmentExportResult(byte[] FileBytes, string FileName, int RowCount, bool Truncated);

// ── All-time mode: GET price-segments/all-time ──────────────────────────────────────────────

public sealed record MedianCheckMonthPointDto(int Year, int Month, decimal MedianCheck, decimal ItemsPerReceipt);

public sealed record AllTimeSegmentDistributionDto(
    PriceSegmentKey Segment, string RangeLabelUa, int CustomerCount, decimal AverageLtv);

/// <summary>Network-level auto-insights (analysis doc §12) — all nullable because every one of
/// them needs at least 2 comparable data points (a year-ago month, a 3-months-back point, a
/// historical peak) that a brand-new tenant with a short history simply won't have yet; null
/// means "not enough history yet", not zero.</summary>
public sealed record AllTimeInsightsDto(
    decimal? YoyPercent,
    decimal? Last3MonthsTrendPercent,
    decimal? BelowPeakPercent,
    decimal? HistoricalPeakMedianCheck,
    decimal? ItemsPerReceiptChangePercent);

/// <summary><see cref="Recommendation"/> is null until <see cref="SelectedSegment"/> is set —
/// mirrors the competitor's own "Оберіть ціновий сегмент нижче — рекомендація підлаштується під
/// нього" prompt-before-selection state (analysis doc §14), not a missing-data bug.</summary>
public sealed record PriceSegmentsAllTimeOverviewDto(
    int CustomersInBase,
    decimal NetworkAverageCheck,
    long PurchasesTotal,
    decimal TurnoverTotal,
    IReadOnlyList<MedianCheckMonthPointDto> MonthlyTrend,
    AllTimeInsightsDto Insights,
    IReadOnlyList<AllTimeSegmentDistributionDto> Distribution,
    PriceSegmentKey? SelectedSegment,
    PriceSegmentRecommendationDto? Recommendation,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── All-time mode: GET price-segments/all-time/customers ────────────────────────────────────

/// <summary>See <see cref="PriceAudienceTableRequest.CanViewUnmaskedPii"/> (TASK-425) — same
/// server-derived, non-client-bindable capability flag.</summary>
public sealed record AllTimeCustomerTableRequest(
    PriceSegmentKey? Segment, IReadOnlyList<Guid>? StoreIds,
    int Page, int PageSize, string? SortBy, bool SortDescending, bool CanViewUnmaskedPii = false);

public sealed record AllTimeCustomerRowDto(
    Guid CustomerId,
    string Name,
    string? Phone,
    PriceSegmentKey Segment,
    string SegmentLabelUa,
    decimal ItemsPerReceipt,
    decimal TypicalCheck,
    int PurchaseCount,
    decimal Ltv);

public sealed record AllTimeCustomerTableDto(
    PriceSegmentKey? Segment,
    int TotalCount,
    IReadOnlyList<AllTimeCustomerRowDto> Rows,
    int Page,
    int PageSize,
    int TotalPages,
    string SortBy,
    bool SortDescending,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── Recommendation inputs — Application-internal, feed PriceSegmentRecommendationTemplates,
// never serialized to the wire directly (same separation MarketingAnalyticsDtos.cs keeps between
// RfmRecommendationInputDto and RfmRecommendationDto). ──────────────────────────────────────────

public sealed record PriceAudienceRecommendationInputDto(
    PriceAudienceKey Audience, int CustomerCount, decimal SharePercentOfAnalyzed, decimal AverageLtv);

public sealed record AllTimeSegmentRecommendationInputDto(
    PriceSegmentKey Segment, string RangeLabelUa, int CustomerCount, decimal AverageLtv);

// ── Settings: GET/PUT api/settings/price-segments — exact same "lazy defaults" shape as
// LoyaltyProgramSettingsDto/UpsertLoyaltyProgramSettingsRequest (design doc §0 file list;
// LoyaltySettingsController is the named precedent). ────────────────────────────────────────

public sealed record PriceSegmentSettingsDto(
    decimal DefaultFrequencyDeclineThresholdPercent, int? MinReceiptsForBoundaries, DateTimeOffset? UpdatedAt);

public sealed record UpsertPriceSegmentSettingsRequest(
    decimal DefaultFrequencyDeclineThresholdPercent, int? MinReceiptsForBoundaries);
