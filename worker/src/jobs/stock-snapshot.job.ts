import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

// TASK-336: daily snapshot of product_stock status counts, one row per
// (tenant, store, day). Written after the hourly expiry-check job has settled
// each batch's Status for the day, so the snapshot reflects "today's" counts.
// The dashboard compares live counts against a snapshot from N weeks back
// (see AnalyticsService.GetExpirySummaryComparisonAsync).

type StatusCountRow = {
  tenant_id: string;
  store_id: string;
  status: string;
  count: string; // COUNT(*) comes back as a numeric/bigint string
};

type Counts = { safe: number; warning: number; critical: number; expired: number };

async function runStockSnapshot(): Promise<void> {
  const client = await db.connect();
  try {
    // Disable RLS for the worker — runs as privileged service role (same
    // convention as expiry-check.job.ts; the worker's DB user owns/bypasses
    // these tables regardless, this just documents intent).
    await client.query("SET app.role = 'worker'");

    // TASK-360 (Block 9 audit): product_stock's store-scoping column is "LocationId", not
    // "StoreId" (v4 Store→Location rename) — this query threw "column StoreId does not
    // exist" on every run, same bug fixed in expiry-check.job.ts's SELECT.
    const { rows } = await client.query<StatusCountRow>(`
      SELECT "TenantId"   AS tenant_id,
             "LocationId" AS store_id,
             "Status"     AS status,
             COUNT(*)     AS count
      FROM product_stock
      WHERE "Quantity" > 0
      GROUP BY "TenantId", "LocationId", "Status"
    `);

    // UTC day boundary, matching expiry-check.job.ts's "today" convention.
    const snapshotDate = new Date().toISOString().slice(0, 10); // "YYYY-MM-DD"

    const byStore = new Map<string, Counts>();
    for (const row of rows) {
      const key = `${row.tenant_id}::${row.store_id}`;
      const counts = byStore.get(key) ?? { safe: 0, warning: 0, critical: 0, expired: 0 };
      const count = Number(row.count);

      switch (row.status) {
        case "safe":
          counts.safe += count;
          break;
        case "warning":
          counts.warning += count;
          break;
        case "critical":
          counts.critical += count;
          break;
        case "expired":
          counts.expired += count;
          break;
        default:
          // "needs_verification" and any other statuses aren't tracked by the
          // 4-bucket snapshot table (TASK-335) — dropped intentionally.
          break;
      }

      byStore.set(key, counts);
    }

    let upserted = 0;
    for (const [key, counts] of byStore) {
      const [tenantId, storeId] = key.split("::");
      // Atomic upsert on the (TenantId, LocationId, SnapshotDate) unique index —
      // avoids the find-then-save race the C# repository's UpsertAsync has
      // under concurrent writers (see handoff 335-to-336).
      await client.query(
        `INSERT INTO stock_status_snapshots
           ("Id", "TenantId", "LocationId", "SnapshotDate", "SafeCount", "WarningCount", "CriticalCount", "ExpiredCount")
         VALUES (gen_random_uuid(), $1, $2, $3::date, $4, $5, $6, $7)
         ON CONFLICT ("TenantId", "LocationId", "SnapshotDate")
         DO UPDATE SET
           "SafeCount"     = EXCLUDED."SafeCount",
           "WarningCount"  = EXCLUDED."WarningCount",
           "CriticalCount" = EXCLUDED."CriticalCount",
           "ExpiredCount"  = EXCLUDED."ExpiredCount"`,
        [tenantId, storeId, snapshotDate, counts.safe, counts.warning, counts.critical, counts.expired]
      );
      upserted++;
    }

    console.log(`[stock-snapshot] wrote ${upserted} store snapshot(s) for ${snapshotDate}`);
  } finally {
    client.release();
  }
}

export function startStockSnapshotWorker(): Worker {
  const worker = new Worker(
    "stock-snapshot",
    async (job: Job) => {
      console.log(`[stock-snapshot] job ${job.id} started`);
      await runStockSnapshot();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("completed", (job) => {
    console.log(`[stock-snapshot] job ${job.id} completed`);
  });

  worker.on("failed", (job, err) => {
    console.error(`[stock-snapshot] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
