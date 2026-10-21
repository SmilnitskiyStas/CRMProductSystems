import { Worker, Job, Queue } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";
import {
  LOOKBACK_DAYS,
  RENOTIFY_DAYS,
  addDays,
  evaluatePromoNoSales,
} from "../services/promo-no-sales";

const notificationQueue = new Queue("notifications", { connection: redisConnection });

export type PromoNoSalesAlertItem = {
  productName: string;
  promoName: string;
  storeStock: number;
  unitsSold: number;
  expectedUnits: number | null;
  days: number;
  reason: "no_sales" | "low_sales";
};

// One row per (store, product, promo) that is running now and where the store holds stock.
// Promo sources: active discounts, published running campaigns, published running rules
// (item + category targets; empty scope.storeIds = every store). Warehouses are not shelves.
// Same sources as PromoNoSalesRepository.cs (parity).
const CANDIDATES_SQL = `
WITH promos AS (
  SELECT d."TenantId" AS tenant_id, d."ProductId" AS product_id,
         'discount:' || d."Id"::text AS promo_key, d."Reason" AS promo_name,
         d."ValidFrom"::date AS promo_start, d."ValidUntil"::date AS promo_end,
         jsonb_build_array(d."LocationId"::text) AS store_ids
    FROM discounts d
   WHERE d."Status" = 'active' AND d."ValidFrom" <= now()
     AND (d."ValidUntil" IS NULL OR d."ValidUntil" > now())
  UNION ALL
  SELECT c."TenantId", cp."ProductId",
         'campaign:' || c."Id"::text, c."Title",
         c."StartsAt"::date, c."EndsAt"::date,
         (SELECT jsonb_agg(cl."LocationId"::text) FROM promotion_campaign_locations cl WHERE cl."CampaignId" = c."Id")
    FROM promotion_campaigns c
    JOIN promotion_campaign_products cp ON cp."CampaignId" = c."Id"
   WHERE c."Status" = 'published' AND c."StartsAt" <= now()
     AND (c."EndsAt" IS NULL OR c."EndsAt" > now())
     AND EXISTS (SELECT 1 FROM promotion_campaign_locations cl WHERE cl."CampaignId" = c."Id")
  UNION ALL
  SELECT r."TenantId", it."Id",
         'rule:' || r."Id"::text, r."Name",
         COALESCE(NULLIF(x.d->'scope'->>'validFrom', '')::timestamptz::date, CURRENT_DATE - ${LOOKBACK_DAYS}),
         NULLIF(x.d->'scope'->>'validTo', '')::timestamptz::date,
         x.d->'scope'->'storeIds'
    FROM promotion_rules r
    JOIN promotion_rule_versions v ON v."Id" = r."PublishedVersionId"
   CROSS JOIN LATERAL (SELECT v."DefinitionJson"::jsonb AS d) x
    JOIN items it ON it."TenantId" = r."TenantId" AND it."IsActive"
     AND (x.d->'targets'->'itemIds' ? it."Id"::text
          OR (it."CategoryId" IS NOT NULL AND x.d->'targets'->'categoryIds' ? it."CategoryId"::text))
   WHERE r."Status" = 'published'
     AND (NULLIF(x.d->'scope'->>'validFrom', '')::timestamptz IS NULL
          OR NULLIF(x.d->'scope'->>'validFrom', '')::timestamptz <= now())
     AND (NULLIF(x.d->'scope'->>'validTo', '')::timestamptz IS NULL
          OR NULLIF(x.d->'scope'->>'validTo', '')::timestamptz > now())
),
stock AS (
  SELECT s."TenantId" AS tenant_id, s."LocationId" AS location_id, s."ProductId" AS product_id,
         SUM(s."Quantity") AS qty
    FROM product_stock s
    JOIN locations l ON l."Id" = s."LocationId" AND l."LocationType" <> 'warehouse'
   WHERE s."Quantity" > 0
   GROUP BY s."TenantId", s."LocationId", s."ProductId"
)
SELECT p.tenant_id, st.location_id, l."Name" AS store_name,
       p.product_id, i."Name" AS product_name,
       p.promo_key, p.promo_name,
       p.promo_start::text AS promo_start, p.promo_end::text AS promo_end,
       st.qty::float8 AS store_stock,
       COALESCE(a."AduEffective", a."Adu30d")::float8 AS adu
  FROM promos p
  JOIN stock st ON st.tenant_id = p.tenant_id AND st.product_id = p.product_id
   AND (p.store_ids IS NULL OR jsonb_array_length(p.store_ids) = 0 OR p.store_ids ? st.location_id::text)
  JOIN locations l ON l."Id" = st.location_id
  JOIN items i ON i."Id" = p.product_id
  LEFT JOIN product_adu a ON a."LocationId" = st.location_id AND a."ProductId" = p.product_id
`;

type CandidateRow = {
  tenant_id: string;
  location_id: string;
  store_name: string;
  product_id: string;
  product_name: string;
  promo_key: string;
  promo_name: string;
  promo_start: string;
  promo_end: string | null;
  store_stock: number;
  adu: number | null;
};

/**
 * Daily: find promo items that do not sell although the store holds stock, dedup against
 * promo_no_sales_alerts (once per store+product+promo, again only after RENOTIFY_DAYS) and enqueue
 * one `promo_no_sales` notification per store listing the items. The upsert and the enqueue share a
 * transaction: if enqueueing fails the ledger rolls back and the next run retries.
 */
async function runPromoNoSales(): Promise<void> {
  const client = await db.connect();
  try {
    await client.query("SET app.role = 'worker'");
    const today = new Date().toISOString().slice(0, 10);
    const earliest = addDays(today, -LOOKBACK_DAYS);

    const { rows: candidates } = await client.query<CandidateRow>(CANDIDATES_SQL);
    if (candidates.length === 0) return;

    const { rows: salesRows } = await client.query<{
      location_id: string; product_id: string; date: string; qty: number;
    }>(
      `SELECT "LocationId" AS location_id, "ProductId" AS product_id, "Date"::text AS date,
              SUM("QuantitySold")::float8 AS qty
         FROM daily_sales
        WHERE "Date" >= $1::date AND "Date" < $2::date AND NOT "IsAnomaly"
          AND "ProductId" = ANY($3::uuid[])
        GROUP BY "LocationId", "ProductId", "Date"`,
      [earliest, today, Array.from(new Set(candidates.map((c) => c.product_id)))]
    );
    const salesByPair = new Map<string, Map<string, number>>();
    for (const r of salesRows) {
      const key = `${r.location_id}|${r.product_id}`;
      if (!salesByPair.has(key)) salesByPair.set(key, new Map());
      salesByPair.get(key)!.set(r.date, r.qty);
    }

    const flagged = candidates
      .map((c) => ({
        c,
        v: evaluatePromoNoSales(
          today, c.promo_start, c.promo_end, c.adu, c.store_stock,
          salesByPair.get(`${c.location_id}|${c.product_id}`) ?? new Map()
        ),
      }))
      .filter((x): x is { c: CandidateRow; v: NonNullable<typeof x.v> } => x.v !== null);
    if (flagged.length === 0) return;

    await client.query("BEGIN");
    try {
      // Claim the ledger rows: insert new ones, bump only those older than the re-notify interval.
      // RETURNING yields exactly the rows this run is allowed to notify about.
      const claimed = new Set<string>();
      for (const { c } of flagged) {
        const { rows } = await client.query(
          `INSERT INTO promo_no_sales_alerts ("TenantId", "LocationId", "ProductId", "PromoKey")
           VALUES ($1, $2, $3, $4)
           ON CONFLICT ("LocationId", "ProductId", "PromoKey") DO UPDATE
              SET "LastNotifiedAt" = now(), "NotifyCount" = promo_no_sales_alerts."NotifyCount" + 1
            WHERE promo_no_sales_alerts."LastNotifiedAt" < now() - make_interval(days => $5::int)
           RETURNING "Id"`,
          [c.tenant_id, c.location_id, c.product_id, c.promo_key, RENOTIFY_DAYS]
        );
        if (rows.length > 0) claimed.add(`${c.location_id}|${c.product_id}|${c.promo_key}`);
      }

      const byStore = new Map<string, { tenantId: string; storeName: string; items: PromoNoSalesAlertItem[] }>();
      for (const { c, v } of flagged) {
        if (!claimed.has(`${c.location_id}|${c.product_id}|${c.promo_key}`)) continue;
        if (!byStore.has(c.location_id)) {
          byStore.set(c.location_id, { tenantId: c.tenant_id, storeName: c.store_name, items: [] });
        }
        byStore.get(c.location_id)!.items.push({
          productName: c.product_name,
          promoName: c.promo_name,
          storeStock: c.store_stock,
          unitsSold: v.unitsSold,
          expectedUnits: v.expectedUnits,
          days: v.days,
          reason: v.reason,
        });
      }

      for (const [storeId, group] of byStore) {
        await notificationQueue.add(
          "promo-no-sales",
          {
            type: "promo_no_sales",
            tenantId: group.tenantId,
            storeId,
            storeName: group.storeName,
            items: group.items,
          },
          {
            jobId: `promo-no-sales-${storeId}-${today}`,
            attempts: 3,
            backoff: { type: "exponential", delay: 60_000 },
          }
        );
      }

      await client.query("COMMIT");
      if (byStore.size > 0) console.log(`[promo-no-sales] enqueued ${byStore.size} store alert(s)`);
    } catch (e) {
      await client.query("ROLLBACK").catch(() => {});
      throw e;
    }
  } finally {
    client.release();
  }
}

export function startPromoNoSalesWorker(): Worker {
  const worker = new Worker(
    "promo-no-sales",
    async (_job: Job) => {
      await runPromoNoSales();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("failed", (job, err) => {
    console.error(`[promo-no-sales] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
