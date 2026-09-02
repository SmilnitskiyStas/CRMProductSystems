using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// See <see cref="IAudienceBuilderRepository"/> for the full design context, the mandatory
/// TASK-428 read-before-editing note on the <c>ILIKE</c> predicates, and the parameterization
/// discipline every method here follows.
///
/// Every "own product" query (Overview/Buyers/BuyerReceipts/MatchedItems) shares the same term-
/// resolution shape: <c>term_matches</c> (UNNEST-zipped text/category term arrays joined against
/// <c>items</c>) → <c>selected</c> (deduplicated, minus <see
/// cref="ResolvedAudienceQuery.ExcludedItemIds"/>) → <c>selected_items</c> (item ids only, term
/// info dropped) → <c>customer_lines</c> (one row per matching pos_transaction_items line, joined
/// against <c>selected_items</c> so a single physical purchase is never counted twice even when
/// its item matches more than one term) → <c>customer_totals</c> (SUM/COUNT per customer) +
/// <c>customer_term_coverage</c> (which term_indexes each customer actually covered, derived from
/// <c>customer_lines</c> re-joined to the small in-memory <c>selected</c> set — NOT a second scan
/// of pos_transaction_items) → <c>qualified</c> (AND/OR + threshold filter). This two-CTE split
/// (totals vs coverage) is a deliberate correction of the design doc's own single-CTE SQL sketch:
/// joining <c>pos_transaction_items</c> directly against <c>selected</c> (which legitimately has
/// MULTIPLE rows for the same item_id when that item matches more than one term/category — e.g. a
/// text term AND a category term both matching "Coca-Cola") would double-count that purchase's
/// quantity/amount once per matching term_index while still correctly satisfying AND-mode
/// coverage. Splitting the aggregation this way keeps AND-mode's "covered every term_index"
/// semantics exactly as designed while keeping total_qty/total_amount correctly deduplicated
/// (analysis doc §22.4/§22.5: "SUM(quantity/amount для SELECTED_SKUS клієнта" — a deduplicated
/// item set, not one row per term match).
///
/// Line amount is computed as <c>ti."PriceFinal" * ti."Quantity"</c> throughout (a second
/// correction of the design doc's sketch, which used <c>ti."PriceFinal"</c> alone as "amount" —
/// PriceFinal is a PER-UNIT price, confirmed by <c>PriceSegmentsRepository.GetAverageUnitPriceAsync</c>
/// and <c>AnalyticsRepository</c>'s own <c>TotalRevenue: g.Sum(i => i.PriceFinal * i.Quantity)</c>;
/// using PriceFinal bare would under-report spend for any line with Quantity != 1).
///
/// Every date-range comparison against <c>t."CreatedAt"</c> (a <c>timestamptz</c> column) casts
/// its parameter explicitly as <c>{n}::timestamptz</c> — even though the C# side already converts
/// DateOnly to a DateTime(Kind=Utc) before binding — per the TASK-428 finding: an implicit
/// cross-type <c>timestamptz</c>-vs-<c>date</c> comparison would invoke non-LEAKPROOF
/// <c>timestamptz_*_date</c> functions and silently re-trigger the same RLS/index-blocking class
/// of issue found for <c>ILIKE</c>. The explicit cast removes any ambiguity regardless of Npgsql's
/// own parameter-type inference.
/// </summary>
public sealed class AudienceBuilderRepository : IAudienceBuilderRepository
{
    private readonly AppDbContext _db;

    public AudienceBuilderRepository(AppDbContext db) => _db = db;

    // ── Categories typeahead ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AudienceCategoryOptionRow>> SearchCategoriesAsync(
        Guid tenantId, string? search, int limit, CancellationToken ct = default)
    {
        var term = search?.Trim() ?? string.Empty;

        // B1: platform_categories is a global, provider-curated table (no RLS, no TenantId) —
        // every tenant sees the same list. The ILIKE has no leakproof concern here (no RLS on the
        // table); the per-tenant item count keeps an explicit i."TenantId" = {0} filter so it is
        // correct regardless of the caller's RLS session. Params: {0}=tenantId {1}=search {2}=limit
        const string sql = """
            SELECT c."Id" AS "CategoryId", c."Name" AS "Name",
                   (SELECT COUNT(*)::int FROM items i WHERE i."CategoryId" = c."Id" AND i."TenantId" = {0}) AS "ItemCount"
            FROM platform_categories c
            WHERE c."IsActive" = true
              AND ({1} = '' OR c."Name" ILIKE '%' || {1} || '%')
            ORDER BY c."Name"
            LIMIT {2}
            """;

        return await _db.Database.SqlQueryRaw<AudienceCategoryOptionRow>(sql, tenantId, term, limit).ToListAsync(ct);
    }

    // ── Own-product audience ─────────────────────────────────────────────────────────────────

    public async Task<AudienceOverviewRow> GetOverviewAsync(ResolvedAudienceQuery q, CancellationToken ct = default)
    {
        // Params: {0}=tenantId {1}=textIdx {2}=textVals {3}=catIdx {4}=catIds {5}=excludedItemIds
        //         {6}=fromDt {7}=toDt {8}=storeIds {9}=matchAll {10}=termCount {11}=minQty {12}=minAmount
        const string sql = """
            WITH term_matches AS (
                SELECT tt.term_index, i."Id" AS item_id
                FROM UNNEST({1}::int[], {2}::text[]) AS tt(term_index, value)
                JOIN items i ON i."TenantId" = {0} AND (
                     i."Id"::text = tt.value
                  OR i."Barcodes" @> to_jsonb(ARRAY[tt.value]::text[])
                  OR i."Name" ILIKE '%' || tt.value || '%')
                UNION ALL
                SELECT ct.term_index, i."Id"
                FROM UNNEST({3}::int[], {4}::uuid[]) AS ct(term_index, category_id)
                JOIN items i ON i."TenantId" = {0} AND i."CategoryId" = ct.category_id
            ),
            selected AS (
                SELECT DISTINCT term_index, item_id FROM term_matches
                WHERE item_id <> ALL({5}::uuid[])
            ),
            selected_items AS (
                SELECT DISTINCT item_id FROM selected
            ),
            customer_lines AS (
                SELECT t."CustomerId" AS cust_id, t."Id" AS txn_id, ti."ProductId" AS item_id,
                       ti."Quantity" AS qty, (ti."PriceFinal" * ti."Quantity") AS amount
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN selected_items si ON si.item_id = ti."ProductId"
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {6}::timestamptz AND t."CreatedAt" <= {7}::timestamptz
                  AND (cardinality({8}::uuid[]) = 0 OR t."LocationId" = ANY({8}::uuid[]))
            ),
            customer_totals AS (
                SELECT cust_id, SUM(qty) AS total_qty, SUM(amount) AS total_amount
                FROM customer_lines GROUP BY cust_id
            ),
            customer_term_coverage AS (
                SELECT cl.cust_id, COUNT(DISTINCT s.term_index) AS covered_terms
                FROM customer_lines cl
                JOIN selected s ON s.item_id = cl.item_id
                GROUP BY cl.cust_id
            ),
            qualified AS (
                SELECT ct.cust_id, ct.total_qty, ct.total_amount
                FROM customer_totals ct
                JOIN customer_term_coverage cc ON cc.cust_id = ct.cust_id
                WHERE ({9} = FALSE OR cc.covered_terms = {10})
                  AND ({11}::numeric IS NULL OR ct.total_qty >= {11})
                  AND ({12}::numeric IS NULL OR ct.total_amount >= {12})
            )
            SELECT
                (SELECT COUNT(*)::int FROM qualified) AS "ParticipantsCount",
                (SELECT COUNT(*)::int FROM selected_items) AS "ItemsInSelectionCount",
                COALESCE((SELECT SUM(total_qty) FROM qualified), 0) AS "UnitsPurchased",
                COALESCE((SELECT SUM(total_amount) FROM qualified), 0) AS "TotalSpend"
            """;

        var rows = await _db.Database.SqlQueryRaw<AudienceOverviewRow>(
            sql, q.TenantId, q.TextTermIndexes, q.TextTermValues, q.CategoryTermIndexes, q.CategoryTermIds,
            q.ExcludedItemIds, q.FromDt, q.ToDt, q.StoreIds, q.MatchAll, q.TermCount,
            NullableParam(q.MinQuantity), NullableParam(q.MinAmount)).ToListAsync(ct);
        return rows[0];
    }

    public async Task<IReadOnlyList<AudienceBuyerRowRaw>> GetBuyersAsync(
        ResolvedAudienceQuery q, int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default)
    {
        var (limit, offset) = NormalizeLimitOffset(page, pageSize);
        var column = BuyerSortColumn(sortBy);
        var direction = Direction(sortDescending);

        // Same CTE chain as GetOverviewAsync, +receipt_count, +customer join, +paging.
        // Params: {0}=tenantId {1}=textIdx {2}=textVals {3}=catIdx {4}=catIds {5}=excludedItemIds
        //         {6}=fromDt {7}=toDt {8}=storeIds {9}=matchAll {10}=termCount {11}=minQty
        //         {12}=minAmount {13}=limit {14}=offset
        var sqlTemplate = """
            WITH term_matches AS (
                SELECT tt.term_index, i."Id" AS item_id
                FROM UNNEST({1}::int[], {2}::text[]) AS tt(term_index, value)
                JOIN items i ON i."TenantId" = {0} AND (
                     i."Id"::text = tt.value
                  OR i."Barcodes" @> to_jsonb(ARRAY[tt.value]::text[])
                  OR i."Name" ILIKE '%' || tt.value || '%')
                UNION ALL
                SELECT ct.term_index, i."Id"
                FROM UNNEST({3}::int[], {4}::uuid[]) AS ct(term_index, category_id)
                JOIN items i ON i."TenantId" = {0} AND i."CategoryId" = ct.category_id
            ),
            selected AS (
                SELECT DISTINCT term_index, item_id FROM term_matches
                WHERE item_id <> ALL({5}::uuid[])
            ),
            selected_items AS (
                SELECT DISTINCT item_id FROM selected
            ),
            customer_lines AS (
                SELECT t."CustomerId" AS cust_id, t."Id" AS txn_id, ti."ProductId" AS item_id,
                       ti."Quantity" AS qty, (ti."PriceFinal" * ti."Quantity") AS amount
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN selected_items si ON si.item_id = ti."ProductId"
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {6}::timestamptz AND t."CreatedAt" <= {7}::timestamptz
                  AND (cardinality({8}::uuid[]) = 0 OR t."LocationId" = ANY({8}::uuid[]))
            ),
            customer_totals AS (
                SELECT cust_id, SUM(qty) AS total_qty, SUM(amount) AS total_amount, COUNT(DISTINCT txn_id) AS receipt_count
                FROM customer_lines GROUP BY cust_id
            ),
            customer_term_coverage AS (
                SELECT cl.cust_id, COUNT(DISTINCT s.term_index) AS covered_terms
                FROM customer_lines cl
                JOIN selected s ON s.item_id = cl.item_id
                GROUP BY cl.cust_id
            ),
            qualified AS (
                SELECT ct.cust_id, ct.total_qty, ct.total_amount, ct.receipt_count
                FROM customer_totals ct
                JOIN customer_term_coverage cc ON cc.cust_id = ct.cust_id
                WHERE ({9} = FALSE OR cc.covered_terms = {10})
                  AND ({11}::numeric IS NULL OR ct.total_qty >= {11})
                  AND ({12}::numeric IS NULL OR ct.total_amount >= {12})
            ),
            joined AS (
                SELECT q.cust_id, c."Name" AS name, c."Phone" AS phone, q.total_qty, q.total_amount, q.receipt_count
                FROM qualified q
                JOIN customers c ON c."Id" = q.cust_id
            )
            SELECT
                cust_id AS "CustomerId", name AS "Name", phone AS "Phone",
                total_qty AS "QuantityPurchased", receipt_count AS "ReceiptCount", total_amount AS "TotalAmount",
                COUNT(*) OVER()::int AS "TotalCount",
                COUNT(*) FILTER (WHERE phone IS NOT NULL) OVER()::int AS "WithPhoneCount"
            FROM joined
            ORDER BY {SORT_COLUMN} {SORT_DIRECTION}, cust_id ASC
            LIMIT {13} OFFSET {14}
            """;

        var sql = sqlTemplate.Replace("{SORT_COLUMN}", column).Replace("{SORT_DIRECTION}", direction);

        return await _db.Database
            .SqlQueryRaw<AudienceBuyerRowRaw>(
                sql, q.TenantId, q.TextTermIndexes, q.TextTermValues, q.CategoryTermIndexes, q.CategoryTermIds,
                q.ExcludedItemIds, q.FromDt, q.ToDt, q.StoreIds, q.MatchAll, q.TermCount,
                NullableParam(q.MinQuantity), NullableParam(q.MinAmount), limit, offset)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AudienceReceiptExportRowRaw>> GetBuyerReceiptsAsync(
        ResolvedAudienceQuery q, int maxRows, CancellationToken ct = default)
    {
        // Same qualification chain, grouped by (customer, receipt) instead of just customer —
        // Quantity/Amount per row are the sum of ONLY the matched/selected SKUs on THAT receipt.
        // Params: {0}=tenantId {1}=textIdx {2}=textVals {3}=catIdx {4}=catIds {5}=excludedItemIds
        //         {6}=fromDt {7}=toDt {8}=storeIds {9}=matchAll {10}=termCount {11}=minQty
        //         {12}=minAmount {13}=maxRows
        const string sql = """
            WITH term_matches AS (
                SELECT tt.term_index, i."Id" AS item_id
                FROM UNNEST({1}::int[], {2}::text[]) AS tt(term_index, value)
                JOIN items i ON i."TenantId" = {0} AND (
                     i."Id"::text = tt.value
                  OR i."Barcodes" @> to_jsonb(ARRAY[tt.value]::text[])
                  OR i."Name" ILIKE '%' || tt.value || '%')
                UNION ALL
                SELECT ct.term_index, i."Id"
                FROM UNNEST({3}::int[], {4}::uuid[]) AS ct(term_index, category_id)
                JOIN items i ON i."TenantId" = {0} AND i."CategoryId" = ct.category_id
            ),
            selected AS (
                SELECT DISTINCT term_index, item_id FROM term_matches
                WHERE item_id <> ALL({5}::uuid[])
            ),
            selected_items AS (
                SELECT DISTINCT item_id FROM selected
            ),
            customer_lines AS (
                SELECT t."CustomerId" AS cust_id, t."Id" AS txn_id, ti."ProductId" AS item_id,
                       ti."Quantity" AS qty, (ti."PriceFinal" * ti."Quantity") AS amount
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN selected_items si ON si.item_id = ti."ProductId"
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {6}::timestamptz AND t."CreatedAt" <= {7}::timestamptz
                  AND (cardinality({8}::uuid[]) = 0 OR t."LocationId" = ANY({8}::uuid[]))
            ),
            customer_totals AS (
                SELECT cust_id, SUM(qty) AS total_qty, SUM(amount) AS total_amount
                FROM customer_lines GROUP BY cust_id
            ),
            customer_term_coverage AS (
                SELECT cl.cust_id, COUNT(DISTINCT s.term_index) AS covered_terms
                FROM customer_lines cl
                JOIN selected s ON s.item_id = cl.item_id
                GROUP BY cl.cust_id
            ),
            qualified AS (
                SELECT ct.cust_id
                FROM customer_totals ct
                JOIN customer_term_coverage cc ON cc.cust_id = ct.cust_id
                WHERE ({9} = FALSE OR cc.covered_terms = {10})
                  AND ({11}::numeric IS NULL OR ct.total_qty >= {11})
                  AND ({12}::numeric IS NULL OR ct.total_amount >= {12})
            ),
            receipt_lines AS (
                SELECT cl.cust_id, cl.txn_id, SUM(cl.qty) AS qty, SUM(cl.amount) AS amount
                FROM customer_lines cl
                WHERE cl.cust_id IN (SELECT cust_id FROM qualified)
                GROUP BY cl.cust_id, cl.txn_id
            )
            SELECT
                rl.cust_id AS "CustomerId", c."Name" AS "Name", c."Phone" AS "Phone",
                t."ReceiptNumber" AS "ReceiptNumber", t."CreatedAt" AS "CreatedAt", loc."Name" AS "StoreName",
                rl.qty AS "Quantity", rl.amount AS "Amount"
            FROM receipt_lines rl
            JOIN pos_transactions t ON t."Id" = rl.txn_id
            JOIN customers c ON c."Id" = rl.cust_id
            LEFT JOIN locations loc ON loc."Id" = t."LocationId"
            ORDER BY c."Name", t."CreatedAt"
            LIMIT {13}
            """;

        return await _db.Database
            .SqlQueryRaw<AudienceReceiptExportRowRaw>(
                sql, q.TenantId, q.TextTermIndexes, q.TextTermValues, q.CategoryTermIndexes, q.CategoryTermIds,
                q.ExcludedItemIds, q.FromDt, q.ToDt, q.StoreIds, q.MatchAll, q.TermCount,
                NullableParam(q.MinQuantity), NullableParam(q.MinAmount), maxRows)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MatchedItemRowRaw>> GetMatchedItemsAsync(
        ResolvedAudienceQuery q, int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default)
    {
        var (limit, offset) = NormalizeLimitOffset(page, pageSize);
        var column = MatchedItemSortColumn(sortBy);
        var direction = Direction(sortDescending);

        // Deliberately independent of MatchAll/TermCount/MinQuantity/MinAmount (design doc §7:
        // this list is "what SKUs matched", not "which customers qualify") and NOT filtered by
        // ExcludedItemIds (IsExcluded is a per-row flag so a previously-excluded SKU stays visible
        // for re-inclusion). Zero-sales SKUs are kept via LEFT JOIN, never dropped (analysis doc
        // §20.4). Params: {0}=tenantId {1}=textIdx {2}=textVals {3}=catIdx {4}=catIds
        //         {5}=fromDt {6}=toDt {7}=storeIds {8}=excludedItemIds {9}=limit {10}=offset
        var sqlTemplate = """
            WITH term_matches AS (
                SELECT tt.term_index, i."Id" AS item_id
                FROM UNNEST({1}::int[], {2}::text[]) AS tt(term_index, value)
                JOIN items i ON i."TenantId" = {0} AND (
                     i."Id"::text = tt.value
                  OR i."Barcodes" @> to_jsonb(ARRAY[tt.value]::text[])
                  OR i."Name" ILIKE '%' || tt.value || '%')
                UNION ALL
                SELECT ct.term_index, i."Id"
                FROM UNNEST({3}::int[], {4}::uuid[]) AS ct(term_index, category_id)
                JOIN items i ON i."TenantId" = {0} AND i."CategoryId" = ct.category_id
            ),
            matched_items AS (
                SELECT DISTINCT item_id FROM term_matches
            ),
            sales AS (
                SELECT ti."ProductId" AS item_id,
                       SUM(ti."Quantity") AS qty_sold,
                       COUNT(DISTINCT t."Id") AS receipt_count,
                       COUNT(DISTINCT t."CustomerId") AS buyer_count
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                WHERE t."TenantId" = {0}
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {5}::timestamptz AND t."CreatedAt" <= {6}::timestamptz
                  AND (cardinality({7}::uuid[]) = 0 OR t."LocationId" = ANY({7}::uuid[]))
                  AND ti."ProductId" IN (SELECT item_id FROM matched_items)
                GROUP BY ti."ProductId"
            ),
            joined AS (
                SELECT mi.item_id, i."Name" AS name,
                       (SELECT NULLIF(string_agg(bc, '; '), '') FROM jsonb_array_elements_text(i."Barcodes") bc) AS barcodes_joined,
                       COALESCE(s.qty_sold, 0) AS qty_sold,
                       COALESCE(s.receipt_count, 0) AS receipt_count,
                       COALESCE(s.buyer_count, 0) AS buyer_count,
                       (mi.item_id = ANY({8}::uuid[])) AS is_excluded
                FROM matched_items mi
                JOIN items i ON i."Id" = mi.item_id
                LEFT JOIN sales s ON s.item_id = mi.item_id
            )
            SELECT
                item_id AS "ItemId", name AS "Name", barcodes_joined AS "BarcodesJoined", is_excluded AS "IsExcluded",
                qty_sold AS "QuantitySold", receipt_count AS "ReceiptCount", buyer_count AS "BuyerCount",
                COUNT(*) OVER()::int AS "TotalCount"
            FROM joined
            ORDER BY {SORT_COLUMN} {SORT_DIRECTION}, item_id ASC
            LIMIT {9} OFFSET {10}
            """;

        var sql = sqlTemplate.Replace("{SORT_COLUMN}", column).Replace("{SORT_DIRECTION}", direction);

        return await _db.Database
            .SqlQueryRaw<MatchedItemRowRaw>(
                sql, q.TenantId, q.TextTermIndexes, q.TextTermValues, q.CategoryTermIndexes, q.CategoryTermIds,
                q.FromDt, q.ToDt, q.StoreIds, q.ExcludedItemIds, limit, offset)
            .ToListAsync(ct);
    }

    // ── Competitor audience ──────────────────────────────────────────────────────────────────

    public async Task<CompetitorOverviewRow> GetCompetitorOverviewAsync(ResolvedCompetitorQuery q, CancellationToken ct = default)
    {
        // Params: {0}=tenantId {1}=compTextIdx {2}=compTextVals {3}=compCatIdx {4}=compCatIds
        //         {5}=ownTextIdx {6}=ownTextVals {7}=ownCatIdx {8}=ownCatIds {9}=ownExcludedItemIds
        //         {10}=fromDt {11}=toDt {12}=storeIds {13}=allTimeHorizon
        const string sql = """
            WITH competitor_matched AS (
                SELECT DISTINCT i."Id" AS item_id
                FROM UNNEST({1}::int[], {2}::text[]) AS tt(term_index, value)
                JOIN items i ON i."TenantId" = {0} AND (
                     i."Id"::text = tt.value
                  OR i."Barcodes" @> to_jsonb(ARRAY[tt.value]::text[])
                  OR i."Name" ILIKE '%' || tt.value || '%')
                UNION
                SELECT DISTINCT i."Id"
                FROM UNNEST({3}::int[], {4}::uuid[]) AS ct(term_index, category_id)
                JOIN items i ON i."TenantId" = {0} AND i."CategoryId" = ct.category_id
            ),
            own_matched_raw AS (
                SELECT DISTINCT i."Id" AS item_id
                FROM UNNEST({5}::int[], {6}::text[]) AS tt(term_index, value)
                JOIN items i ON i."TenantId" = {0} AND (
                     i."Id"::text = tt.value
                  OR i."Barcodes" @> to_jsonb(ARRAY[tt.value]::text[])
                  OR i."Name" ILIKE '%' || tt.value || '%')
                UNION
                SELECT DISTINCT i."Id"
                FROM UNNEST({7}::int[], {8}::uuid[]) AS ct(term_index, category_id)
                JOIN items i ON i."TenantId" = {0} AND i."CategoryId" = ct.category_id
            ),
            own_matched AS (
                SELECT item_id FROM own_matched_raw WHERE item_id <> ALL({9}::uuid[])
            ),
            competitor_period_lines AS (
                SELECT t."CustomerId" AS cust_id, t."Id" AS txn_id,
                       ti."Quantity" AS qty, (ti."PriceFinal" * ti."Quantity") AS amount
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN competitor_matched cm ON cm.item_id = ti."ProductId"
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {10}::timestamptz AND t."CreatedAt" <= {11}::timestamptz
                  AND (cardinality({12}::uuid[]) = 0 OR t."LocationId" = ANY({12}::uuid[]))
            ),
            competitor_period_buyers AS (
                SELECT DISTINCT cust_id FROM competitor_period_lines
            ),
            own_buyers_to_exclude AS (
                SELECT DISTINCT t."CustomerId" AS cust_id
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN own_matched om ON om.item_id = ti."ProductId"
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" IN (SELECT cust_id FROM competitor_period_buyers)
                  AND t."Status" <> 'fiscalization_failed'
                  AND ({13} OR (t."CreatedAt" >= {10}::timestamptz AND t."CreatedAt" <= {11}::timestamptz))
                  AND (cardinality({12}::uuid[]) = 0 OR t."LocationId" = ANY({12}::uuid[]))
            ),
            final_audience AS (
                SELECT cust_id FROM competitor_period_buyers
                WHERE cust_id NOT IN (SELECT cust_id FROM own_buyers_to_exclude)
            ),
            agg AS (
                SELECT fa.cust_id, SUM(cpl.qty) AS total_qty, SUM(cpl.amount) AS total_amount
                FROM final_audience fa
                JOIN competitor_period_lines cpl ON cpl.cust_id = fa.cust_id
                GROUP BY fa.cust_id
            )
            SELECT
                (SELECT COUNT(*)::int FROM final_audience) AS "NewAudienceCount",
                (SELECT COUNT(*)::int FROM competitor_matched) AS "CompetitorItemsCount",
                COALESCE((SELECT SUM(total_qty) FROM agg), 0) AS "UnitsPurchased",
                COALESCE((SELECT SUM(total_amount) FROM agg), 0) AS "TotalSpend"
            """;

        var rows = await _db.Database.SqlQueryRaw<CompetitorOverviewRow>(
            sql, q.TenantId, q.CompetitorTextTermIndexes, q.CompetitorTextTermValues, q.CompetitorCategoryTermIndexes, q.CompetitorCategoryTermIds,
            q.OwnTextTermIndexes, q.OwnTextTermValues, q.OwnCategoryTermIndexes, q.OwnCategoryTermIds,
            q.OwnExcludedItemIds, q.FromDt, q.ToDt, q.StoreIds, q.AllTimeHorizon).ToListAsync(ct);
        return rows[0];
    }

    public async Task<IReadOnlyList<CompetitorBuyerRowRaw>> GetCompetitorBuyersAsync(
        ResolvedCompetitorQuery q, int page, int pageSize, string? sortBy, bool sortDescending, CancellationToken ct = default)
    {
        var (limit, offset) = NormalizeLimitOffset(page, pageSize);
        var column = CompetitorBuyerSortColumn(sortBy);
        var direction = Direction(sortDescending);

        // Same CTE chain as GetCompetitorOverviewAsync, +receipt_count, +customer join, +paging.
        // Params: {0}=tenantId {1}=compTextIdx {2}=compTextVals {3}=compCatIdx {4}=compCatIds
        //         {5}=ownTextIdx {6}=ownTextVals {7}=ownCatIdx {8}=ownCatIds {9}=ownExcludedItemIds
        //         {10}=fromDt {11}=toDt {12}=storeIds {13}=allTimeHorizon {14}=limit {15}=offset
        var sqlTemplate = """
            WITH competitor_matched AS (
                SELECT DISTINCT i."Id" AS item_id
                FROM UNNEST({1}::int[], {2}::text[]) AS tt(term_index, value)
                JOIN items i ON i."TenantId" = {0} AND (
                     i."Id"::text = tt.value
                  OR i."Barcodes" @> to_jsonb(ARRAY[tt.value]::text[])
                  OR i."Name" ILIKE '%' || tt.value || '%')
                UNION
                SELECT DISTINCT i."Id"
                FROM UNNEST({3}::int[], {4}::uuid[]) AS ct(term_index, category_id)
                JOIN items i ON i."TenantId" = {0} AND i."CategoryId" = ct.category_id
            ),
            own_matched_raw AS (
                SELECT DISTINCT i."Id" AS item_id
                FROM UNNEST({5}::int[], {6}::text[]) AS tt(term_index, value)
                JOIN items i ON i."TenantId" = {0} AND (
                     i."Id"::text = tt.value
                  OR i."Barcodes" @> to_jsonb(ARRAY[tt.value]::text[])
                  OR i."Name" ILIKE '%' || tt.value || '%')
                UNION
                SELECT DISTINCT i."Id"
                FROM UNNEST({7}::int[], {8}::uuid[]) AS ct(term_index, category_id)
                JOIN items i ON i."TenantId" = {0} AND i."CategoryId" = ct.category_id
            ),
            own_matched AS (
                SELECT item_id FROM own_matched_raw WHERE item_id <> ALL({9}::uuid[])
            ),
            competitor_period_lines AS (
                SELECT t."CustomerId" AS cust_id, t."Id" AS txn_id,
                       ti."Quantity" AS qty, (ti."PriceFinal" * ti."Quantity") AS amount
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN competitor_matched cm ON cm.item_id = ti."ProductId"
                WHERE t."TenantId" = {0} AND t."CustomerId" IS NOT NULL
                  AND t."Status" <> 'fiscalization_failed'
                  AND t."CreatedAt" >= {10}::timestamptz AND t."CreatedAt" <= {11}::timestamptz
                  AND (cardinality({12}::uuid[]) = 0 OR t."LocationId" = ANY({12}::uuid[]))
            ),
            competitor_period_buyers AS (
                SELECT DISTINCT cust_id FROM competitor_period_lines
            ),
            own_buyers_to_exclude AS (
                SELECT DISTINCT t."CustomerId" AS cust_id
                FROM pos_transaction_items ti
                JOIN pos_transactions t ON t."Id" = ti."TransactionId"
                JOIN own_matched om ON om.item_id = ti."ProductId"
                WHERE t."TenantId" = {0}
                  AND t."CustomerId" IN (SELECT cust_id FROM competitor_period_buyers)
                  AND t."Status" <> 'fiscalization_failed'
                  AND ({13} OR (t."CreatedAt" >= {10}::timestamptz AND t."CreatedAt" <= {11}::timestamptz))
                  AND (cardinality({12}::uuid[]) = 0 OR t."LocationId" = ANY({12}::uuid[]))
            ),
            final_audience AS (
                SELECT cust_id FROM competitor_period_buyers
                WHERE cust_id NOT IN (SELECT cust_id FROM own_buyers_to_exclude)
            ),
            agg AS (
                SELECT fa.cust_id, SUM(cpl.qty) AS total_qty, SUM(cpl.amount) AS total_amount,
                       COUNT(DISTINCT cpl.txn_id) AS receipt_count
                FROM final_audience fa
                JOIN competitor_period_lines cpl ON cpl.cust_id = fa.cust_id
                GROUP BY fa.cust_id
            ),
            joined AS (
                SELECT a.cust_id, c."Name" AS name, c."Phone" AS phone, a.total_qty, a.total_amount, a.receipt_count
                FROM agg a
                JOIN customers c ON c."Id" = a.cust_id
            )
            SELECT
                cust_id AS "CustomerId", name AS "Name", phone AS "Phone",
                total_qty AS "QuantityPurchased", receipt_count AS "ReceiptCount", total_amount AS "TotalAmount",
                COUNT(*) OVER()::int AS "TotalCount",
                COUNT(*) FILTER (WHERE phone IS NOT NULL) OVER()::int AS "WithPhoneCount"
            FROM joined
            ORDER BY {SORT_COLUMN} {SORT_DIRECTION}, cust_id ASC
            LIMIT {14} OFFSET {15}
            """;

        var sql = sqlTemplate.Replace("{SORT_COLUMN}", column).Replace("{SORT_DIRECTION}", direction);

        return await _db.Database
            .SqlQueryRaw<CompetitorBuyerRowRaw>(
                sql, q.TenantId, q.CompetitorTextTermIndexes, q.CompetitorTextTermValues, q.CompetitorCategoryTermIndexes, q.CompetitorCategoryTermIds,
                q.OwnTextTermIndexes, q.OwnTextTermValues, q.OwnCategoryTermIndexes, q.OwnCategoryTermIds,
                q.OwnExcludedItemIds, q.FromDt, q.ToDt, q.StoreIds, q.AllTimeHorizon, limit, offset)
            .ToListAsync(ct);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static (int Limit, int Offset) NormalizeLimitOffset(int page, int pageSize)
    {
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Max(1, pageSize);
        return (safePageSize, (safePage - 1) * safePageSize);
    }

    private static string Direction(bool descending) => descending ? "DESC" : "ASC";

    /// <summary>Boxes a nullable value-type SQL parameter as either the boxed value or
    /// <see cref="DBNull.Value"/> — same rationale as <c>PriceSegmentsRepository.NullableParam</c>
    /// (never a bare C# null reference into <c>SqlQueryRaw</c>'s non-nullable-annotated params
    /// array; DBNull.Value is the unambiguous ADO.NET way to represent a SQL NULL). The SQL side
    /// always reads these back through an explicit cast (e.g. <c>{11}::numeric IS NULL OR ...</c>).</summary>
    private static object NullableParam<T>(T? value) where T : struct => (object?)value ?? DBNull.Value;

    private static string BuyerSortColumn(string? sortBy) => AudienceBuilderSortKeys.NormalizeBuyers(sortBy) switch
    {
        "name" => "name",
        "receipts" => "receipt_count",
        "amount" => "total_amount",
        _ => "total_qty", // "qty" — confirmed competitor default, analysis doc §14.2
    };

    private static string MatchedItemSortColumn(string? sortBy) => AudienceBuilderSortKeys.NormalizeMatchedItems(sortBy) switch
    {
        "name" => "name",
        "receipts" => "receipt_count",
        "buyers" => "buyer_count",
        _ => "qty_sold", // "sold" — analysis doc §20.1
    };

    private static string CompetitorBuyerSortColumn(string? sortBy) => AudienceBuilderSortKeys.NormalizeCompetitorBuyers(sortBy) switch
    {
        "name" => "name",
        "receipts" => "receipt_count",
        "amount" => "total_amount",
        _ => "total_qty", // "qty"
    };
}
