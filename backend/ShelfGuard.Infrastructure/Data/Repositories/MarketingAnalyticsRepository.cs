using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.MarketingAnalytics;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// TASK-406: first raw-SQL repository in this codebase — <c>NTILE(5)</c> (R/F/M quintile
/// scoring) and the segment-scoped aggregations below have no LINQ/EF-translatable
/// equivalent. Every query uses <c>Database.SqlQueryRaw&lt;T&gt;</c> with <c>{n}</c>
/// positional placeholders, which EF Core rewrites into real provider parameters (Npgsql
/// <c>@p0</c>, <c>@p1</c>, ...) — NEVER string-interpolated values. Verified empirically
/// against a live Postgres instance during implementation (POCO column-to-property mapping,
/// <c>Guid[]</c> array parameters, <c>DateOnly</c> parameters/columns) before writing this
/// file for real; see the live integration tests in
/// ShelfGuard.Tests/Infrastructure/MarketingAnalyticsRepositoryIntegrationTests.cs.
///
/// Store filter convention used everywhere below: the caller (service layer) always passes a
/// non-null <c>Guid[]</c> for storeIds (empty array = "no restriction, all stores" — the
/// service normalizes null/empty to the same thing before calling in). Every query guards with
/// <c>cardinality({n}::uuid[]) = 0 OR col = ANY({n}::uuid[])</c> so an empty array matches
/// everything rather than nothing.
///
/// "Fiscalization_failed" exclusion mirrors the existing convention in
/// AnalyticsRepository.BuildPosTransactionQuery (the only other place that decides which POS
/// transactions count as "real" for reporting purposes).
/// </summary>
public sealed class MarketingAnalyticsRepository : IMarketingAnalyticsRepository
{
    private readonly AppDbContext _db;

    public MarketingAnalyticsRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<RfmScoredCustomerRow>> GetScoredCustomersAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);
        var (fromDt, toDt) = ToUtcRange(from, to);

        // Params: {0}=tenantId {1}=stores {2}=fromDt {3}=toDt {4}=to(periodEnd, date)
        const string sql = """
            WITH window_tx AS (
                SELECT t."CustomerId" AS cust_id, t."TotalAmount" AS amount, t."CreatedAt" AS created_at
                FROM pos_transactions t
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {2} AND t."CreatedAt" <= {3}
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
            ),
            window_agg AS (
                SELECT cust_id,
                       COUNT(*)::int AS receipt_count,
                       SUM(amount) AS period_revenue,
                       MAX(created_at) AS last_purchase_in_window
                FROM window_tx
                GROUP BY cust_id
            ),
            lifetime_tx AS (
                SELECT t."CustomerId" AS cust_id, t."CreatedAt" AS created_at
                FROM pos_transactions t
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
            ),
            lifetime_agg AS (
                SELECT cust_id,
                       COUNT(*)::int AS lifetime_receipt_count,
                       MIN(created_at) AS first_purchase_ever,
                       MAX(created_at) AS last_purchase_ever
                FROM lifetime_tx
                GROUP BY cust_id
            ),
            scored_base AS (
                SELECT
                    w.cust_id,
                    w.receipt_count,
                    w.period_revenue,
                    -- Whole-day integer difference (never EXTRACT(DAY FROM interval), which only
                    -- returns an interval's "days" field, not the total elapsed days across months).
                    ({4}::date - (w.last_purchase_in_window AT TIME ZONE 'UTC')::date) AS days_since_last_purchase,
                    l.lifetime_receipt_count,
                    l.first_purchase_ever,
                    l.last_purchase_ever
                FROM window_agg w
                JOIN lifetime_agg l ON l.cust_id = w.cust_id
            )
            SELECT
                cust_id AS "CustomerId",
                -- recency_score = 6 - NTILE(5) OVER (ORDER BY days_since_last_purchase ASC) — plan formula, verbatim.
                (6 - NTILE(5) OVER (ORDER BY days_since_last_purchase ASC))::int AS "RecencyScore",
                (NTILE(5) OVER (ORDER BY receipt_count ASC))::int AS "FrequencyScore",
                (NTILE(5) OVER (ORDER BY period_revenue ASC))::int AS "MonetaryScore",
                period_revenue AS "PeriodRevenue",
                receipt_count AS "PeriodReceiptCount",
                (first_purchase_ever AT TIME ZONE 'UTC')::date AS "FirstPurchaseDateEver",
                lifetime_receipt_count AS "LifetimeReceiptCount",
                (last_purchase_ever AT TIME ZONE 'UTC')::date AS "LastPurchaseDateEver"
            FROM scored_base
            """;

        var rows = await _db.Database
            .SqlQueryRaw<RfmScoredCustomerRow>(sql, tenantId, stores, fromDt, toDt, to)
            .ToListAsync(ct);
        return rows;
    }

    public async Task<RfmCustomerBaseCountsRow> GetCustomerBaseCountsAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);

        // Params: {0}=tenantId {1}=stores
        const string sql = """
            SELECT
                (SELECT COUNT(*)::int FROM customers c WHERE c."TenantId" = {0}) AS "RegisteredCount",
                (SELECT COUNT(DISTINCT t."CustomerId")::int
                   FROM pos_transactions t
                   WHERE t."TenantId" = {0}
                     AND t."CustomerId" IS NOT NULL
                     AND t."Status" <> 'fiscalization_failed'
                     AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
                ) AS "EverPurchasedCount"
            """;

        var rows = await _db.Database.SqlQueryRaw<RfmCustomerBaseCountsRow>(sql, tenantId, stores).ToListAsync(ct);
        return rows[0];
    }

    public async Task<IReadOnlyList<RfmTopProductRow>> GetTopProductsAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, int limit, CancellationToken ct = default)
    {
        if (segmentCustomerIds.Count == 0) return [];
        var stores = Normalize(storeIds);
        var (fromDt, toDt) = ToUtcRange(from, to);

        // Params: {0}=tenantId {1}=segmentCustomerIds {2}=stores {3}=fromDt {4}=toDt {5}=limit
        const string sql = """
            WITH seg_items AS (
                SELECT i."Name" AS product_name, i."Barcodes" AS barcodes,
                       ti."TransactionId" AS txn_id, t."CustomerId" AS cust_id
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN items i ON i."Id" = ti."ProductId"
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" = ANY({1}::uuid[])
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {3} AND t."CreatedAt" <= {4}
                  AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
                  AND i."ItemType" <> 'packaging'
            )
            SELECT
                product_name AS "ProductName",
                COUNT(DISTINCT cust_id)::int AS "UniqueCustomerCount",
                COUNT(DISTINCT txn_id)::int AS "ReceiptCount",
                MIN(barcodes ->> 0) AS "Barcode"
            FROM seg_items
            GROUP BY product_name
            ORDER BY COUNT(DISTINCT cust_id) DESC, product_name
            LIMIT {5}
            """;

        return await _db.Database
            .SqlQueryRaw<RfmTopProductRow>(sql, tenantId, segmentCustomerIds.ToArray(), stores, fromDt, toDt, limit)
            .ToListAsync(ct);
    }

    public async Task<RfmBehaviorRow> GetBehaviorAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (segmentCustomerIds.Count == 0)
            return new RfmBehaviorRow(0, 0m, null, 0m, [], []);

        var stores = Normalize(storeIds);
        var (fromDt, toDt) = ToUtcRange(from, to);
        var custIds = segmentCustomerIds.ToArray();

        // Params: {0}=tenantId {1}=segmentCustomerIds {2}=stores {3}=fromDt {4}=toDt {5}=to(periodEnd, date)
        const string aggSql = """
            WITH seg_tx AS (
                SELECT t."CustomerId" AS cust_id, t."TotalAmount" AS amount, t."CreatedAt" AS created_at
                FROM pos_transactions t
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" = ANY({1}::uuid[])
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {3} AND t."CreatedAt" <= {4}
                  AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
            ),
            per_customer_last AS (
                SELECT cust_id, MAX(created_at) AS last_purchase
                FROM seg_tx
                GROUP BY cust_id
            )
            SELECT
                (SELECT COUNT(*) FROM seg_tx)::int AS "ReceiptCount",
                COALESCE((SELECT SUM(amount) FROM seg_tx), 0) AS "TotalRevenue",
                (SELECT (MAX(created_at) AT TIME ZONE 'UTC')::date FROM seg_tx) AS "LastVisit",
                COALESCE(
                    (SELECT AVG({5}::date - (last_purchase AT TIME ZONE 'UTC')::date) FROM per_customer_last),
                    0
                )::numeric AS "AverageRecencyDays"
            """;

        // Params: {0}=tenantId {1}=segmentCustomerIds {2}=stores {3}=fromDt {4}=toDt
        const string daySql = """
            SELECT
                EXTRACT(ISODOW FROM (t."CreatedAt" AT TIME ZONE 'Europe/Kyiv'))::int AS "DayOfWeekIso",
                COUNT(*)::int AS "ReceiptCount"
            FROM pos_transactions t
            WHERE t."TenantId" = {0}
              AND t."CustomerId" = ANY({1}::uuid[])
              AND t."Status" <> 'fiscalization_failed'
              AND t."CreatedAt" >= {3} AND t."CreatedAt" <= {4}
              AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
            GROUP BY 1
            ORDER BY 1
            """;

        const string hourSql = """
            SELECT
                EXTRACT(HOUR FROM (t."CreatedAt" AT TIME ZONE 'Europe/Kyiv'))::int AS "Hour",
                COUNT(*)::int AS "ReceiptCount"
            FROM pos_transactions t
            WHERE t."TenantId" = {0}
              AND t."CustomerId" = ANY({1}::uuid[])
              AND t."Status" <> 'fiscalization_failed'
              AND t."CreatedAt" >= {3} AND t."CreatedAt" <= {4}
              AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
            GROUP BY 1
            ORDER BY 1
            """;

        var aggRows = await _db.Database
            .SqlQueryRaw<RfmBehaviorAggRow>(aggSql, tenantId, custIds, stores, fromDt, toDt, to)
            .ToListAsync(ct);
        var agg = aggRows[0];

        var byDay = await _db.Database
            .SqlQueryRaw<RfmDayCountRow>(daySql, tenantId, custIds, stores, fromDt, toDt)
            .ToListAsync(ct);

        var byHour = await _db.Database
            .SqlQueryRaw<RfmHourCountRow>(hourSql, tenantId, custIds, stores, fromDt, toDt)
            .ToListAsync(ct);

        return new RfmBehaviorRow(agg.ReceiptCount, agg.TotalRevenue, agg.LastVisit, agg.AverageRecencyDays, byDay, byHour);
    }

    public async Task<RfmLtvRow> GetLtvAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        if (segmentCustomerIds.Count == 0) return new RfmLtvRow(0m, 0m);
        var stores = Normalize(storeIds);

        // All-time — deliberately NO date filter (plan: "LTV завжди all-time"). Store filter
        // still applies. Params: {0}=tenantId {1}=segmentCustomerIds {2}=stores
        const string sql = """
            WITH seg_tx AS (
                SELECT t."CustomerId" AS cust_id, t."TotalAmount" AS amount
                FROM pos_transactions t
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" = ANY({1}::uuid[])
                  AND t."Status" <> 'fiscalization_failed'
                  AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
            ),
            per_customer AS (
                SELECT cust_id, SUM(amount) AS total_spent
                FROM seg_tx
                GROUP BY cust_id
            )
            SELECT
                COALESCE(AVG(total_spent), 0) AS "AverageLtv",
                COALESCE(SUM(total_spent), 0) AS "TotalLtv"
            FROM per_customer
            """;

        var rows = await _db.Database
            .SqlQueryRaw<RfmLtvRow>(sql, tenantId, segmentCustomerIds.ToArray(), stores)
            .ToListAsync(ct);
        return rows[0];
    }

    public async Task<IReadOnlyList<RfmAffinityRow>> GetAffinityAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, string anchorProductName, int candidatePoolSize, int topN,
        CancellationToken ct = default)
    {
        if (segmentCustomerIds.Count == 0) return [];
        var stores = Normalize(storeIds);
        var (fromDt, toDt) = ToUtcRange(from, to);

        // Params: {0}=tenantId {1}=segmentCustomerIds {2}=stores {3}=fromDt {4}=toDt
        //         {5}=anchorProductName {6}=candidatePoolSize {7}=topN
        const string sql = """
            WITH seg_items AS (
                SELECT t."CustomerId" AS cust_id, i."Name" AS product_name, i."Barcodes" AS barcodes
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN items i ON i."Id" = ti."ProductId"
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" = ANY({1}::uuid[])
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {3} AND t."CreatedAt" <= {4}
                  AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
                  AND i."ItemType" <> 'packaging'
            ),
            segment_size AS (
                SELECT (SELECT COUNT(DISTINCT x) FROM unnest({1}::uuid[]) x)::numeric AS n
            ),
            anchor_buyers AS (
                SELECT DISTINCT cust_id FROM seg_items WHERE product_name = {5}
            ),
            anchor_count AS (
                SELECT COUNT(*)::numeric AS n FROM anchor_buyers
            ),
            candidate_pool AS (
                SELECT product_name, MIN(barcodes ->> 0) AS barcode, COUNT(DISTINCT cust_id)::numeric AS total_buyers
                FROM seg_items
                WHERE product_name <> {5}
                GROUP BY product_name
                ORDER BY COUNT(DISTINCT cust_id) DESC
                LIMIT {6}
            ),
            both_buyers AS (
                SELECT cp.product_name, cp.barcode, cp.total_buyers,
                       COUNT(DISTINCT si.cust_id)::numeric AS both_count
                FROM candidate_pool cp
                JOIN seg_items si ON si.product_name = cp.product_name
                JOIN anchor_buyers ab ON ab.cust_id = si.cust_id
                GROUP BY cp.product_name, cp.barcode, cp.total_buyers
            )
            SELECT
                b.product_name AS "ProductName",
                ROUND(
                    (b.both_count / NULLIF(ac.n, 0)) / NULLIF(b.total_buyers / NULLIF(ss.n, 0), 0)
                , 4) AS "Lift",
                b.both_count::int AS "BothBuyersCount",
                ROUND((b.both_count / NULLIF(ac.n, 0)) * 100, 2) AS "ShareAmongAnchorBuyersPercent",
                ROUND((b.total_buyers / NULLIF(ss.n, 0)) * 100, 2) AS "ShareAmongSegmentPercent",
                b.barcode AS "Barcode"
            FROM both_buyers b, anchor_count ac, segment_size ss
            ORDER BY "Lift" DESC NULLS LAST
            LIMIT {7}
            """;

        return await _db.Database
            .SqlQueryRaw<RfmAffinityRow>(
                sql, tenantId, segmentCustomerIds.ToArray(), stores, fromDt, toDt,
                anchorProductName, candidatePoolSize, topN)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RfmBasketRow>> GetBasketAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, string anchorProductName, int topN, CancellationToken ct = default)
    {
        if (segmentCustomerIds.Count == 0) return [];
        var stores = Normalize(storeIds);
        var (fromDt, toDt) = ToUtcRange(from, to);

        // "Разом у чеку" is receipt-level co-occurrence — a different population/formula than
        // affinity (customer-level lift). Both scoped to the SAME segment's receipts.
        // Params: {0}=tenantId {1}=segmentCustomerIds {2}=stores {3}=fromDt {4}=toDt
        //         {5}=anchorProductName {6}=candidatePoolSize(reusing topN*20 cap below) {7}=topN
        const string sql = """
            WITH seg_receipts AS (
                SELECT t."Id" AS txn_id
                FROM pos_transactions t
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" = ANY({1}::uuid[])
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {3} AND t."CreatedAt" <= {4}
                  AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
            ),
            receipt_items AS (
                SELECT ti."TransactionId" AS txn_id, i."Name" AS product_name, i."Barcodes" AS barcodes
                FROM pos_transaction_items ti
                JOIN items i ON i."Id" = ti."ProductId"
                WHERE ti."TransactionId" IN (SELECT txn_id FROM seg_receipts)
                  AND i."ItemType" <> 'packaging'
            ),
            anchor_receipts AS (
                SELECT DISTINCT txn_id FROM receipt_items WHERE product_name = {5}
            ),
            anchor_receipt_count AS (
                SELECT COUNT(*)::numeric AS n FROM anchor_receipts
            ),
            candidate_pool AS (
                SELECT product_name, MIN(barcodes ->> 0) AS barcode
                FROM receipt_items
                WHERE product_name <> {5}
                GROUP BY product_name
                ORDER BY COUNT(DISTINCT txn_id) DESC
                LIMIT {6}
            ),
            both_in_receipt AS (
                SELECT cp.product_name, cp.barcode, COUNT(DISTINCT ri.txn_id)::int AS both_count
                FROM candidate_pool cp
                JOIN receipt_items ri ON ri.product_name = cp.product_name
                JOIN anchor_receipts ar ON ar.txn_id = ri.txn_id
                GROUP BY cp.product_name, cp.barcode
            )
            SELECT
                b.product_name AS "ProductName",
                b.both_count AS "BothReceiptsCount",
                ac.n::int AS "AnchorReceiptsCount",
                b.barcode AS "Barcode"
            FROM both_in_receipt b, anchor_receipt_count ac
            ORDER BY b.both_count DESC
            LIMIT {7}
            """;

        // Candidate pool capped generously above topN so ranking-by-both_count still has
        // enough breadth to choose from (plan: cap pool before ranking — same idea as affinity).
        const int candidatePoolSize = 200;

        return await _db.Database
            .SqlQueryRaw<RfmBasketRow>(
                sql, tenantId, segmentCustomerIds.ToArray(), stores, fromDt, toDt,
                anchorProductName, candidatePoolSize, topN)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RfmExportCustomerRow>> GetExportCustomersAsync(
        Guid tenantId, IReadOnlyList<Guid> customerIds, CancellationToken ct = default)
    {
        if (customerIds.Count == 0) return [];

        // Plain LINQ — Customer is a normal mapped entity, no aggregation/window function
        // involved, so this deliberately does NOT use raw SQL (unlike every method above).
        var idSet = customerIds.ToHashSet();
        return await _db.Customers
            .Where(c => c.TenantId == tenantId && idSet.Contains(c.Id))
            .Select(c => new RfmExportCustomerRow(c.Id, c.Name, c.Phone, c.Email, c.TotalOrders, c.TotalSpent))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetProductBuyerCustomerIdsAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, string productName, CancellationToken ct = default)
    {
        if (segmentCustomerIds.Count == 0) return [];
        var stores = Normalize(storeIds);
        var (fromDt, toDt) = ToUtcRange(from, to);

        // Params: {0}=tenantId {1}=segmentCustomerIds {2}=stores {3}=fromDt {4}=toDt {5}=productName
        const string sql = """
            SELECT DISTINCT t."CustomerId" AS "CustomerId"
            FROM pos_transaction_items ti
            JOIN pos_transactions t ON t."Id" = ti."TransactionId"
            JOIN items i ON i."Id" = ti."ProductId"
            WHERE t."TenantId" = {0}
              AND t."CustomerId" = ANY({1}::uuid[])
              AND t."Status" <> 'fiscalization_failed'
              AND t."CreatedAt" >= {3} AND t."CreatedAt" <= {4}
              AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
              AND i."Name" = {5}
            """;

        var rows = await _db.Database
            .SqlQueryRaw<RfmCustomerIdRow>(sql, tenantId, segmentCustomerIds.ToArray(), stores, fromDt, toDt, productName)
            .ToListAsync(ct);
        return rows.Select(r => r.CustomerId).ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetProductPairBuyerCustomerIdsAsync(
        Guid tenantId, IReadOnlyList<Guid> segmentCustomerIds, IReadOnlyList<Guid>? storeIds,
        DateOnly from, DateOnly to, string productName, string pairedProductName, CancellationToken ct = default)
    {
        if (segmentCustomerIds.Count == 0) return [];
        var stores = Normalize(storeIds);
        var (fromDt, toDt) = ToUtcRange(from, to);

        // Customer-level intersection — serves the export button from either the affinity or
        // the basket tab identically (plan §15.3: "покупці, які купували і базовий, і супутній").
        // Params: {0}=tenantId {1}=segmentCustomerIds {2}=stores {3}=fromDt {4}=toDt
        //         {5}=productName {6}=pairedProductName
        const string sql = """
            WITH buyers_a AS (
                SELECT DISTINCT t."CustomerId" AS cust_id
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN items i ON i."Id" = ti."ProductId"
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" = ANY({1}::uuid[])
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {3} AND t."CreatedAt" <= {4}
                  AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
                  AND i."Name" = {5}
            ),
            buyers_b AS (
                SELECT DISTINCT t."CustomerId" AS cust_id
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN items i ON i."Id" = ti."ProductId"
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" = ANY({1}::uuid[])
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {3} AND t."CreatedAt" <= {4}
                  AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
                  AND i."Name" = {6}
            )
            SELECT a.cust_id AS "CustomerId" FROM buyers_a a JOIN buyers_b b ON b.cust_id = a.cust_id
            """;

        var rows = await _db.Database
            .SqlQueryRaw<RfmCustomerIdRow>(
                sql, tenantId, segmentCustomerIds.ToArray(), stores, fromDt, toDt, productName, pairedProductName)
            .ToListAsync(ct);
        return rows.Select(r => r.CustomerId).ToList();
    }

    // ── helpers ───────────────────────────────────────────────────────────

    /// <summary>Null/empty both mean "no store restriction" — normalized to one array shape
    /// so every SQL string only needs the single `cardinality(...) = 0 OR ...` guard.</summary>
    private static Guid[] Normalize(IReadOnlyList<Guid>? storeIds) =>
        storeIds is null or { Count: 0 } ? [] : storeIds.Distinct().ToArray();

    // Npgsql rejects DateTime with Kind=Unspecified as a parameter for timestamptz columns —
    // mirrors AnalyticsRepository.ToUtcStart/ToUtcEnd exactly.
    private static (DateTime FromDt, DateTime ToDt) ToUtcRange(DateOnly from, DateOnly to) => (
        from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));

    /// <summary>Internal-only row shape for the scalar half of GetBehaviorAsync's 3 queries —
    /// never exposed past this class (the public method assembles the full RfmBehaviorRow).</summary>
    private sealed class RfmBehaviorAggRow
    {
        public int ReceiptCount { get; set; }
        public decimal TotalRevenue { get; set; }
        public DateOnly? LastVisit { get; set; }
        public decimal AverageRecencyDays { get; set; }
    }
}
