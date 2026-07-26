using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.Dtos;

// ── Overview (GET /api/marketing-analytics/overview) ────────────────────────────────────────

public sealed record RfmSegmentSummaryDto(
    RfmSegmentKey Key,
    string LabelUa,
    string ShortDescriptionUa,
    int CustomerCount,
    decimal SharePercentOfPeriodCustomers,
    decimal Revenue,
    decimal SharePercentOfPeriodRevenue);

public sealed record RfmNoPurchaseSummaryDto(
    int CustomerCount,
    decimal SharePercentOfRegisteredBase);

public sealed record RfmOverviewDto(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    int PeriodCustomerCount,
    decimal PeriodRevenue,
    int RegisteredCustomerCount,
    int EverPurchasedCustomerCount,
    decimal EverPurchasedSharePercent,
    IReadOnlyList<RfmSegmentSummaryDto> Segments,
    RfmNoPurchaseSummaryDto NoPurchase,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── Segment detail (GET /api/marketing-analytics/segments/{key}) ────────────────────────────

public sealed record RfmTopProductDto(
    int Rank,
    string ProductName,
    decimal CoveragePercent,
    int UniqueCustomerCount,
    int ReceiptCount,
    string? Barcode);

/// <summary>Share of the segment's receipts falling on one ISO day-of-week (1=Monday..7=Sunday)
/// or one hour-of-day (0-23, Europe/Kyiv). Kept as machine-readable ints, not pre-localized
/// Ukrainian labels — the frontend already owns weekday/hour i18n elsewhere.</summary>
public sealed record RfmDayOfWeekShareDto(int DayOfWeekIso, decimal SharePercent);

public sealed record RfmHourShareDto(int Hour, decimal SharePercent);

public sealed record RfmBehaviorDto(
    int? PeakDayOfWeekIso,
    int? PeakHour,
    decimal AverageTicket,
    int ReceiptCount,
    decimal ReceiptsPerCustomer,
    DateOnly? LastVisit,
    decimal AverageRecencyDays,
    decimal AverageLtv,
    decimal TotalLtv,
    IReadOnlyList<RfmDayOfWeekShareDto> ByDayOfWeek,
    IReadOnlyList<RfmHourShareDto> ByHour,
    IReadOnlyList<RfmHourShareDto> TopPeakHours);

public sealed record RfmRecommendationDto(
    string TriggerUa,
    string ActionUa,
    string OfferUa,
    string CautionUa,
    IReadOnlyList<string> ProductsForPromo);

/// <summary>Live KPIs substituted into <see cref="RecommendationTemplates"/>'s per-segment copy
/// — the same numbers already computed for <see cref="RfmSegmentSummaryDto"/>/
/// <see cref="RfmBehaviorDto"/>, just narrowed to the fields the templates actually quote.</summary>
public sealed record RfmRecommendationInputDto(
    int CustomerCount,
    decimal SharePercentOfPeriodCustomers,
    decimal SharePercentOfPeriodRevenue,
    decimal AverageRecencyDays,
    decimal AverageLtv,
    int? PeakDayOfWeekIso,
    int? PeakHour,
    IReadOnlyList<string> TopProductNames,
    string? TopProductName,
    decimal? TopProductCoveragePercent);

public sealed record RfmSegmentDetailDto(
    RfmSegmentKey Key,
    string LabelUa,
    string ShortDescriptionUa,
    int CustomerCount,
    IReadOnlyList<RfmTopProductDto> TopProducts,
    RfmBehaviorDto Behavior,
    RfmRecommendationDto Recommendation,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── Cross-sell: affinity (lift) / basket ("разом у чеку") ────────────────────────────────────

public sealed record RfmAffinityItemDto(
    string ProductName,
    decimal Lift,
    int BothBuyersCount,
    decimal ShareAmongAnchorBuyersPercent,
    decimal ShareAmongSegmentPercent,
    string? Barcode);

public sealed record RfmAffinityResultDto(
    RfmSegmentKey SegmentKey,
    string AnchorProductName,
    IReadOnlyList<RfmAffinityItemDto> Items,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

public sealed record RfmBasketItemDto(
    string ProductName,
    decimal TogetherSharePercent,
    int BothReceiptsCount,
    string? Barcode);

public sealed record RfmBasketResultDto(
    RfmSegmentKey SegmentKey,
    string AnchorProductName,
    IReadOnlyList<RfmBasketItemDto> Items,
    string FiltersHash,
    DateTimeOffset CalculatedAt);

// ── AI "explain" (POST .../segments/{key}/explain) ───────────────────────────────────────────

public sealed record ExplainRfmSegmentResultDto(
    string ExplanationUa,
    string Model,
    int TokensUsed);

// ── Exports ──────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Shared filter+PII shape for all three export endpoints. <c>UnmaskPii</c> only takes effect
/// when the caller passes the <c>MarketingAnalyticsAuthorization.CanExportPii</c> check
/// (backend/ShelfGuard.Infrastructure/Authorization — store_manager+ or the
/// marketing_analytics.export_pii capability) — otherwise the service silently falls back to
/// masked, it never 403s a caller who simply left the box unchecked.
/// </summary>
public sealed record ExportRfmFilterRequest(
    RfmSegmentKey Key,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid>? StoreIds,
    bool UnmaskPii);

public sealed record ExportRfmProductRequest(
    RfmSegmentKey Key,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid>? StoreIds,
    bool UnmaskPii,
    string ProductName);

/// <summary>"Покупці обох" (RFM_ANALYSIS.md §15.3) — customer-level intersection (bought both
/// ProductName and PairedProductName within the period), scoped to the segment. Serves the
/// export button from BOTH the affinity and basket tabs equally — the export itself doesn't
/// distinguish which formula the user was looking at when they clicked it.</summary>
public sealed record ExportRfmProductPairRequest(
    RfmSegmentKey Key,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Guid>? StoreIds,
    bool UnmaskPii,
    string ProductName,
    string PairedProductName);

/// <summary>Result of a completed export — bytes are the finished .xlsx file, streamed straight
/// into the HTTP response body by the controller (no server-side temp file).</summary>
public sealed record RfmExportResult(byte[] FileBytes, string FileName, int RowCount, bool Truncated);
