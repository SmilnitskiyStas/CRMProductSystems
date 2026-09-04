import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

// TASK-691 / plan catalog-form-buffers-promo.md Slice 4 (4a + 4b): the replenishment engine
// (ADU + CDA buffer) has always had the C# math (AduService / BufferService) but only ran it
// on demand — the "generate order" button chains recalculateAdu -> recalculateBuffers. Nothing
// aggregated POS sales into daily_sales and nothing recomputed ADU / buffers on a schedule, so
// on any tenant that never imported a sales CSV the whole engine sat on an empty daily_sales
// table and produced nothing. This nightly job closes both gaps:
//
//   Phase 1  pos_transactions (+items) from the last N days  ->  daily_sales (Source = 'pos')
//   Phase 2  daily_sales  ->  product_adu        (ADU windows 30/60/90, data-density grouping)
//   Phase 3  product_adu  ->  product_buffer     (CDA green/yellow/red zones)
//
// Structure mirrors supplier-metrics-recompute.job.ts / stock-snapshot.job.ts: direct `pg`
// queries via the shared `db` pool, `SET app.role = 'worker'` as the FIRST statement on the
// connection so the RLS `worker_bypass` policies on pos_transactions / pos_transaction_items /
// daily_sales / discounts / items / supply_schedules / product_adu / product_buffer apply — this
// job spans EVERY tenant and there is no per-request `app.tenant_id`. Deliberately NOT the
// callback-into-API pattern ai-order.job.ts uses (see that file's silent-RLS-bug history);
// the worker service account is single-tenant, so an API loop could not reach every tenant.
//
// ╔══════════════════════════════════════════════════════════════════════════════════════════╗
// ║ MATH PARITY — load-bearing                                                                 ║
// ║                                                                                           ║
// ║ Phase 2/3 reimplement, in SQL, the pure C# calculators:                                    ║
// ║   · AduCalculator      — backend/ShelfGuard.Application/Features/Adu/AduService.cs          ║
// ║   · CdaBufferCalculator — backend/ShelfGuard.Application/Features/Buffer/BufferService.cs   ║
// ║ The "generate order" button runs the C# path; this job runs the SQL path. They MUST agree  ║
// ║ or the two produce divergent product_buffer rows. When either calculator changes, change   ║
// ║ the matching SQL here (a comment on each C# class points back to this file).                ║
// ║                                                                                           ║
// ║ Accepted, documented divergences (all immaterial to a min/max-stock suggestion):           ║
// ║  1. Rounding mode. Postgres round() is half-away-from-zero; C# Math.Round is banker's       ║
// ║     (half-to-even). Sub-0.01-unit effect on a buffer quantity.                             ║
// ║  2. Zero-sales products. AduService writes an all-NULL product_adu row for every eligible   ║
// ║     product with no valid sales day (so it can count "insufficient data"). This job writes  ║
// ║     no row for them. Downstream-equivalent: both yield AduEffective = NULL -> no buffer.    ║
// ║  3. This job never writes Item.MinStock / MaxStock / SafetyBuffer — same as the C# path.    ║
// ║     Those stay user-owned; the catalog form only *suggests* the computed values (Slice 4c).║
// ╚══════════════════════════════════════════════════════════════════════════════════════════╝

/**
 * Phase 1 re-aggregates a trailing window each night, not just "yesterday" — cheap insurance
 * against a late fiscalization, a backdated transaction, or the worker missing a night. The
 * upsert makes a re-run idempotent.
 */
const POS_LOOKBACK_DAYS = 3;

/** ADU longest window (v2-spec §1). Matches AduCalculator.MaxWindowDays. */
const ADU_MAX_WINDOW_DAYS = 90;

// ── Phase 1: POS -> daily_sales ────────────────────────────────────────────────────────────────
//
// Sums pos_transaction_items.Quantity per (tenant, location, product, calendar day UTC) over
// transactions in ('fiscalized', 'pending_fiscalization') — a 'fiscalization_failed' row is a
// sale that never completed and is excluded. QuantityEndOfDay is left NULL (transactions carry
// no end-of-day stock count); AduCalculator.IsValidDay treats sold > 0 as a valid day regardless.
//
// IsPromoDay is set when a promo/campaign discount for that product+location covered the day —
// AduCalculator excludes promo days from the ADU sample, same as the C# GetEligibleProductIdsAsync
// path excludes currently-discounted products.
//
// The `ON CONFLICT ... WHERE daily_sales."Source" = 'pos'` guard means this job NEVER overwrites a
// row a human entered by hand (Source 'manual') or a CSV import ('import') — a manual correction
// for a given day always wins over the automatic POS aggregate.
const PHASE1_POS_TO_DAILY_SALES_SQL = `
  WITH sold AS (
    SELECT t."TenantId"                             AS tenant_id,
           t."LocationId"                           AS location_id,
           ti."ProductId"                           AS product_id,
           (t."CreatedAt" AT TIME ZONE 'UTC')::date AS sale_date,
           SUM(ti."Quantity")                       AS qty
    FROM pos_transactions t
    JOIN pos_transaction_items ti ON ti."TransactionId" = t."Id"
    WHERE t."Status" IN ('fiscalized', 'pending_fiscalization')
      AND t."CreatedAt" >= $1
      AND t."CreatedAt" <  $2
    GROUP BY 1, 2, 3, 4
  )
  INSERT INTO daily_sales
    ("Id", "TenantId", "LocationId", "ProductId", "Date",
     "QuantitySold", "QuantityEndOfDay", "IsPromoDay", "IsAnomaly", "Source", "CreatedAt")
  SELECT gen_random_uuid(), s.tenant_id, s.location_id, s.product_id, s.sale_date,
         s.qty, NULL,
         EXISTS (
           SELECT 1 FROM discounts d
           WHERE d."ProductId"  = s.product_id
             AND d."LocationId" = s.location_id
             AND (d."Reason" = 'promo' OR d."PromotionCampaignId" IS NOT NULL)
             AND d."ValidFrom"::date <= s.sale_date
             AND (d."ValidUntil" IS NULL OR d."ValidUntil"::date >= s.sale_date)
         ),
         false, 'pos', now()
  FROM sold s
  ON CONFLICT ("LocationId", "ProductId", "Date") DO UPDATE
    SET "QuantitySold" = EXCLUDED."QuantitySold",
        "IsPromoDay"   = EXCLUDED."IsPromoDay",
        "CreatedAt"    = now()
    WHERE daily_sales."Source" = 'pos'
`;

// ── Phase 2: daily_sales -> product_adu ────────────────────────────────────────────────────────
//
// SQL port of AduRepository.GetEligibleProductIdsAsync + AduCalculator.Compute, for every store
// at once. $1 = "today" (UTC date); today itself is never counted (it is still in progress).
//
//   eligible  — Item.IsActive AND ManagementType 'MTS' AND DefaultSupplierId set AND that
//               supplier has an active supply_schedule into the store AND the product is not
//               under a discount active right now (v2-spec §1).
//   valid     — daily_sales rows that are AduCalculator.IsValidDay: not promo, not anomaly,
//               (sold > 0 OR end-of-day > 0), Date in [today-90, today). The unique index
//               (LocationId, ProductId, Date) already guarantees one row per day, so the C#
//               DistinctBy(Date) needs no SQL equivalent.
//   win       — per product: valid-day COUNT and QuantitySold SUM in each of the 30/60/90 windows.
//   ADU_N     — SUM / COUNT over window N, 4 dp (AduCalculator.WindowAdu).
//   group     — tightest window that clears its threshold: 30d>=20 -> 3, 60d>=15 -> 2,
//               90d>=10 -> 1, else none. effective ADU = the ADU of the group's window.
const PHASE2_ADU_SQL = `
  WITH eligible AS (
    SELECT i."Id" AS product_id, i."TenantId" AS tenant_id, sch."LocationId" AS location_id
    FROM items i
    JOIN supply_schedules sch
      ON sch."SupplierId" = i."DefaultSupplierId"
     AND sch."IsActive"
    WHERE i."IsActive"
      AND i."ManagementType" = 'MTS'
      AND i."DefaultSupplierId" IS NOT NULL
      AND NOT EXISTS (
        SELECT 1 FROM discounts d
        WHERE d."ProductId"  = i."Id"
          AND d."LocationId" = sch."LocationId"
          AND d."Status"     = 'active'
          AND d."ValidFrom" <= now()
          AND (d."ValidUntil" IS NULL OR d."ValidUntil" >= now())
      )
  ),
  valid AS (
    SELECT e.tenant_id, e.location_id, e.product_id,
           ds."Date" AS d, ds."QuantitySold" AS qty
    FROM eligible e
    JOIN daily_sales ds
      ON ds."LocationId" = e.location_id
     AND ds."ProductId"  = e.product_id
    WHERE NOT ds."IsPromoDay"
      AND NOT ds."IsAnomaly"
      AND (ds."QuantitySold" > 0 OR COALESCE(ds."QuantityEndOfDay", 0) > 0)
      AND ds."Date" <  $1::date
      AND ds."Date" >= $1::date - ${ADU_MAX_WINDOW_DAYS}
  ),
  win AS (
    SELECT tenant_id, location_id, product_id,
           (COUNT(*) FILTER (WHERE d >= $1::date - 30))::int AS vd30,
           (COUNT(*) FILTER (WHERE d >= $1::date - 60))::int AS vd60,
           COUNT(*)::int                                     AS vd90,
           SUM(qty) FILTER (WHERE d >= $1::date - 30)        AS s30,
           SUM(qty) FILTER (WHERE d >= $1::date - 60)        AS s60,
           SUM(qty)                                          AS s90
    FROM valid
    GROUP BY tenant_id, location_id, product_id
  ),
  calc AS (
    SELECT tenant_id, location_id, product_id, vd30, vd60, vd90,
           CASE WHEN vd30 > 0 THEN round(s30 / vd30, 4) END AS adu30,
           CASE WHEN vd60 > 0 THEN round(s60 / vd60, 4) END AS adu60,
           CASE WHEN vd90 > 0 THEN round(s90 / vd90, 4) END AS adu90,
           CASE WHEN vd30 >= 20 THEN 3::smallint
                WHEN vd60 >= 15 THEN 2::smallint
                WHEN vd90 >= 10 THEN 1::smallint
                ELSE NULL END AS product_group
    FROM win
  )
  INSERT INTO product_adu
    ("Id", "TenantId", "LocationId", "ProductId",
     "Adu30d", "Adu60d", "Adu90d", "AduEffective", "ProductGroup",
     "ValidDays30d", "ValidDays60d", "CalculatedAt")
  SELECT gen_random_uuid(), tenant_id, location_id, product_id,
         adu30, adu60, adu90,
         CASE product_group WHEN 3 THEN adu30 WHEN 2 THEN adu60 WHEN 1 THEN adu90 ELSE NULL END,
         product_group, vd30, vd60, now()
  FROM calc
  ON CONFLICT ("LocationId", "ProductId") DO UPDATE SET
    "Adu30d"       = EXCLUDED."Adu30d",
    "Adu60d"       = EXCLUDED."Adu60d",
    "Adu90d"       = EXCLUDED."Adu90d",
    "AduEffective" = EXCLUDED."AduEffective",
    "ProductGroup" = EXCLUDED."ProductGroup",
    "ValidDays30d" = EXCLUDED."ValidDays30d",
    "ValidDays60d" = EXCLUDED."ValidDays60d",
    "CalculatedAt" = EXCLUDED."CalculatedAt"
`;

// ── Phase 3: product_adu -> product_buffer ─────────────────────────────────────────────────────
//
// SQL port of BufferService.RecalculateAsync + CdaBufferCalculator, for every store at once.
// $1 = "today" (UTC date). Starts from product_adu rows with a non-NULL AduEffective — same as
// BufferRepository.GetEffectiveAdusAsync — and INNER JOINs the product's DefaultSupplierId to an
// active supply_schedule into the store; a product with no supplier/schedule is dropped (the C#
// path counts it as "skipped").
//
//   lead_time    = schedule.OrderLeadDays when > 0, else 1        (CdaBufferCalculator: DefaultLeadTimeDays)
//   order_cycle  = round(7 / deliveries-per-week, 1), min 1/week  (CdaBufferCalculator.CycleFromSchedule)
//   variability  = CV (population stddev / mean) of valid-day QuantitySold over the product
//                  group's window (group 3 -> 30d, 2 -> 60d, else 90d), clamped to [0.2, 1.5];
//                  < 2 samples or mean <= 0 -> 0.2         (CdaBufferCalculator.Variability)
//   green        = round(adu * (lead_time + order_cycle), 2)
//   yellow       = round(adu * order_cycle * variability, 2)
//   red          = round(adu * lead_time * 1.0, 2)          (DefaultSafetyFactor = 1.0)
//   total        = green + yellow + red                      (sum of the already-rounded zones)
const PHASE3_BUFFER_SQL = `
  WITH base AS (
    SELECT pa."TenantId"                  AS tenant_id,
           pa."LocationId"                AS location_id,
           pa."ProductId"                 AS product_id,
           pa."AduEffective"              AS adu,
           COALESCE(pa."ProductGroup", 1) AS product_group,
           CASE WHEN sch."OrderLeadDays" IS NOT NULL AND sch."OrderLeadDays" > 0
                THEN sch."OrderLeadDays"::numeric
                ELSE 1::numeric END AS lead_time,
           round(7.0 / GREATEST(1, COALESCE(array_length(sch."DayOfWeek", 1), 0)), 1) AS order_cycle
    FROM product_adu pa
    JOIN items i
      ON i."Id" = pa."ProductId"
     AND i."DefaultSupplierId" IS NOT NULL
    JOIN supply_schedules sch
      ON sch."LocationId" = pa."LocationId"
     AND sch."SupplierId" = i."DefaultSupplierId"
     AND sch."IsActive"
    WHERE pa."AduEffective" IS NOT NULL
  ),
  cv AS (
    SELECT b.location_id, b.product_id,
           CASE
             WHEN COUNT(ds."Date") < 2          THEN 0.2::numeric
             WHEN AVG(ds."QuantitySold") <= 0   THEN 0.2::numeric
             ELSE GREATEST(0.2, LEAST(1.5,
                    round(stddev_pop(ds."QuantitySold") / AVG(ds."QuantitySold"), 2)))
           END AS variability
    FROM base b
    LEFT JOIN daily_sales ds
      ON ds."LocationId" = b.location_id
     AND ds."ProductId"  = b.product_id
     AND NOT ds."IsPromoDay"
     AND NOT ds."IsAnomaly"
     AND (ds."QuantitySold" > 0 OR COALESCE(ds."QuantityEndOfDay", 0) > 0)
     AND ds."Date" <  $1::date
     AND ds."Date" >= $1::date - (CASE b.product_group WHEN 3 THEN 30 WHEN 2 THEN 60 ELSE 90 END)
    GROUP BY b.location_id, b.product_id
  ),
  zones AS (
    SELECT b.tenant_id, b.location_id, b.product_id, b.lead_time, b.order_cycle,
           round(b.adu * (b.lead_time + b.order_cycle), 2)      AS green,
           round(b.adu * b.order_cycle * cv.variability, 2)     AS yellow,
           round(b.adu * b.lead_time * 1.0, 2)                  AS red
    FROM base b
    JOIN cv ON cv.location_id = b.location_id AND cv.product_id = b.product_id
  )
  INSERT INTO product_buffer
    ("Id", "TenantId", "LocationId", "ProductId",
     "BufferGreen", "BufferYellow", "BufferRed", "BufferTotal",
     "LeadTimeDays", "OrderCycleDays", "CalculatedAt")
  SELECT gen_random_uuid(), tenant_id, location_id, product_id,
         green, yellow, red, green + yellow + red,
         lead_time, order_cycle, now()
  FROM zones
  ON CONFLICT ("LocationId", "ProductId") DO UPDATE SET
    "BufferGreen"    = EXCLUDED."BufferGreen",
    "BufferYellow"   = EXCLUDED."BufferYellow",
    "BufferRed"      = EXCLUDED."BufferRed",
    "BufferTotal"    = EXCLUDED."BufferTotal",
    "LeadTimeDays"   = EXCLUDED."LeadTimeDays",
    "OrderCycleDays" = EXCLUDED."OrderCycleDays",
    "CalculatedAt"   = EXCLUDED."CalculatedAt"
`;

/** UTC start-of-day for `d` (00:00:00.000Z). */
function startOfUtcDay(d: Date): Date {
  return new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()));
}

async function runReplenishmentRecompute(): Promise<void> {
  const client = await db.connect();
  try {
    // MUST be the first statement on this connection — every table below is FORCE RLS and the
    // job spans all tenants, so the `worker_bypass` policies are the only thing letting it
    // read/write.
    await client.query("SET app.role = 'worker'");

    const today = startOfUtcDay(new Date());
    const windowStart = new Date(today.getTime() - POS_LOOKBACK_DAYS * 86_400_000);
    const todayDate = today.toISOString().slice(0, 10); // 'YYYY-MM-DD'

    const phase1 = await client.query(PHASE1_POS_TO_DAILY_SALES_SQL, [
      windowStart.toISOString(),
      today.toISOString(),
    ]);
    const phase2 = await client.query(PHASE2_ADU_SQL, [todayDate]);
    const phase3 = await client.query(PHASE3_BUFFER_SQL, [todayDate]);

    console.log(
      `[replenishment-recompute] daily_sales rows from POS: ${phase1.rowCount ?? 0}; ` +
        `product_adu upserts: ${phase2.rowCount ?? 0}; product_buffer upserts: ${phase3.rowCount ?? 0}`
    );
  } finally {
    client.release();
  }
}

export function startReplenishmentRecomputeWorker(): Worker {
  const worker = new Worker(
    "replenishment-recompute",
    async (job: Job) => {
      console.log(`[replenishment-recompute] job ${job.id} started`);
      await runReplenishmentRecompute();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("completed", (job) => {
    console.log(`[replenishment-recompute] job ${job.id} completed`);
  });

  worker.on("failed", (job, err) => {
    console.error(`[replenishment-recompute] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
