using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;

// ── Raw rows returned by the repository's SQL — the SERVICE maps these into the wire-facing
// Dtos/PriceSegmentDtos.cs / Dtos/FrequencyDtos.cs records, exactly the same Application/
// Infrastructure separation MarketingAnalyticsRepository.cs already established in Фаза 1. These
// shapes must never be serialized directly (plain int/decimal ordinals, not the enum — the
// repository classifies inline in SQL for the paginated table queries, but the ENUM mapping back
// to PriceSegmentKey/PriceAudienceKey/FrequencyAudienceKey happens in the service, mirroring how
// RfmScoredCustomerRow carries plain ints that RfmSegmentClassifier alone turns into a key).

/// <summary>One customer's metrics for a single [from,to] window (+ store filter) — the one
/// shared primitive both the price-comparison AND frequency flows are built on (design doc
/// §1.1: "Один метод... викликається ДВІЧІ сервісом (current, previous)"; frequency reuses the
/// exact same call rather than a second query, since <see cref="ReceiptCount"/>/
/// <see cref="TotalSpend"/> are exactly what frequency classification needs too).</summary>
public sealed record PriceCustomerPeriodMetricsRow(
    Guid CustomerId, decimal MedianCheck, int ReceiptCount, decimal TotalSpend, decimal ItemsPerReceipt);

/// <summary>All-time LTV for a specific set of customers (design doc: LTV is always all-time,
/// same rule Фаза 1's RfmLtvRow already follows). An empty/null <c>customerIds</c> argument at
/// the call site means "every customer with purchase history" (used by the all-time
/// distribution's per-tier average).</summary>
public sealed record PriceLtvRow(Guid CustomerId, decimal Ltv);

/// <summary>Network-wide all-time KPIs (design doc §11) — <see cref="CustomersInBase"/> counts
/// customers who ever purchased (COUNT DISTINCT CustomerId over pos_transactions), NOT the raw
/// `customers` table row count (that would include never-purchased registrations, a different
/// concept the RFM "Без покупок" card already owns).</summary>
public sealed record AllTimeKpiRow(int CustomersInBase, decimal NetworkAverageCheck, long PurchasesTotal, decimal TurnoverTotal);

/// <summary>One calendar month's network-wide median check (design doc item 8: median of ALL
/// network receipts that month, NOT the average of per-customer medians) + items/receipt.</summary>
public sealed record MedianCheckMonthRow(int Year, int Month, decimal MedianCheck, decimal ItemsPerReceipt);

/// <summary>All-time distribution of the WHOLE customer base across the 7 tiers (design doc
/// §13/§14.1) — <see cref="SegmentOrdinal"/> is <see cref="Domain.Constants.PriceSegmentKey"/>'s
/// int backing (1-7, see <c>PriceSegmentCatalog.Ordinal</c>/<c>FromOrdinal</c>).</summary>
public sealed record AllTimeSegmentDistributionRow(int SegmentOrdinal, int CustomerCount, decimal AverageLtv);

/// <summary>One page of the comparison-mode price-audience table (design doc §2 "РЕАЛЬНУ
/// server-side пагінацію" — classification AND filtering happen inside the SQL itself, this row
/// is already filtered to one audience and already sorted/paginated). <see cref="TotalCount"/>/
/// <see cref="WithPhoneCount"/>/<see cref="AnalyzedCount"/>/<see cref="AverageLtvOverall"/> are
/// window-function aggregates over the FULL filtered result (repeated identically on every row
/// in the page) — never just this page's own rows — computed via <c>COUNT(*) OVER()</c> etc. in
/// the same round-trip, per design doc §2's "COUNT(*) OVER() в тому самому запиті".
/// <see cref="AnalyzedCount"/> is the comparison COHORT size (bought in both windows) BEFORE the
/// audience filter — i.e. the same "Проаналізовано" denominator the overview endpoint reports,
/// needed here too so this table's own Recommendation block can quote a correct SharePercent
/// without a second round-trip to the overview.</summary>
public sealed record PriceAudienceTableRowRaw(
    Guid CustomerId, string Name, string? Phone,
    int PreviousSegmentOrdinal, int CurrentSegmentOrdinal,
    decimal ItemsPerReceiptPrevious, decimal ItemsPerReceiptCurrent,
    decimal TypicalCheckCurrent, decimal Ltv,
    int TotalCount, int WithPhoneCount, int AnalyzedCount, decimal AverageLtvOverall);

/// <summary>One page of the all-time customer table (design doc §2 `all-time/customers`),
/// optionally filtered to one <see cref="SegmentOrdinal"/> (null request = all 7 tiers).</summary>
public sealed record AllTimeCustomerRowRaw(
    Guid CustomerId, string Name, string? Phone,
    int SegmentOrdinal, decimal ItemsPerReceipt, decimal TypicalCheck, int PurchaseCount, decimal Ltv,
    int TotalCount);

/// <summary>One page of the frequency-audience table (design doc §4's "Sleeping re-orientation"
/// already baked into the WHERE clause server-side — see <c>PriceSegmentsRepository</c>'s
/// remarks). <see cref="TypicalCheckCurrent"/>/<see cref="FrequencyDeltaPercent"/> nullable per
/// <see cref="Dtos.FrequencyAudienceRowDto"/>'s own doc. <see cref="UnionPopulationCount"/>/
/// <see cref="AverageLtvOverall"/>/<see cref="AveragePreviousFrequencyOverall"/>/
/// <see cref="AverageCurrentFrequencyOverall"/> are window-function aggregates over the FULL
/// filtered (one-audience) result — same "repeated identically on every row" convention as
/// <see cref="PriceAudienceTableRowRaw"/> — <see cref="UnionPopulationCount"/> specifically is
/// the current∪previous population size BEFORE the audience filter (this table's own
/// "Проаналізовано"-equivalent denominator), not the count matching this one audience.</summary>
public sealed record FrequencyAudienceTableRowRaw(
    Guid CustomerId, string Name, string? Phone,
    int PreviousFrequency, int CurrentFrequency, int FrequencyDeltaAbsolute, decimal? FrequencyDeltaPercent,
    decimal? TypicalCheckCurrent, decimal SpendCurrentPeriod, decimal Ltv,
    int TotalCount, int WithPhoneCount, int UnionPopulationCount,
    decimal AverageLtvOverall, decimal AveragePreviousFrequencyOverall, decimal AverageCurrentFrequencyOverall);

/// <summary>
/// Raw-SQL data access for Фаза 2 price segments + frequency/reactivation (TASK-420, design doc
/// §1). Second raw-SQL repository in this codebase after <see cref="IMarketingAnalyticsRepository"/>
/// — <c>PERCENTILE_CONT</c> has no LINQ/EF-translatable equivalent, and the paginated audience/
/// customer tables need database-level <c>ORDER BY / LIMIT / OFFSET</c> with
/// <c>COUNT(*) OVER()</c> (design doc §2: unlike Фаза 1, these tables can hold tens of thousands
/// of rows and must never be fetched-then-paginated in C#). Every user-controllable filter
/// (tenantId, storeIds, dates, segment/audience ordinals, spend thresholds) is passed as a SQL
/// PARAMETER — never string-interpolated; <c>sortBy</c> is translated through a fixed C# switch
/// allowlist in the implementation before it ever reaches the SQL string (the requested value
/// itself never appears in the query text). Boundaries (P20-P97) and network unit-price figures
/// are recomputed on every call, never cached (design doc §1.2 / plan's "ніколи не кешувати"
/// rule carried over from Фаза 1's quintiles).
/// </summary>
public interface IPriceSegmentsRepository
{
    /// <summary>Median check / receipt count / total spend / items-per-receipt per customer for
    /// ONE window. Called twice by the service for comparison-mode flows (current + previous) —
    /// see <see cref="PriceCustomerPeriodMetricsRow"/>'s own doc.</summary>
    Task<IReadOnlyList<PriceCustomerPeriodMetricsRow>> GetCustomerPeriodMetricsAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Network-wide all-time P20/P40/P60/P80/P90/P97 of the per-customer median-check
    /// distribution (design doc §1.2) — same numbers regardless of which comparison window is
    /// active. Zero-customer tenants get all-zero boundaries (guarded, never null/throw).</summary>
    Task<PriceSegmentBoundariesRow> GetBoundariesAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>Weighted network-wide average unit price for ONE window (design doc §1.7:
    /// <c>SUM(PriceFinal*Quantity)/SUM(Quantity)</c>, packaging excluded) — feeds the Price Index
    /// KPI. Deliberately NOT scoped to identified customers only (a network-wide inflation
    /// signal, not a customer-behavior one) — every non-failed sale counts.</summary>
    Task<decimal> GetAverageUnitPriceAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>All-time LTV for the given customers (empty/null = every customer with purchase
    /// history). Store-filtered, never date-filtered.</summary>
    Task<IReadOnlyList<PriceLtvRow>> GetLtvMapAsync(
        Guid tenantId, IReadOnlyList<Guid>? customerIds, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>Design doc §11 all-time network KPIs.</summary>
    Task<AllTimeKpiRow> GetAllTimeKpiAsync(Guid tenantId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>Design doc §12 monthly trend — one row per calendar month with at least one
    /// receipt, oldest first.</summary>
    Task<IReadOnlyList<MedianCheckMonthRow>> GetMonthlyTrendAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>All-time distribution of the whole customer base across the 7 tiers, classified
    /// against the SAME <paramref name="boundaries"/> the caller already fetched via
    /// <see cref="GetBoundariesAsync"/> — never recomputes them internally, so a single overview
    /// call only computes boundaries once.</summary>
    Task<IReadOnlyList<AllTimeSegmentDistributionRow>> GetAllTimeDistributionAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, PriceSegmentBoundariesRow boundaries, CancellationToken ct = default);

    /// <summary>
    /// Comparison-mode price-audience table — ONE audience (already filtered + classified in
    /// SQL against <paramref name="boundaries"/>), sorted/paginated at the database level.
    /// <paramref name="sortBy"/>: "name" | "segment" | "items" | "check" | "ltv" (unrecognized
    /// values fall back to "check" descending, the competitor's own confirmed default —
    /// analysis doc §9.2). Returns an empty list (never throws) when nobody in the comparison
    /// cohort matches <paramref name="audienceOrdinal"/>.
    /// </summary>
    Task<IReadOnlyList<PriceAudienceTableRowRaw>> GetPriceAudienceTableAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds,
        DateOnly currentFrom, DateOnly currentTo, DateOnly previousFrom, DateOnly previousTo,
        PriceSegmentBoundariesRow boundaries, int audienceOrdinal,
        int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default);

    /// <summary>All-time customer table, optionally filtered to one tier (<paramref
    /// name="segmentOrdinal"/> null = all 7). <paramref name="sortBy"/>: "name" | "segment" |
    /// "items" | "check" | "purchases" | "ltv" (default "check" descending).</summary>
    Task<IReadOnlyList<AllTimeCustomerRowRaw>> GetAllTimeCustomerTableAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, PriceSegmentBoundariesRow boundaries, int? segmentOrdinal,
        int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default);

    /// <summary>
    /// Frequency-audience table — ONE audience, sorted/paginated at the database level.
    /// <paramref name="useCurrentBasis"/> is false only for <see
    /// cref="Domain.Constants.FrequencyAudienceKey.Sleeping"/> (design doc §4): when false, the
    /// <paramref name="minSpend"/>/<paramref name="maxSpend"/>/<paramref name="priceSegmentOrdinal"/>
    /// filters apply to the customer's PREVIOUS-period spend/median-check instead of current
    /// (the displayed <see cref="FrequencyAudienceTableRowRaw.SpendCurrentPeriod"/>/
    /// <see cref="FrequencyAudienceTableRowRaw.TypicalCheckCurrent"/> columns themselves never
    /// change meaning). <paramref name="sortBy"/>: "name" | "previous" | "current" | "delta" |
    /// "check" | "spend" | "ltv" (default "delta" descending... for Sleeping/Declining this
    /// naturally surfaces the biggest drop first; see implementation for the exact per-audience
    /// default).
    /// </summary>
    Task<IReadOnlyList<FrequencyAudienceTableRowRaw>> GetFrequencyAudienceTableAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds,
        DateOnly currentFrom, DateOnly currentTo, DateOnly previousFrom, DateOnly previousTo,
        decimal declineThresholdPercent, PriceSegmentBoundariesRow boundaries, int audienceOrdinal,
        bool useCurrentBasis, decimal? minSpend, decimal? maxSpend, int? priceSegmentOrdinal,
        int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default);

    // ── Settings (TASK-419's PriceSegmentSettings entity — same lazy-upsert shape as
    // ILoyaltyRepository's GetSettingsAsync/AddSettingsAsync/UpdateSettings/SaveChangesAsync)
    // ─────────────────────────────────────────────────────────────────────────────────────────

    Task<PriceSegmentSettings?> GetSettingsAsync(Guid tenantId, CancellationToken ct = default);

    Task AddSettingsAsync(PriceSegmentSettings settings, CancellationToken ct = default);

    void UpdateSettings(PriceSegmentSettings settings);

    Task SaveChangesAsync(CancellationToken ct = default);
}
