namespace ShelfGuard.Application.Features.MarketingAnalytics;

// ── Raw rows returned by the repository's SQL — the SERVICE maps these into the wire-facing
// Dtos/MarketingAnalyticsDtos.cs records. Kept separate on purpose: these shapes exist purely
// to carry SQL-computed numbers across the Infrastructure→Application boundary, and must never
// be serialized directly (e.g. they use plain int/decimal, not the RfmSegmentKey enum — the
// repository never classifies, only scores; classification is RfmSegmentClassifier's job).

/// <summary>One row per customer who purchased at least once within [from,to] (+ store
/// filter). R/F/M scores are windowed to that population; the three "*Ever" fields are
/// LIFETIME facts (ignore the date filter) — exactly what RfmSegmentClassifier needs.</summary>
public sealed record RfmScoredCustomerRow(
    Guid CustomerId,
    int RecencyScore,
    int FrequencyScore,
    int MonetaryScore,
    decimal PeriodRevenue,
    int PeriodReceiptCount,
    DateOnly FirstPurchaseDateEver,
    int LifetimeReceiptCount,
    DateOnly LastPurchaseDateEver);

/// <summary>Whole-tenant base counts, independent of the date filter (store filter still
/// applies) — powers the "Купували будь-коли" / "Без покупок" KPI card.</summary>
public sealed record RfmCustomerBaseCountsRow(int RegisteredCount, int EverPurchasedCount);

public sealed record RfmTopProductRow(string ProductName, int UniqueCustomerCount, int ReceiptCount, string? Barcode);

public sealed record RfmDayCountRow(int DayOfWeekIso, int ReceiptCount);
public sealed record RfmHourCountRow(int Hour, int ReceiptCount);

public sealed record RfmBehaviorRow(
    int ReceiptCount,
    decimal TotalRevenue,
    DateOnly? LastVisit,
    decimal AverageRecencyDays,
    IReadOnlyList<RfmDayCountRow> ByDayOfWeek,
    IReadOnlyList<RfmHourCountRow> ByHour);

/// <summary>All-time (never date-filtered — plan: "LTV завжди all-time"), still store-filtered.</summary>
public sealed record RfmLtvRow(decimal AverageLtv, decimal TotalLtv);

public sealed record RfmAffinityRow(
    string ProductName,
    decimal Lift,
    int BothBuyersCount,
    decimal ShareAmongAnchorBuyersPercent,
    decimal ShareAmongSegmentPercent,
    string? Barcode);

public sealed record RfmBasketRow(string ProductName, int BothReceiptsCount, int AnchorReceiptsCount, string? Barcode);

public sealed record RfmExportCustomerRow(
    Guid CustomerId, string Name, string? Phone, string? Email, int TotalOrders, decimal TotalSpent);

/// <summary>Internal SqlQueryRaw target for a bare customer-id projection — never exposed past
/// the repository (callers get a plain <c>IReadOnlyList&lt;Guid&gt;</c>).</summary>
public sealed record RfmCustomerIdRow(Guid CustomerId);

// ── Store migration (TASK-502) raw rows — 1:1 with the matching Dtos/*Dto shape, kept as
// separate Row types for the same reason as every row above: the repository only ever hands
// back SQL-computed facts, never the wire-facing Dto, even when the shapes are identical today.

public sealed record StoreMigrationFlowRow(
    Guid FromStoreId, string FromStoreName, Guid ToStoreId, string ToStoreName, int CustomerCount, decimal Revenue);

public sealed record StoreMigrationCustomerRow(
    Guid CustomerId, string Name, string? Phone, string? Email,
    Guid FromStoreId, string FromStoreName, DateOnly FromDate,
    Guid ToStoreId, string ToStoreName, DateOnly ToDate,
    int TransactionCountInPeriod, decimal RevenueInPeriod);

/// <summary>
/// Raw-SQL data access for RFM scoring and its derived views (TASK-406, plan §"R/F/M-скоринг").
/// First use of <c>Database.SqlQueryRaw&lt;T&gt;</c> in this codebase — <c>NTILE(5)</c> has no
/// LINQ/EF-translatable equivalent. Every user-controllable filter (tenantId, storeIds, dates,
/// product names) is passed as a SQL PARAMETER — never string-interpolated — see
/// MarketingAnalyticsRepository's implementation comments for the exact mechanism.
/// Quintiles are recomputed on every call from the currently-filtered population; nothing here
/// caches R/F/M scores globally (plan's explicit requirement).
/// </summary>
public interface IMarketingAnalyticsRepository
{
    Task<IReadOnlyList<RfmScoredCustomerRow>> GetScoredCustomersAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<RfmCustomerBaseCountsRow> GetCustomerBaseCountsAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>Ranked by unique-customer coverage within <paramref name="segmentCustomerIds"/>
    /// (plan: "не просто за кількістю чеків"). Groups by Item.Name, excludes ItemType="packaging".</summary>
    Task<IReadOnlyList<RfmTopProductRow>> GetTopProductsAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, int limit, CancellationToken ct = default);

    Task<RfmBehaviorRow> GetBehaviorAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<RfmLtvRow> GetLtvAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default);

    /// <summary>Lift for ONE anchor product only (plan: never a full pairwise scan). Candidate
    /// pool is capped at <paramref name="candidatePoolSize"/> (by segment popularity) before
    /// ranking lift, then the top <paramref name="topN"/> by lift are returned.</summary>
    Task<IReadOnlyList<RfmAffinityRow>> GetAffinityAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, string anchorProductName, int candidatePoolSize, int topN,
        CancellationToken ct = default);

    /// <summary>"Разом у чеку" — receipt-level co-occurrence, a different formula than affinity.
    /// Scoped to the segment's own receipts (same population as the affinity call above).</summary>
    Task<IReadOnlyList<RfmBasketRow>> GetBasketAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, string anchorProductName, int topN, CancellationToken ct = default);

    Task<IReadOnlyList<RfmExportCustomerRow>> GetExportCustomersAsync(
        Guid tenantId, IReadOnlyList<Guid> customerIds, CancellationToken ct = default);

    /// <summary>Segment customers who bought <paramref name="productName"/> at least once in
    /// the window — feeds the "Покупці у файл" export under a top product.</summary>
    Task<IReadOnlyList<Guid>> GetProductBuyerCustomerIdsAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, string productName, CancellationToken ct = default);

    /// <summary>Segment customers who bought BOTH products at least once in the window — feeds
    /// the "Покупці обох → файл" export from either the affinity or basket tab.</summary>
    Task<IReadOnlyList<Guid>> GetProductPairBuyerCustomerIdsAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, string productName, string pairedProductName, CancellationToken ct = default);

    // ── Store migration (TASK-502) ───────────────────────────────────────────────────────────

    /// <summary>Customers with >=1 non-failed transaction in [from,to] (store filter applied) —
    /// PERIOD-scoped, unlike <see cref="GetCustomerBaseCountsAsync"/>'s lifetime
    /// <c>EverPurchasedCount</c>. Feeds <c>StoreMigrationOverviewDto.ActiveCustomerCount</c>.</summary>
    Task<int> GetActivePeriodCustomerCountAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Aggregated from-store→to-store matrix (non-zero cells only) for customers whose
    /// first and last transaction in [from,to] were at different stores. A cell matches the
    /// store filter if EITHER its from-store or its to-store is in <paramref name="storeIds"/>
    /// (empty = all stores).</summary>
    Task<IReadOnlyList<StoreMigrationFlowRow>> GetStoreMigrationFlowsAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Per-customer drill-down rows for the same migrated population as
    /// <see cref="GetStoreMigrationFlowsAsync"/>, ordered most-recent-migration-first and capped
    /// at <paramref name="limit"/> — the caller passes a small limit for the on-screen table and
    /// a large one (export row cap) for the Excel export.</summary>
    Task<IReadOnlyList<StoreMigrationCustomerRow>> GetStoreMigrationCustomersAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, int limit, CancellationToken ct = default);
}
