using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// TASK-420: second raw-SQL repository in this codebase after <c>MarketingAnalyticsRepository</c>
/// (Фаза 1) — <c>PERCENTILE_CONT</c> has no LINQ/EF-translatable equivalent, and the three
/// paginated audience/customer tables need database-level <c>ORDER BY / LIMIT / OFFSET</c> with
/// <c>COUNT(*) OVER()</c> window aggregates (design doc §2: unlike Фаза 1, these tables can hold
/// tens of thousands of rows and must never be fetched-then-paginated in C#).
///
/// Same parameterization discipline as Фаза 1: every query uses <c>Database.SqlQueryRaw&lt;T&gt;</c>
/// with <c>{n}</c> positional placeholders (EF rewrites these into real Npgsql parameters, never
/// string-interpolated values) — including every user-controllable filter (tenantId, storeIds,
/// dates, segment/audience ordinals, spend thresholds). The three paginated methods ALSO need a
/// dynamic <c>ORDER BY</c> column + direction, which is NOT parameterizable in standard SQL — the
/// safe pattern used here: the requested <c>sortBy</c> string is first passed through
/// <see cref="PriceSegmentSortKeys"/>'s fixed allowlist switch (never reaches the SQL string
/// itself), producing one of a small set of HARDCODED literal column names; that literal is then
/// substituted into the SQL template via a plain <c>string.Replace</c> on unique
/// <c>{SORT_COLUMN}</c>/<c>{SORT_DIRECTION}</c> tokens BEFORE the string is handed to
/// <c>SqlQueryRaw</c> — deliberately different token syntax from EF's own <c>{0}</c>.."{n}"
/// placeholders so the two substitution mechanisms can never collide (a C# interpolated string
/// for the whole query would have tried to evaluate <c>{0}</c> etc. as C# expressions, which is
/// exactly why this class never uses <c>$"""..."""</c> for a template containing SqlQueryRaw
/// placeholders).
///
/// <b>Kept in sync with the pure classifiers:</b> the CASE-expression ladders below
/// (segment tier, price audience, frequency audience) are the SQL mirror of
/// <see cref="PriceSegmentClassifier"/>/<see cref="PriceAudienceClassifier"/>/
/// <see cref="FrequencyAudienceClassifier"/> — any change to a threshold/comparison in one place
/// must be mirrored in the other (documented on both sides).
/// </summary>
public sealed class PriceSegmentsRepository : IPriceSegmentsRepository
{
    private readonly AppDbContext _db;

    public PriceSegmentsRepository(AppDbContext db) => _db = db;

    // ── Shared per-customer metrics (design doc §1.1) ───────────────────────────────────────

    public async Task<IReadOnlyList<PriceCustomerPeriodMetricsRow>> GetCustomerPeriodMetricsAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);
        var (fromDt, toDt) = ToUtcRange(from, to);

        // "Товарів у чеку" = SUM(Quantity) / COUNT(DISTINCT receipt), packaging excluded (design
        // doc §1.5). Params: {0}=tenantId {1}=stores {2}=fromDt {3}=toDt
        const string sql = """
            WITH tx AS (
                SELECT t."Id" AS txn_id, t."CustomerId" AS cust_id, t."TotalAmount" AS amount
                FROM pos_transactions t
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {2} AND t."CreatedAt" <= {3}
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
            ),
            txn_qty AS (
                SELECT ti."TransactionId" AS txn_id, SUM(ti."Quantity") AS qty_sum
                FROM pos_transaction_items ti
                JOIN items i ON i."Id" = ti."ProductId"
                WHERE ti."TransactionId" IN (SELECT txn_id FROM tx)
                  AND i."ItemType" <> 'packaging'
                GROUP BY ti."TransactionId"
            )
            SELECT
                tx.cust_id AS "CustomerId",
                (PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY tx.amount))::numeric AS "MedianCheck",
                COUNT(*)::int AS "ReceiptCount",
                SUM(tx.amount) AS "TotalSpend",
                (COALESCE(SUM(tq.qty_sum), 0) / COUNT(*)::numeric) AS "ItemsPerReceipt"
            FROM tx
            LEFT JOIN txn_qty tq ON tq.txn_id = tx.txn_id
            GROUP BY tx.cust_id
            """;

        return await _db.Database
            .SqlQueryRaw<PriceCustomerPeriodMetricsRow>(sql, tenantId, stores, fromDt, toDt)
            .ToListAsync(ct);
    }

    // ── Boundaries + network figures (design doc §1.2 / §1.7) ───────────────────────────────

    public async Task<PriceSegmentBoundariesRow> GetBoundariesAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);

        // All-time, live, never cached (design doc §1.2 — confirmed network-scoped, not window-
        // scoped, by the competitor's own boundaries staying identical across 30/60/90-day
        // windows). Zero-customer tenants get all-zero boundaries via COALESCE, never null/throw
        // — PERCENTILE_CONT without GROUP BY always returns exactly one row even over 0 input
        // rows (a bare NULL), so `rows[0]` below is always safe. Params: {0}=tenantId {1}=stores
        const string sql = """
            WITH customer_medians AS (
                SELECT t."CustomerId" AS cust_id,
                       (PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY t."TotalAmount"))::numeric AS median_check
                FROM pos_transactions t
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
                GROUP BY t."CustomerId"
            )
            SELECT
                COALESCE((PERCENTILE_CONT(0.20) WITHIN GROUP (ORDER BY median_check))::numeric, 0) AS "P20",
                COALESCE((PERCENTILE_CONT(0.40) WITHIN GROUP (ORDER BY median_check))::numeric, 0) AS "P40",
                COALESCE((PERCENTILE_CONT(0.60) WITHIN GROUP (ORDER BY median_check))::numeric, 0) AS "P60",
                COALESCE((PERCENTILE_CONT(0.80) WITHIN GROUP (ORDER BY median_check))::numeric, 0) AS "P80",
                COALESCE((PERCENTILE_CONT(0.90) WITHIN GROUP (ORDER BY median_check))::numeric, 0) AS "P90",
                COALESCE((PERCENTILE_CONT(0.97) WITHIN GROUP (ORDER BY median_check))::numeric, 0) AS "P97"
            FROM customer_medians
            """;

        var rows = await _db.Database.SqlQueryRaw<PriceSegmentBoundariesRow>(sql, tenantId, stores).ToListAsync(ct);
        return rows[0];
    }

    public async Task<decimal> GetAverageUnitPriceAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);
        var (fromDt, toDt) = ToUtcRange(from, to);

        // Network-wide, weighted by quantity (design doc §1.7), deliberately NOT scoped to
        // identified customers — a store-level inflation signal, not a customer-behavior one.
        // Params: {0}=tenantId {1}=stores {2}=fromDt {3}=toDt
        const string sql = """
            SELECT COALESCE(SUM(ti."PriceFinal" * ti."Quantity") / NULLIF(SUM(ti."Quantity"), 0), 0)
            FROM pos_transaction_items ti
            JOIN pos_transactions t ON t."Id" = ti."TransactionId"
            JOIN items i ON i."Id" = ti."ProductId"
            WHERE t."TenantId" = {0}
              AND t."Status" <> 'fiscalization_failed'
              AND t."CreatedAt" >= {2} AND t."CreatedAt" <= {3}
              AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
              AND i."ItemType" <> 'packaging'
            """;

        var rows = await _db.Database.SqlQueryRaw<decimal>(sql, tenantId, stores, fromDt, toDt).ToListAsync(ct);
        return rows.Count > 0 ? rows[0] : 0m;
    }

    public async Task<IReadOnlyList<PriceLtvRow>> GetLtvMapAsync(
        Guid tenantId, IReadOnlyList<Guid>? customerIds, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var custIds = Normalize(customerIds);
        var stores = Normalize(storeIds);

        // All-time, never date-filtered (LTV rule Фаза 1's RfmLtvRow already follows). Empty
        // customerIds means "every customer with purchase history" — same cardinality(...)=0
        // idiom as the store filter. Params: {0}=tenantId {1}=customerIds {2}=stores
        const string sql = """
            SELECT
                t."CustomerId" AS "CustomerId",
                SUM(t."TotalAmount") AS "Ltv"
            FROM pos_transactions t
            WHERE t."TenantId" = {0}
              AND t."CustomerId" IS NOT NULL
              AND t."Status" <> 'fiscalization_failed'
              AND (cardinality({1}::uuid[]) = 0 OR t."CustomerId" = ANY({1}::uuid[]))
              AND (cardinality({2}::uuid[]) = 0 OR t."LocationId" = ANY({2}::uuid[]))
            GROUP BY t."CustomerId"
            """;

        return await _db.Database.SqlQueryRaw<PriceLtvRow>(sql, tenantId, custIds, stores).ToListAsync(ct);
    }

    // ── All-time KPIs / trend / distribution (design doc §11-§14) ───────────────────────────

    public async Task<AllTimeKpiRow> GetAllTimeKpiAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);

        // "Клієнтів у базі" = COUNT(DISTINCT CustomerId) over pos_transactions, i.e. everyone
        // who ever purchased — NOT the raw `customers` table row count (design doc's
        // AllTimeKpiRow doc comment). Params: {0}=tenantId {1}=stores
        const string sql = """
            SELECT
                COUNT(DISTINCT t."CustomerId")::int AS "CustomersInBase",
                COALESCE(SUM(t."TotalAmount") / NULLIF(COUNT(*), 0), 0) AS "NetworkAverageCheck",
                COUNT(*)::bigint AS "PurchasesTotal",
                COALESCE(SUM(t."TotalAmount"), 0) AS "TurnoverTotal"
            FROM pos_transactions t
            WHERE t."TenantId" = {0}
              AND t."CustomerId" IS NOT NULL
              AND t."Status" <> 'fiscalization_failed'
              AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
            """;

        var rows = await _db.Database.SqlQueryRaw<AllTimeKpiRow>(sql, tenantId, stores).ToListAsync(ct);
        return rows[0];
    }

    public async Task<IReadOnlyList<MedianCheckMonthRow>> GetMonthlyTrendAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);

        // Median of ALL network receipts that month — NOT the average of per-customer medians
        // (design doc item 8). Params: {0}=tenantId {1}=stores
        const string sql = """
            WITH monthly_tx AS (
                SELECT date_trunc('month', t."CreatedAt" AT TIME ZONE 'Europe/Kyiv') AS month_start,
                       t."Id" AS txn_id, t."TotalAmount" AS amount
                FROM pos_transactions t
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
            ),
            monthly_qty AS (
                SELECT mt.txn_id, SUM(ti."Quantity") AS qty
                FROM monthly_tx mt
                JOIN pos_transaction_items ti ON ti."TransactionId" = mt.txn_id
                JOIN items i ON i."Id" = ti."ProductId"
                WHERE i."ItemType" <> 'packaging'
                GROUP BY mt.txn_id
            )
            SELECT
                EXTRACT(YEAR FROM mt.month_start)::int AS "Year",
                EXTRACT(MONTH FROM mt.month_start)::int AS "Month",
                (PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY mt.amount))::numeric AS "MedianCheck",
                (COALESCE(SUM(mq.qty), 0) / COUNT(DISTINCT mt.txn_id)::numeric) AS "ItemsPerReceipt"
            FROM monthly_tx mt
            LEFT JOIN monthly_qty mq ON mq.txn_id = mt.txn_id
            GROUP BY mt.month_start
            ORDER BY mt.month_start
            """;

        return await _db.Database.SqlQueryRaw<MedianCheckMonthRow>(sql, tenantId, stores).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AllTimeSegmentDistributionRow>> GetAllTimeDistributionAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, PriceSegmentBoundariesRow boundaries, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);

        // Params: {0}=tenantId {1}=stores {2}=P20 {3}=P40 {4}=P60 {5}=P80 {6}=P90 {7}=P97
        const string sql = """
            WITH customer_median AS (
                SELECT t."CustomerId" AS cust_id,
                       (PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY t."TotalAmount"))::numeric AS median_check,
                       SUM(t."TotalAmount") AS ltv
                FROM pos_transactions t
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
                GROUP BY t."CustomerId"
            ),
            classified AS (
                SELECT cust_id, ltv,
                    CASE
                        WHEN median_check < {2} THEN 1
                        WHEN median_check < {3} THEN 2
                        WHEN median_check < {4} THEN 3
                        WHEN median_check < {5} THEN 4
                        WHEN median_check < {6} THEN 5
                        WHEN median_check < {7} THEN 6
                        ELSE 7
                    END AS segment_ordinal
                FROM customer_median
            )
            SELECT
                segment_ordinal AS "SegmentOrdinal",
                COUNT(*)::int AS "CustomerCount",
                COALESCE(AVG(ltv), 0) AS "AverageLtv"
            FROM classified
            GROUP BY segment_ordinal
            ORDER BY segment_ordinal
            """;

        return await _db.Database
            .SqlQueryRaw<AllTimeSegmentDistributionRow>(
                sql, tenantId, stores, boundaries.P20, boundaries.P40, boundaries.P60, boundaries.P80, boundaries.P90, boundaries.P97)
            .ToListAsync(ct);
    }

    // ── Paginated tables (design doc §2 — real server-side sort/pagination) ─────────────────

    public async Task<IReadOnlyList<PriceAudienceTableRowRaw>> GetPriceAudienceTableAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds,
        DateOnly currentFrom, DateOnly currentTo, DateOnly previousFrom, DateOnly previousTo,
        PriceSegmentBoundariesRow boundaries, int audienceOrdinal,
        int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);
        var (curFromDt, curToDt) = ToUtcRange(currentFrom, currentTo);
        var (prevFromDt, prevToDt) = ToUtcRange(previousFrom, previousTo);
        var (limit, offset) = NormalizeLimitOffset(page, pageSize);
        var column = PriceAudienceSortColumn(sortBy);
        var direction = Direction(sortDescending);

        // "Проаналізовано" = INNER JOIN of cur/prev (comparison cohort). Segment/audience
        // classification mirrors PriceSegmentClassifier/PriceAudienceClassifier exactly (see
        // class remarks). cohort_total is the WHOLE cohort size BEFORE the audience filter — the
        // per-row "Analyzed" denominator this table's own Recommendation needs, without a second
        // round-trip to the overview endpoint. Params: {0}=tenantId {1}=stores {2}=curFromDt
        // {3}=curToDt {4}=prevFromDt {5}=prevToDt {6}=P20 {7}=P40 {8}=P60 {9}=P80 {10}=P90
        // {11}=P97 {12}=audienceOrdinal {13}=pageSize {14}=offset
        const string sqlTemplate = """
            WITH cur AS (
                SELECT t."CustomerId" AS cust_id,
                       (PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY t."TotalAmount"))::numeric AS median_check,
                       (COALESCE(SUM(tq.qty_sum), 0) / COUNT(*)::numeric) AS items_per_receipt
                FROM pos_transactions t
                LEFT JOIN (
                    SELECT ti."TransactionId" AS txn_id, SUM(ti."Quantity") AS qty_sum
                    FROM pos_transaction_items ti
                    JOIN items i ON i."Id" = ti."ProductId"
                    WHERE i."ItemType" <> 'packaging'
                    GROUP BY ti."TransactionId"
                ) tq ON tq.txn_id = t."Id"
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {2} AND t."CreatedAt" <= {3}
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
                GROUP BY t."CustomerId"
            ),
            prev AS (
                SELECT t."CustomerId" AS cust_id,
                       (PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY t."TotalAmount"))::numeric AS median_check,
                       (COALESCE(SUM(tq.qty_sum), 0) / COUNT(*)::numeric) AS items_per_receipt
                FROM pos_transactions t
                LEFT JOIN (
                    SELECT ti."TransactionId" AS txn_id, SUM(ti."Quantity") AS qty_sum
                    FROM pos_transaction_items ti
                    JOIN items i ON i."Id" = ti."ProductId"
                    WHERE i."ItemType" <> 'packaging'
                    GROUP BY ti."TransactionId"
                ) tq ON tq.txn_id = t."Id"
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {4} AND t."CreatedAt" <= {5}
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
                GROUP BY t."CustomerId"
            ),
            cohort AS (
                SELECT c.cust_id, c.median_check AS current_median, c.items_per_receipt AS current_ipr,
                       p.median_check AS previous_median, p.items_per_receipt AS previous_ipr
                FROM cur c
                JOIN prev p ON p.cust_id = c.cust_id
            ),
            classified AS (
                SELECT cust_id, current_median, current_ipr, previous_median, previous_ipr,
                    CASE WHEN current_median < {6} THEN 1 WHEN current_median < {7} THEN 2
                         WHEN current_median < {8} THEN 3 WHEN current_median < {9} THEN 4
                         WHEN current_median < {10} THEN 5 WHEN current_median < {11} THEN 6
                         ELSE 7 END AS current_seg,
                    CASE WHEN previous_median < {6} THEN 1 WHEN previous_median < {7} THEN 2
                         WHEN previous_median < {8} THEN 3 WHEN previous_median < {9} THEN 4
                         WHEN previous_median < {10} THEN 5 WHEN previous_median < {11} THEN 6
                         ELSE 7 END AS previous_seg
                FROM cohort
            ),
            audience_tagged AS (
                SELECT *,
                    CASE
                        WHEN current_seg > previous_seg AND current_ipr > previous_ipr THEN 1
                        WHEN current_seg > previous_seg THEN 2
                        WHEN current_seg < previous_seg THEN 3
                        ELSE 4
                    END AS audience_ordinal
                FROM classified
            ),
            cohort_total AS (
                SELECT COUNT(*)::int AS n FROM audience_tagged
            ),
            ltv_map AS (
                SELECT "CustomerId" AS cust_id, SUM("TotalAmount") AS ltv
                FROM pos_transactions
                WHERE "TenantId" = {0} AND "CustomerId" IS NOT NULL AND "Status" <> 'fiscalization_failed'
                  AND (cardinality({1}::uuid[]) = 0 OR "LocationId" = ANY({1}::uuid[]))
                GROUP BY "CustomerId"
            ),
            filtered AS (
                SELECT at.*, c."Name" AS name, c."Phone" AS phone, COALESCE(lm.ltv, 0) AS ltv, ct.n AS cohort_total
                FROM audience_tagged at
                JOIN customers c ON c."Id" = at.cust_id
                LEFT JOIN ltv_map lm ON lm.cust_id = at.cust_id
                CROSS JOIN cohort_total ct
                WHERE at.audience_ordinal = {12}
            )
            SELECT
                cust_id AS "CustomerId", name AS "Name", phone AS "Phone",
                previous_seg AS "PreviousSegmentOrdinal", current_seg AS "CurrentSegmentOrdinal",
                previous_ipr AS "ItemsPerReceiptPrevious", current_ipr AS "ItemsPerReceiptCurrent",
                current_median AS "TypicalCheckCurrent", ltv AS "Ltv",
                COUNT(*) OVER()::int AS "TotalCount",
                COUNT(*) FILTER (WHERE phone IS NOT NULL) OVER()::int AS "WithPhoneCount",
                MAX(cohort_total) OVER()::int AS "AnalyzedCount",
                COALESCE(AVG(ltv) OVER(), 0) AS "AverageLtvOverall"
            FROM filtered
            ORDER BY {SORT_COLUMN} {SORT_DIRECTION}, cust_id ASC
            LIMIT {13} OFFSET {14}
            """;

        var sql = sqlTemplate.Replace("{SORT_COLUMN}", column).Replace("{SORT_DIRECTION}", direction);

        return await _db.Database
            .SqlQueryRaw<PriceAudienceTableRowRaw>(
                sql, tenantId, stores, curFromDt, curToDt, prevFromDt, prevToDt,
                boundaries.P20, boundaries.P40, boundaries.P60, boundaries.P80, boundaries.P90, boundaries.P97,
                audienceOrdinal, limit, offset)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AllTimeCustomerRowRaw>> GetAllTimeCustomerTableAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds, PriceSegmentBoundariesRow boundaries, int? segmentOrdinal,
        int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);
        var (limit, offset) = NormalizeLimitOffset(page, pageSize);
        var column = AllTimeSortColumn(sortBy);
        var direction = Direction(sortDescending);

        // Params: {0}=tenantId {1}=stores {2}=P20 {3}=P40 {4}=P60 {5}=P80 {6}=P90 {7}=P97
        //         {8}=segmentOrdinal(nullable) {9}=pageSize {10}=offset
        const string sqlTemplate = """
            WITH customer_all AS (
                SELECT t."CustomerId" AS cust_id,
                       (PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY t."TotalAmount"))::numeric AS median_check,
                       COUNT(*)::int AS purchase_count,
                       SUM(t."TotalAmount") AS ltv
                FROM pos_transactions t
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
                GROUP BY t."CustomerId"
            ),
            items_all AS (
                SELECT t."CustomerId" AS cust_id, SUM(ti."Quantity") AS qty_sum
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN items i ON i."Id" = ti."ProductId"
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
                  AND i."ItemType" <> 'packaging'
                GROUP BY t."CustomerId"
            ),
            classified AS (
                SELECT ca.cust_id, ca.median_check, ca.purchase_count, ca.ltv,
                       (COALESCE(ia.qty_sum, 0) / ca.purchase_count::numeric) AS items_per_receipt,
                       CASE WHEN ca.median_check < {2} THEN 1 WHEN ca.median_check < {3} THEN 2
                            WHEN ca.median_check < {4} THEN 3 WHEN ca.median_check < {5} THEN 4
                            WHEN ca.median_check < {6} THEN 5 WHEN ca.median_check < {7} THEN 6
                            ELSE 7 END AS segment_ordinal
                FROM customer_all ca
                LEFT JOIN items_all ia ON ia.cust_id = ca.cust_id
            ),
            filtered AS (
                SELECT cl.*, c."Name" AS name, c."Phone" AS phone
                FROM classified cl
                JOIN customers c ON c."Id" = cl.cust_id
                WHERE {8}::int IS NULL OR cl.segment_ordinal = {8}
            )
            SELECT
                cust_id AS "CustomerId", name AS "Name", phone AS "Phone",
                segment_ordinal AS "SegmentOrdinal", items_per_receipt AS "ItemsPerReceipt",
                median_check AS "TypicalCheck", purchase_count AS "PurchaseCount", ltv AS "Ltv",
                COUNT(*) OVER()::int AS "TotalCount"
            FROM filtered
            ORDER BY {SORT_COLUMN} {SORT_DIRECTION}, cust_id ASC
            LIMIT {9} OFFSET {10}
            """;

        var sql = sqlTemplate.Replace("{SORT_COLUMN}", column).Replace("{SORT_DIRECTION}", direction);

        return await _db.Database
            .SqlQueryRaw<AllTimeCustomerRowRaw>(
                sql, tenantId, stores, boundaries.P20, boundaries.P40, boundaries.P60, boundaries.P80, boundaries.P90, boundaries.P97,
                NullableParam(segmentOrdinal), limit, offset)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FrequencyAudienceTableRowRaw>> GetFrequencyAudienceTableAsync(
        Guid tenantId, IReadOnlyList<Guid>? storeIds,
        DateOnly currentFrom, DateOnly currentTo, DateOnly previousFrom, DateOnly previousTo,
        decimal declineThresholdPercent, PriceSegmentBoundariesRow boundaries, int audienceOrdinal,
        bool useCurrentBasis, decimal? minSpend, decimal? maxSpend, int? priceSegmentOrdinal,
        int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default)
    {
        var stores = Normalize(storeIds);
        var (curFromDt, curToDt) = ToUtcRange(currentFrom, currentTo);
        var (prevFromDt, prevToDt) = ToUtcRange(previousFrom, previousTo);
        var (limit, offset) = NormalizeLimitOffset(page, pageSize);
        var column = FrequencySortColumn(sortBy);
        var direction = Direction(sortDescending);

        // Union population (current ∪ previous buyers — NOT the intersection the price-
        // comparison table uses). Sleeping re-orientation (design doc §4) happens entirely in
        // the WHERE clause below via the useCurrentBasis CASE — the displayed
        // TypicalCheckCurrent/SpendCurrentPeriod columns never change meaning, only which basis
        // the min/max/segment FILTER checks against. Params: {0}=tenantId {1}=stores
        // {2}=curFromDt {3}=curToDt {4}=prevFromDt {5}=prevToDt {6}=declineThresholdPercent
        // {7}=P20 {8}=P40 {9}=P60 {10}=P80 {11}=P90 {12}=P97 {13}=audienceOrdinal {14}=minSpend
        // {15}=maxSpend {16}=priceSegmentOrdinal {17}=useCurrentBasis {18}=pageSize {19}=offset
        const string sqlTemplate = """
            WITH cur AS (
                SELECT t."CustomerId" AS cust_id,
                       COUNT(*)::int AS receipt_count,
                       SUM(t."TotalAmount") AS spend,
                       (PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY t."TotalAmount"))::numeric AS median_check
                FROM pos_transactions t
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {2} AND t."CreatedAt" <= {3}
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
                GROUP BY t."CustomerId"
            ),
            prev AS (
                SELECT t."CustomerId" AS cust_id,
                       COUNT(*)::int AS receipt_count,
                       SUM(t."TotalAmount") AS spend,
                       (PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY t."TotalAmount"))::numeric AS median_check
                FROM pos_transactions t
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {4} AND t."CreatedAt" <= {5}
                  AND (cardinality({1}::uuid[]) = 0 OR t."LocationId" = ANY({1}::uuid[]))
                GROUP BY t."CustomerId"
            ),
            union_pop AS (
                SELECT cust_id FROM cur
                UNION
                SELECT cust_id FROM prev
            ),
            combined AS (
                SELECT up.cust_id,
                       COALESCE(p.receipt_count, 0) AS previous_frequency,
                       COALESCE(c.receipt_count, 0) AS current_frequency,
                       COALESCE(c.spend, 0) AS current_spend,
                       COALESCE(p.spend, 0) AS previous_spend,
                       c.median_check AS current_median_check,
                       p.median_check AS previous_median_check
                FROM union_pop up
                LEFT JOIN cur c ON c.cust_id = up.cust_id
                LEFT JOIN prev p ON p.cust_id = up.cust_id
            ),
            classified AS (
                SELECT
                    cust_id, previous_frequency, current_frequency, current_spend, previous_spend,
                    current_median_check, previous_median_check,
                    (current_frequency - previous_frequency) AS delta_absolute,
                    CASE WHEN previous_frequency > 0
                         THEN ((current_frequency - previous_frequency)::numeric / previous_frequency * 100)
                         ELSE NULL END AS delta_percent,
                    CASE
                        WHEN previous_frequency > 0 AND current_frequency = 0 THEN 1
                        WHEN current_frequency > previous_frequency THEN 3
                        WHEN current_frequency > 0 AND current_frequency < previous_frequency
                             AND ((previous_frequency - current_frequency)::numeric / previous_frequency * 100) >= {6}
                        THEN 2
                        ELSE 4
                    END AS audience_ordinal,
                    CASE
                        WHEN current_median_check IS NULL THEN NULL
                        WHEN current_median_check < {7} THEN 1 WHEN current_median_check < {8} THEN 2
                        WHEN current_median_check < {9} THEN 3 WHEN current_median_check < {10} THEN 4
                        WHEN current_median_check < {11} THEN 5 WHEN current_median_check < {12} THEN 6
                        ELSE 7
                    END AS current_segment,
                    CASE
                        WHEN previous_median_check IS NULL THEN NULL
                        WHEN previous_median_check < {7} THEN 1 WHEN previous_median_check < {8} THEN 2
                        WHEN previous_median_check < {9} THEN 3 WHEN previous_median_check < {10} THEN 4
                        WHEN previous_median_check < {11} THEN 5 WHEN previous_median_check < {12} THEN 6
                        ELSE 7
                    END AS previous_segment
                FROM combined
            ),
            union_total AS (
                SELECT COUNT(*)::int AS n FROM classified
            ),
            ltv_map AS (
                SELECT "CustomerId" AS cust_id, SUM("TotalAmount") AS ltv
                FROM pos_transactions
                WHERE "TenantId" = {0} AND "CustomerId" IS NOT NULL AND "Status" <> 'fiscalization_failed'
                  AND (cardinality({1}::uuid[]) = 0 OR "LocationId" = ANY({1}::uuid[]))
                GROUP BY "CustomerId"
            ),
            filtered AS (
                SELECT cl.*, c."Name" AS name, c."Phone" AS phone, COALESCE(lm.ltv, 0) AS ltv, ut.n AS union_total
                FROM classified cl
                JOIN customers c ON c."Id" = cl.cust_id
                LEFT JOIN ltv_map lm ON lm.cust_id = cl.cust_id
                CROSS JOIN union_total ut
                WHERE cl.audience_ordinal = {13}
                  AND ({14}::numeric IS NULL OR (CASE WHEN {17} THEN cl.current_spend ELSE cl.previous_spend END) >= {14})
                  AND ({15}::numeric IS NULL OR (CASE WHEN {17} THEN cl.current_spend ELSE cl.previous_spend END) <= {15})
                  AND ({16}::int IS NULL OR (CASE WHEN {17} THEN cl.current_segment ELSE cl.previous_segment END) = {16})
            )
            SELECT
                cust_id AS "CustomerId", name AS "Name", phone AS "Phone",
                previous_frequency AS "PreviousFrequency", current_frequency AS "CurrentFrequency",
                delta_absolute AS "FrequencyDeltaAbsolute", delta_percent AS "FrequencyDeltaPercent",
                current_median_check AS "TypicalCheckCurrent", current_spend AS "SpendCurrentPeriod", ltv AS "Ltv",
                COUNT(*) OVER()::int AS "TotalCount",
                COUNT(*) FILTER (WHERE phone IS NOT NULL) OVER()::int AS "WithPhoneCount",
                MAX(union_total) OVER()::int AS "UnionPopulationCount",
                COALESCE(AVG(ltv) OVER(), 0) AS "AverageLtvOverall",
                COALESCE(AVG(previous_frequency) OVER(), 0) AS "AveragePreviousFrequencyOverall",
                COALESCE(AVG(current_frequency) OVER(), 0) AS "AverageCurrentFrequencyOverall"
            FROM filtered
            ORDER BY {SORT_COLUMN} {SORT_DIRECTION}, cust_id ASC
            LIMIT {18} OFFSET {19}
            """;

        var sql = sqlTemplate.Replace("{SORT_COLUMN}", column).Replace("{SORT_DIRECTION}", direction);

        return await _db.Database
            .SqlQueryRaw<FrequencyAudienceTableRowRaw>(
                sql, tenantId, stores, curFromDt, curToDt, prevFromDt, prevToDt,
                declineThresholdPercent, boundaries.P20, boundaries.P40, boundaries.P60, boundaries.P80, boundaries.P90, boundaries.P97,
                audienceOrdinal, NullableParam(minSpend), NullableParam(maxSpend), NullableParam(priceSegmentOrdinal),
                useCurrentBasis, limit, offset)
            .ToListAsync(ct);
    }

    // ── Settings (mirrors LoyaltyRepository's GetSettingsAsync/AddSettingsAsync/UpdateSettings/
    // SaveChangesAsync shape exactly) ────────────────────────────────────────────────────────

    public Task<PriceSegmentSettings?> GetSettingsAsync(Guid tenantId, CancellationToken ct = default) =>
        _db.PriceSegmentSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public Task AddSettingsAsync(PriceSegmentSettings settings, CancellationToken ct = default) =>
        _db.PriceSegmentSettings.AddAsync(settings, ct).AsTask();

    public void UpdateSettings(PriceSegmentSettings settings) => _db.PriceSegmentSettings.Update(settings);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>Null/empty both mean "no restriction" — normalized to one array shape so every
    /// SQL string only needs the single `cardinality(...) = 0 OR ...` guard (same convention
    /// MarketingAnalyticsRepository already established for store filters; reused here for both
    /// store AND customer-id filters).</summary>
    private static Guid[] Normalize(IReadOnlyList<Guid>? ids) =>
        ids is null or { Count: 0 } ? [] : ids.Distinct().ToArray();

    // Npgsql rejects DateTime with Kind=Unspecified as a parameter for timestamptz columns —
    // mirrors AnalyticsRepository/MarketingAnalyticsRepository's own ToUtcRange exactly.
    private static (DateTime FromDt, DateTime ToDt) ToUtcRange(DateOnly from, DateOnly to) => (
        from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));

    private static (int Limit, int Offset) NormalizeLimitOffset(int page, int pageSize)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Max(1, pageSize);
        return (safePageSize, (safePage - 1) * safePageSize);
    }

    private static string Direction(bool descending) => descending ? "DESC" : "ASC";

    /// <summary>
    /// Boxes a nullable value-type SQL parameter (e.g. an optional segment/spend filter) as
    /// either the boxed underlying value or the ADO.NET <see cref="DBNull.Value"/> sentinel —
    /// never a bare C# <c>null</c> reference. Two reasons: (1) <c>SqlQueryRaw</c>'s
    /// <c>params object[] parameters</c> is a non-nullable-annotated array, so a bare boxed
    /// <c>Nullable&lt;T&gt;</c> with no value (which boxes to an actual null reference — standard
    /// CLR behavior) trips a real nullable-warning; (2) more importantly, <c>DBNull.Value</c> is
    /// the traditional, unambiguous ADO.NET way to represent a SQL NULL parameter value — this
    /// codebase's first genuinely-nullable raw-SQL parameter (every prior raw-SQL repository only
    /// ever passed non-null arrays/scalars), so being explicit here avoids relying on whatever a
    /// bare null would infer through Npgsql's own type-inference path. The SQL side always reads
    /// these back through an explicit cast (e.g. <c>{8}::int IS NULL OR ...</c>), which resolves
    /// the parameter's type from the cast regardless of which of the two forms sent it.
    /// </summary>
    private static object NullableParam<T>(T? value) where T : struct => (object?)value ?? DBNull.Value;

    /// <summary>Maps an already-allowlisted (via PriceSegmentSortKeys) key to a literal SQL
    /// column reference from the comparison-table query's CTEs — never receives the raw
    /// caller-supplied string directly.</summary>
    private static string PriceAudienceSortColumn(string? sortBy) => PriceSegmentSortKeys.NormalizePriceAudience(sortBy) switch
    {
        "name" => "name",
        "segment" => "current_seg",
        "items" => "current_ipr",
        "ltv" => "ltv",
        _ => "current_median", // "check" — confirmed competitor default (analysis doc §9.2)
    };

    private static string AllTimeSortColumn(string? sortBy) => PriceSegmentSortKeys.NormalizeAllTime(sortBy) switch
    {
        "name" => "name",
        "segment" => "segment_ordinal",
        "items" => "items_per_receipt",
        "purchases" => "purchase_count",
        "ltv" => "ltv",
        _ => "median_check", // "check"
    };

    private static string FrequencySortColumn(string? sortBy) => PriceSegmentSortKeys.NormalizeFrequency(sortBy) switch
    {
        "name" => "name",
        "previous" => "previous_frequency",
        "current" => "current_frequency",
        "check" => "current_median_check",
        "spend" => "current_spend",
        "ltv" => "ltv",
        _ => "delta_absolute", // "delta"
    };
}
