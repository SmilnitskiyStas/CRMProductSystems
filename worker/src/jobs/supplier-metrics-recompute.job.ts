import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

// TASK-653 / plan §"Worker-задача" (eventual-whistling-rabbit.md): nightly recompute of the
// marketplace supplier performance aggregates whose columns have existed since v4 but were
// never actually written — the `supplier_metrics` comment "Updated by background job" described
// a job that did not exist. Schema side landed in TASK-65x
// (`20260831090731_AddSupplierPerformanceData`): `DeliveryByRegion` jsonb,
// `DeliverySampleSize`, `ResponseSampleSize`, `AggregatesComputedAt` on `supplier_metrics`,
// `DestinationRegionCode` snapshot + partial index `ix_marketplace_orders_metrics` on
// `marketplace_orders`.
//
// Structure mirrors loyalty-tier-recompute.job.ts: direct `pg` queries via the shared `db` pool,
// `SET app.role = 'worker'` as the first statement on the connection so the RLS `worker_bypass`
// policies on suppliers / supplier_profiles / supplier_metrics / marketplace_orders /
// marketplace_order_receipt* / supplier_chat_* apply (this job spans EVERY tenant — there is no
// per-request `app.tenant_id` to key off). Deliberately not the callback-into-API pattern
// ai-order.job.ts uses; see that file's own comments for the silent-RLS-bug history.
//
// ╔══════════════════════════════════════════════════════════════════════════════════════════╗
// ║ WRITE BOUNDARY — load-bearing (plan risk 6; mirrors ADR-034 Decision 4)                   ║
// ║                                                                                           ║
// ║ This job writes ONLY these `supplier_metrics` columns:                                    ║
// ║     AvgDeliveryDays · DeliverySampleSize · DeliveryByRegion ·                              ║
// ║     ResponseTimeHours · ResponseSampleSize ·                                              ║
// ║     CancellationRate · OrderAccuracy · OnTimeDeliveryRate · CompositeScore ·               ║
// ║     AggregatesComputedAt                                                                   ║
// ║ (plus SupplierId / TenantId on the INSERT branch, when the row does not exist yet).       ║
// ║ TASK-689 (plan 1-partitioned-book.md Phase 6d, ADR-036 amendment) added `OnTimeDeliveryRate`║
// ║ and `CompositeScore` to this set — both worker-computed, both written in the SAME single    ║
// ║ `UPSERT_METRICS_SQL` statement as the columns above, so the disjointness argument below is  ║
// ║ unchanged. `CompositeScore` is DERIVED partly from `Rating`, but `Rating` is only READ      ║
// ║ (a cheap `RATING_PRE_READ_SQL` before the upsert) — never written here.                     ║
// ║                                                                                           ║
// ║ It must NEVER write `Rating` — that column is owned by the synchronous review path         ║
// ║ (MarketplaceRepository.UpsertMetricsRatingAsync, ADR-035), which also owns `UpdatedAt`.    ║
// ║ It must NEVER write `QualityScore` — there is no data source for it today; writing a       ║
// ║ placeholder would silently invent a number the marketplace UI renders as fact.             ║
// ║                                                                                           ║
// ║ `supplier_metrics` has NO xmin optimistic-concurrency token. What keeps this job and the   ║
// ║ synchronous Rating writer safe against each other is precisely that they touch DISJOINT    ║
// ║ COLUMNS via SEPARATE statements — Postgres row-level locking serialises the two UPDATEs    ║
// ║ and neither clobbers the other's columns. Any future "upsert the whole metrics row" path   ║
// ║ (here or in the repository) reintroduces the clobber risk and must add an explicit         ║
// ║ concurrency token first.                                                                   ║
// ╚══════════════════════════════════════════════════════════════════════════════════════════╝
//
// TASK-671 — nightly metric snapshot (`supplier_metrics_snapshots`):
// After the `supplier_metrics` upsert above, this job ALSO writes one append-only row per supplier
// per day into `supplier_metrics_snapshots` — a FULL point-in-time copy of the whole metric set,
// `Rating` and `QualityScore` INCLUDED. The write-boundary box above governs only the live, shared
// `supplier_metrics` row (no concurrency token, a separate synchronous `Rating` writer). The
// snapshot table is a DISTINCT, append-only table that nothing else writes, keyed by a UNIQUE
// (`SupplierId`, `SnapshotDate`) index — so the copy carries no clobber risk and a same-day re-run
// is an idempotent overwrite (`ON CONFLICT DO UPDATE`). `Rating` / `QualityScore` for the snapshot
// are read straight back from the `supplier_metrics` row this loop just upserted (this job computes
// neither column); `OnTimeDeliveryRate` / `CompositeScore` (TASK-689) ARE computed here and copied
// into the snapshot from the same values written to the live row. Feeds both
// `GET /api/marketplace/suppliers/{id}/metrics-history` and `GET /api/supplier-cabinet/metrics-history`.
//
// Population: every `suppliers` row that has a `supplier_profiles` row — deliberately NO
// `IsPublic` filter, so the numbers are already there the moment a supplier publishes.
// `suppliers."TenantId"` IS the supplier tenant used by `marketplace_orders."SupplierTenantId"`
// and `supplier_chat_sessions."SupplierTenantId"` (see
// MarketplaceRepository.GetSupplierTenantIdAsync), and is the same value
// UpsertMetricsRatingAsync stores in `supplier_metrics."TenantId"`.
//
// Known measurement limitations (accepted, plan risks 2-4):
//  · `DeliveredAt` only exists once the CLIENT finalises a MarketplaceOrderReceipt (ADR-033), so
//    shipped-but-never-received orders are invisible to the delivery average — it is biased
//    toward conscientious clients. There is no `ConfirmedAt`, so "supplier response to an order"
//    cannot be measured at all; response time comes from chat only.
//  · `DestinationRegionCode` is a snapshot written at order-creation time and is NULL for every
//    historical order. Such orders feed the overall average but not the per-region breakdown, so
//    `DeliverySampleSize >= Σ DeliveryByRegion[].sampleSize` — expected, not a bug. The UI must
//    show the sample sizes ("на основі N") or a sparse breakdown looks broken.
//  · The response median only counts chat sessions where the supplier EVENTUALLY replied. A
//    supplier who ignores half their threads therefore looks identical to one who answers all of
//    them; a real "response rate" metric is out of scope for this task.

/** Rolling window for delivery / accuracy samples. */
const DELIVERY_WINDOW_DAYS = 365;
/** Rolling window for chat response-time samples. */
const RESPONSE_WINDOW_DAYS = 180;

type SupplierRow = {
  supplier_id: string;
  tenant_id: string;
};

/**
 * One delivered order: how long it took, where it went (NULL for historical orders), and whether
 * it beat its promised date. `onTime` is `null` when the order carried no `ExpectedDeliveryDate`
 * (every order shipped before that column existed, plus orders shipped without an ETA) — those are
 * excluded from the on-time rate entirely, numerator and denominator both (TASK-689).
 */
export type DeliverySampleRow = {
  regionCode: string | null;
  days: number;
  onTime: boolean | null;
};

export type RegionDeliveryStat = {
  regionCode: string;
  avgDeliveryDays: number;
  sampleSize: number;
};

// ── Pure logic (DB-free, exported for a future test harness — worker/ has no test runner
//    today, same situation loyalty-tier-recompute.job.ts documents) ───────────────────────────

/** Round to `dp` decimal places. Guards against `-0` and non-finite input. */
export function roundTo(n: number, dp: number): number {
  if (!Number.isFinite(n)) return 0;
  const factor = 10 ** dp;
  const rounded = Math.round(n * factor) / factor;
  return rounded === 0 ? 0 : rounded;
}

/**
 * Overall delivery average across every sample, regardless of region.
 * Returns `avgDeliveryDays: null` (not 0) when there is nothing to average — "no data" and
 * "instant delivery" must not collapse to the same rendered value.
 */
export function computeAvgDeliveryDays(
  rows: DeliverySampleRow[]
): { avgDeliveryDays: number | null; sampleSize: number } {
  if (rows.length === 0) return { avgDeliveryDays: null, sampleSize: 0 };
  const total = rows.reduce((sum, r) => sum + r.days, 0);
  return { avgDeliveryDays: roundTo(total / rows.length, 2), sampleSize: rows.length };
}

/**
 * Per-region breakdown, sorted by region code so the stored jsonb is stable between runs
 * (a stable array means a no-op recompute produces an identical value, which keeps diffs and
 * any future change-detection honest).
 *
 * Rows with a NULL region contribute to the overall average only — see the header note about
 * `DeliverySampleSize >= Σ sampleSize`. `minSample` suppresses regions with too few orders to
 * be meaningful; the job passes the default of 1 (show everything, let the UI print `n=`).
 */
export function buildRegionBreakdown(
  rows: DeliverySampleRow[],
  minSample = 1
): RegionDeliveryStat[] {
  const buckets = new Map<string, { total: number; count: number }>();
  for (const row of rows) {
    if (row.regionCode === null) continue;
    const bucket = buckets.get(row.regionCode) ?? { total: 0, count: 0 };
    bucket.total += row.days;
    bucket.count += 1;
    buckets.set(row.regionCode, bucket);
  }

  return [...buckets.entries()]
    .filter(([, b]) => b.count >= minSample)
    .map(([regionCode, b]) => ({
      regionCode,
      avgDeliveryDays: roundTo(b.total / b.count, 2),
      sampleSize: b.count,
    }))
    .sort((a, b) => (a.regionCode < b.regionCode ? -1 : a.regionCode > b.regionCode ? 1 : 0));
}

/**
 * Median (not mean) hours-to-first-reply — one supplier who went on holiday for a fortnight
 * should not dominate their own average. Linear-interpolated middle for even-sized samples,
 * matching Postgres `PERCENTILE_CONT(0.5)` so a future SQL-side implementation would agree.
 */
export function computeMedianResponseHours(hours: number[]): number | null {
  const sorted = hours.filter((h) => Number.isFinite(h)).sort((a, b) => a - b);
  if (sorted.length === 0) return null;
  const mid = Math.floor(sorted.length / 2);
  const median =
    sorted.length % 2 === 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
  return roundTo(median, 2);
}

/**
 * cancelled / (delivered + cancelled) — the share of CLOSED orders that ended in a cancellation.
 * Orders still in flight (new/confirmed/shipped) are in neither side of the ratio: counting them
 * as "not cancelled" would make a supplier's rate drop simply by having open orders.
 * Returns null when the supplier has no closed orders at all.
 */
export function computeCancellationRate(cancelled: number, closed: number): number | null {
  if (closed <= 0) return null;
  return roundTo(cancelled / closed, 4);
}

/**
 * accurate / evaluated, over delivered orders that have a FINALISED receipt. An order with no
 * finalised receipt is excluded from the denominator entirely — absence of evidence is not
 * evidence of a short delivery. Returns null when nothing could be evaluated.
 */
export function computeOrderAccuracy(accurate: number, evaluated: number): number | null {
  if (evaluated <= 0) return null;
  return roundTo(accurate / evaluated, 4);
}

/**
 * TASK-689: on-time / (on-time + late), over the SAME 365-day delivered sample as
 * `computeAvgDeliveryDays`, but counting ONLY the orders that carried an `ExpectedDeliveryDate`
 * (`row.onTime !== null`). An order with no promised date can't be judged on-time, so it is
 * excluded from both sides of the ratio. Returns `null` when no delivered order in the window had
 * a promised date. Fraction 0.0000–1.0000, rounded to 4dp (matches the `numeric(5,4)` column).
 */
export function computeOnTimeDeliveryRate(rows: DeliverySampleRow[]): number | null {
  const judged = rows.filter((r) => r.onTime !== null);
  if (judged.length === 0) return null;
  const onTime = judged.filter((r) => r.onTime === true).length;
  return roundTo(onTime / judged.length, 4);
}

/**
 * TASK-689 (plan 1-partitioned-book.md Phase 6d, request #10) — composite quality score: the
 * equal-weight mean of whichever of these four components is available (non-null):
 *   · `Rating` / 5                                  (review score, 1–5 → 0–1)
 *   · `OrderAccuracy`                               (0–1 fraction)
 *   · `OnTimeDeliveryRate`                          (0–1 fraction)
 *   · clamp(1 − `ResponseTimeHours` / 48, 0, 1)     (48h+ to first reply ⇒ 0; instant ⇒ 1)
 * Nulls are skipped, not treated as 0 (a supplier with no reviews yet is not "a zero-star
 * supplier"). Returns `null` only when ALL four are null. Rounded to 3dp (`numeric(4,3)`).
 * Precedent: ADR-034's `(R+F+M)/3` loyalty score.
 */
export function computeCompositeScore(components: {
  rating: number | null;
  orderAccuracy: number | null;
  onTimeDeliveryRate: number | null;
  responseTimeHours: number | null;
}): number | null {
  const parts: number[] = [];
  if (components.rating !== null) parts.push(clamp01(components.rating / 5));
  if (components.orderAccuracy !== null) parts.push(clamp01(components.orderAccuracy));
  if (components.onTimeDeliveryRate !== null) parts.push(clamp01(components.onTimeDeliveryRate));
  if (components.responseTimeHours !== null)
    parts.push(clamp01(1 - components.responseTimeHours / 48));

  if (parts.length === 0) return null;
  return roundTo(parts.reduce((sum, p) => sum + p, 0) / parts.length, 3);
}

/** Clamp to [0, 1]; guards non-finite input to 0. */
function clamp01(n: number): number {
  if (!Number.isFinite(n)) return 0;
  return n < 0 ? 0 : n > 1 ? 1 : n;
}

// ── SQL ──────────────────────────────────────────────────────────────────────────────────────

/**
 * Every supplier that has a marketplace profile. No `IsPublic` filter on purpose (see header).
 */
const SUPPLIERS_SQL = `
  SELECT s."Id" AS supplier_id, s."TenantId" AS tenant_id
  FROM suppliers s
  JOIN supplier_profiles sp ON sp."SupplierId" = s."Id"
  ORDER BY s."Id"
`;

/**
 * One row per delivered order inside the rolling window. Aggregation happens in JS (see the
 * pure functions above) so the overall average and the per-region breakdown are derived from
 * exactly the same sample set — no chance of the two drifting apart through separate GROUP BYs.
 * The predicate matches the partial index `ix_marketplace_orders_metrics`
 * ("SupplierTenantId","DeliveredAt") WHERE "Status" = 'delivered'.
 *
 * `DeliveredAt >= ShippedAt` drops clock-skew / manual-correction rows that would otherwise
 * contribute a negative duration.
 *
 * TASK-689: `on_time` rides on this same sample so the on-time rate and the delivery average are
 * always derived from exactly the same set of orders. It is NULL when the order has no
 * `ExpectedDeliveryDate` (a `date` column, added by the supplier-portal expansion; NULL for every
 * order shipped before it existed or shipped without an ETA) — such orders are dropped from the
 * on-time ratio entirely. Otherwise it is `DeliveredAt::date <= ExpectedDeliveryDate`.
 */
const DELIVERY_SAMPLES_SQL = `
  SELECT o."DestinationRegionCode" AS region_code,
         (EXTRACT(EPOCH FROM (o."DeliveredAt" - o."ShippedAt")) / 86400.0)::float8 AS days,
         CASE
           WHEN o."ExpectedDeliveryDate" IS NULL THEN NULL
           ELSE (o."DeliveredAt"::date <= o."ExpectedDeliveryDate")
         END AS on_time
  FROM marketplace_orders o
  WHERE o."SupplierTenantId" = $1
    AND o."Status" = 'delivered'
    AND o."ShippedAt" IS NOT NULL
    AND o."DeliveredAt" IS NOT NULL
    AND o."DeliveredAt" >= o."ShippedAt"
    AND o."DeliveredAt" >= NOW() - ($2 || ' days')::interval
`;

/**
 * One row per chat session that produced a measurable response: hours between the client's first
 * message and the supplier's first message after it. Sessions the supplier never answered yield
 * no row at all (the `first_reply` join drops them) — see the header limitation.
 * Uses IX_supplier_chat_messages_SessionId_SenderTenantId_CreatedAt.
 */
const RESPONSE_SAMPLES_SQL = `
  WITH sessions AS (
    SELECT s."Id" AS session_id, s."ClientTenantId" AS client_tenant_id
    FROM supplier_chat_sessions s
    WHERE s."SupplierTenantId" = $1
  ),
  first_client AS (
    SELECT ss.session_id, MIN(m."CreatedAt") AS first_client_at
    FROM sessions ss
    JOIN supplier_chat_messages m
      ON m."SessionId" = ss.session_id
     AND m."SenderTenantId" = ss.client_tenant_id
    GROUP BY ss.session_id
  ),
  first_reply AS (
    SELECT fc.session_id, MIN(m."CreatedAt") AS first_reply_at
    FROM first_client fc
    JOIN supplier_chat_messages m
      ON m."SessionId" = fc.session_id
     AND m."SenderTenantId" = $1
     AND m."CreatedAt" > fc.first_client_at
    GROUP BY fc.session_id
  )
  SELECT (EXTRACT(EPOCH FROM (fr.first_reply_at - fc.first_client_at)) / 3600.0)::float8 AS hours
  FROM first_client fc
  JOIN first_reply fr ON fr.session_id = fc.session_id
  WHERE fc.first_client_at >= NOW() - ($2 || ' days')::interval
`;

/**
 * All-time, not windowed: a cancellation is a rare, memorable event and a 365-day window would
 * make a supplier's history quietly evaporate. Only CLOSED orders (delivered + cancelled) form
 * the denominator — see computeCancellationRate.
 */
const CANCELLATION_COUNTS_SQL = `
  SELECT COUNT(*) FILTER (WHERE o."Status" = 'cancelled')::int                    AS cancelled,
         COUNT(*) FILTER (WHERE o."Status" IN ('delivered', 'cancelled'))::int    AS closed
  FROM marketplace_orders o
  WHERE o."SupplierTenantId" = $1
`;

/**
 * Order accuracy over the same 365-day delivered window. An order counts as accurate when EVERY
 * line of its finalised receipt was received in full. Orders with no receipt, or a receipt still
 * in 'draft', never reach the `evaluated` CTE and so are excluded from both numerator and
 * denominator (a missing receipt says nothing about the supplier).
 *
 * `marketplace_order_receipts` has a UNIQUE index on "MarketplaceOrderId" (ADR-033: one
 * receiving session per order), so this join cannot fan out. A finalised receipt always has a
 * non-null QuantityReceived on every line (MarketplaceOrderReceiptService gates on it before
 * flipping Status to 'received') — the IS NOT NULL check is belt-and-braces.
 */
const ACCURACY_COUNTS_SQL = `
  WITH scoped AS (
    SELECT o."Id" AS order_id
    FROM marketplace_orders o
    WHERE o."SupplierTenantId" = $1
      AND o."Status" = 'delivered'
      AND o."DeliveredAt" IS NOT NULL
      AND o."DeliveredAt" >= NOW() - ($2 || ' days')::interval
  ),
  evaluated AS (
    SELECT sc.order_id,
           bool_and(ri."QuantityReceived" IS NOT NULL
                    AND ri."QuantityReceived" = ri."QuantityOrdered") AS accurate
    FROM scoped sc
    JOIN marketplace_order_receipts r
      ON r."MarketplaceOrderId" = sc.order_id
     AND r."Status" = 'received'
    JOIN marketplace_order_receipt_items ri ON ri."ReceiptId" = r."Id"
    GROUP BY sc.order_id
  )
  SELECT COUNT(*)::int                              AS evaluated,
         COUNT(*) FILTER (WHERE accurate)::int      AS accurate
  FROM evaluated
`;

/**
 * TASK-689: `Rating` read BEFORE the upsert, so `CompositeScore` (computed in JS and written in the
 * one `UPSERT_METRICS_SQL` statement below) can use the current review score. Returns no row for
 * the ~majority of suppliers that have never been reviewed — `rating` is then simply null, which is
 * also what `CompositeScore` would see post-upsert (the upsert never touches `Rating`). This is a
 * plain READ; `Rating` stays owned by the synchronous review path.
 */
const RATING_PRE_READ_SQL = `
  SELECT "Rating" FROM supplier_metrics WHERE "SupplierId" = $1
`;

/**
 * Load-or-create in one statement. Most suppliers have NO `supplier_metrics` row at all — the
 * only thing that creates one today is UpsertMetricsRatingAsync, on the first review — so this
 * cannot be a bare UPDATE. Conflict target is the UNIQUE index on "SupplierId"
 * (IX_supplier_metrics_SupplierId), the same 1-to-1 key that repository path uses.
 *
 * The DO UPDATE list is the write boundary from the header, verbatim (TASK-689 added
 * "OnTimeDeliveryRate" + "CompositeScore" — still one statement, still disjoint from the
 * synchronous "Rating"/"UpdatedAt"/"QualityScore" writer). "TenantId" is set on insert only;
 * "Rating", "QualityScore" and "UpdatedAt" appear nowhere in this statement.
 */
const UPSERT_METRICS_SQL = `
  INSERT INTO supplier_metrics
    ("SupplierId", "TenantId", "AvgDeliveryDays", "DeliverySampleSize", "DeliveryByRegion",
     "ResponseTimeHours", "ResponseSampleSize", "CancellationRate", "OrderAccuracy",
     "OnTimeDeliveryRate", "CompositeScore", "AggregatesComputedAt")
  VALUES ($1, $2, $3, $4, $5::jsonb, $6, $7, $8, $9, $10, $11, NOW())
  ON CONFLICT ("SupplierId") DO UPDATE SET
    "AvgDeliveryDays"      = EXCLUDED."AvgDeliveryDays",
    "DeliverySampleSize"   = EXCLUDED."DeliverySampleSize",
    "DeliveryByRegion"     = EXCLUDED."DeliveryByRegion",
    "ResponseTimeHours"    = EXCLUDED."ResponseTimeHours",
    "ResponseSampleSize"   = EXCLUDED."ResponseSampleSize",
    "CancellationRate"     = EXCLUDED."CancellationRate",
    "OrderAccuracy"        = EXCLUDED."OrderAccuracy",
    "OnTimeDeliveryRate"   = EXCLUDED."OnTimeDeliveryRate",
    "CompositeScore"       = EXCLUDED."CompositeScore",
    "AggregatesComputedAt" = EXCLUDED."AggregatesComputedAt"
`;

/**
 * TASK-671: `Rating` + `QualityScore` of the just-upserted `supplier_metrics` row. This job does
 * not compute either column — `Rating` is owned by the synchronous review path
 * (MarketplaceRepository.UpsertMetricsRatingAsync), `QualityScore` has no data source today — so
 * the daily snapshot reads them back rather than re-deriving them. The row is guaranteed to exist:
 * UPSERT_METRICS_SQL ran for this supplier immediately before.
 */
const SNAPSHOT_RATING_QUALITY_SQL = `
  SELECT "Rating", "QualityScore" FROM supplier_metrics WHERE "SupplierId" = $1
`;

/**
 * TASK-671: one append-only row per supplier per day in `supplier_metrics_snapshots` — a FULL
 * point-in-time copy of the metric set, `Rating` and `QualityScore` INCLUDED (see the header note
 * on why copying them here is safe: distinct append-only table, nothing else writes it). Idempotent
 * on the UNIQUE (`SupplierId`, `SnapshotDate`) index — a same-day re-run overwrites rather than
 * duplicates, so the job stays safe to retry. Runs on the same `SET app.role = 'worker'`
 * connection, so the table's `worker_bypass` RLS policy applies. Feeds
 * `GET /api/marketplace/suppliers/{id}/metrics-history`.
 */
const SNAPSHOT_UPSERT_SQL = `
  INSERT INTO supplier_metrics_snapshots
    ("Id", "SupplierId", "TenantId", "SnapshotDate", "AvgDeliveryDays", "OrderAccuracy",
     "QualityScore", "Rating", "CancellationRate", "ResponseTimeHours", "DeliverySampleSize",
     "ResponseSampleSize", "OnTimeDeliveryRate", "CompositeScore", "CreatedAt")
  VALUES (gen_random_uuid(), $1, $2, CURRENT_DATE, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, NOW())
  ON CONFLICT ("SupplierId", "SnapshotDate") DO UPDATE SET
    "AvgDeliveryDays"    = EXCLUDED."AvgDeliveryDays",
    "OrderAccuracy"      = EXCLUDED."OrderAccuracy",
    "QualityScore"       = EXCLUDED."QualityScore",
    "Rating"             = EXCLUDED."Rating",
    "CancellationRate"   = EXCLUDED."CancellationRate",
    "ResponseTimeHours"  = EXCLUDED."ResponseTimeHours",
    "DeliverySampleSize" = EXCLUDED."DeliverySampleSize",
    "ResponseSampleSize" = EXCLUDED."ResponseSampleSize",
    "OnTimeDeliveryRate" = EXCLUDED."OnTimeDeliveryRate",
    "CompositeScore"     = EXCLUDED."CompositeScore"
`;

async function runSupplierMetricsRecompute(): Promise<void> {
  const client = await db.connect();
  try {
    // MUST be the first statement on this connection — every table below is FORCE RLS and the
    // job spans all tenants, so the `worker_bypass` policies are the only thing letting it read.
    await client.query("SET app.role = 'worker'");

    const suppliersRes = await client.query<SupplierRow>(SUPPLIERS_SQL);

    let withDeliveryData = 0;
    let withResponseData = 0;
    let regionRowsWritten = 0;
    let snapshotsWritten = 0;

    for (const supplier of suppliersRes.rows) {
      const tenantId = supplier.tenant_id;

      const deliveryRes = await client.query<{
        region_code: string | null;
        days: number;
        on_time: boolean | null;
      }>(DELIVERY_SAMPLES_SQL, [tenantId, DELIVERY_WINDOW_DAYS]);
      const deliverySamples: DeliverySampleRow[] = deliveryRes.rows.map((r) => ({
        regionCode: r.region_code,
        days: Number(r.days),
        onTime: r.on_time,
      }));

      const { avgDeliveryDays, sampleSize: deliverySampleSize } =
        computeAvgDeliveryDays(deliverySamples);
      const regionBreakdown = buildRegionBreakdown(deliverySamples);
      const onTimeDeliveryRate = computeOnTimeDeliveryRate(deliverySamples);
      regionRowsWritten += regionBreakdown.length;
      if (deliverySampleSize > 0) withDeliveryData++;

      const responseRes = await client.query<{ hours: number }>(RESPONSE_SAMPLES_SQL, [
        tenantId,
        RESPONSE_WINDOW_DAYS,
      ]);
      const responseHours = responseRes.rows.map((r) => Number(r.hours));
      const responseTimeHours = computeMedianResponseHours(responseHours);
      const responseSampleSize = responseHours.length;
      if (responseSampleSize > 0) withResponseData++;

      const cancelRes = await client.query<{ cancelled: number; closed: number }>(
        CANCELLATION_COUNTS_SQL,
        [tenantId]
      );
      const cancellationRate = computeCancellationRate(
        cancelRes.rows[0]?.cancelled ?? 0,
        cancelRes.rows[0]?.closed ?? 0
      );

      const accuracyRes = await client.query<{ evaluated: number; accurate: number }>(
        ACCURACY_COUNTS_SQL,
        [tenantId, DELIVERY_WINDOW_DAYS]
      );
      const orderAccuracy = computeOrderAccuracy(
        accuracyRes.rows[0]?.accurate ?? 0,
        accuracyRes.rows[0]?.evaluated ?? 0
      );

      // TASK-689: read the current review score BEFORE the upsert so CompositeScore (written in the
      // one UPSERT_METRICS_SQL statement) can factor it in. No row for a never-reviewed supplier →
      // rating stays null. This is a plain READ — Rating remains owned by the synchronous review
      // path and is never written here.
      const preRatingRes = await client.query<{ Rating: string | null }>(RATING_PRE_READ_SQL, [
        supplier.supplier_id,
      ]);
      const ratingForComposite =
        preRatingRes.rows[0]?.Rating != null ? Number(preRatingRes.rows[0].Rating) : null;
      const compositeScore = computeCompositeScore({
        rating: ratingForComposite,
        orderAccuracy,
        onTimeDeliveryRate,
        responseTimeHours,
      });

      // Sample sizes are written as real counts (0, never NULL) — "based on 0 orders" is a fact
      // the UI can render, whereas NULL is indistinguishable from "this job never ran".
      // DeliveryByRegion is NULL rather than '[]' when there is nothing to break down, matching
      // AvgDeliveryDays = NULL for the same "no data" state.
      await client.query(UPSERT_METRICS_SQL, [
        supplier.supplier_id,
        tenantId,
        avgDeliveryDays === null ? null : avgDeliveryDays.toFixed(2),
        deliverySampleSize,
        regionBreakdown.length === 0 ? null : JSON.stringify(regionBreakdown),
        responseTimeHours === null ? null : responseTimeHours.toFixed(2),
        responseSampleSize,
        cancellationRate === null ? null : cancellationRate.toFixed(4),
        orderAccuracy === null ? null : orderAccuracy.toFixed(4),
        onTimeDeliveryRate === null ? null : onTimeDeliveryRate.toFixed(4),
        compositeScore === null ? null : compositeScore.toFixed(3),
      ]);

      // TASK-671: append-only daily snapshot — a FULL copy of the metric set. Rating and
      // QualityScore are read back from the row just upserted (this job computes neither); the
      // recompute columns reuse the values computed above. Idempotent on (SupplierId, SnapshotDate).
      const rqRes = await client.query<{ Rating: string | null; QualityScore: string | null }>(
        SNAPSHOT_RATING_QUALITY_SQL,
        [supplier.supplier_id]
      );
      const rq = rqRes.rows[0];

      await client.query(SNAPSHOT_UPSERT_SQL, [
        supplier.supplier_id,
        tenantId,
        avgDeliveryDays === null ? null : avgDeliveryDays.toFixed(2),
        orderAccuracy === null ? null : orderAccuracy.toFixed(4),
        rq?.QualityScore ?? null,
        rq?.Rating ?? null,
        cancellationRate === null ? null : cancellationRate.toFixed(4),
        responseTimeHours === null ? null : responseTimeHours.toFixed(2),
        deliverySampleSize,
        responseSampleSize,
        onTimeDeliveryRate === null ? null : onTimeDeliveryRate.toFixed(4),
        compositeScore === null ? null : compositeScore.toFixed(3),
      ]);
      snapshotsWritten++;
    }

    console.log(
      `[supplier-metrics-recompute] suppliers: ${suppliersRes.rows.length}, ` +
        `with delivery data: ${withDeliveryData}, with response data: ${withResponseData}, ` +
        `region rows: ${regionRowsWritten}, snapshots written: ${snapshotsWritten}`
    );
  } finally {
    client.release();
  }
}

export function startSupplierMetricsRecomputeWorker(): Worker {
  const worker = new Worker(
    "supplier-metrics-recompute",
    async (job: Job) => {
      console.log(`[supplier-metrics-recompute] job ${job.id} started`);
      await runSupplierMetricsRecompute();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("completed", (job) => {
    console.log(`[supplier-metrics-recompute] job ${job.id} completed`);
  });

  worker.on("failed", (job, err) => {
    console.error(`[supplier-metrics-recompute] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
