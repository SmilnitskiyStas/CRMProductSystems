import { Worker, Job, Queue } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

const notificationQueue = new Queue("notifications", { connection: redisConnection });

export type ShelfScanItemVerdict = { name: string; status: string };

// ResultJson is serialized by the backend with camelCase (JsonSerializerDefaults.Web):
// { summary, items: [{ itemId, name, status: "ok"|"low"|"missing", ... }] }
export function extractShelfItemNames(resultJson: unknown): { missing: string[]; low: string[] } {
  let parsed: any = resultJson;
  if (typeof parsed === "string") {
    try { parsed = JSON.parse(parsed); } catch { return { missing: [], low: [] }; }
  }
  const items: ShelfScanItemVerdict[] = Array.isArray(parsed?.items) ? parsed.items : [];
  const names = (status: string) =>
    items.filter((i) => i?.status === status && typeof i.name === "string").map((i) => i.name);
  return { missing: names("missing"), low: names("low") };
}

/**
 * Claims every shelf_scans row with AlertPending = true (clearing the flag in the same
 * statement via UPDATE ... RETURNING, SKIP LOCKED so concurrent runs never claim the same row)
 * and enqueues one `shelf_empty` notification per scan. Claim + enqueue share a transaction:
 * if enqueueing fails the flag is rolled back and the next run retries. The BullMQ jobId is
 * deterministic per scan as a second guard against duplicates.
 */
async function runShelfAlert(): Promise<void> {
  const client = await db.connect();
  try {
    await client.query("SET app.role = 'worker'");
    await client.query("BEGIN");
    try {
      const { rows } = await client.query<{
        scan_id: string;
        tenant_id: string;
        location_id: string;
        zone_id: string;
        missing_count: number;
        low_count: number;
        summary: string | null;
        result_json: unknown;
      }>(
        `WITH claimed AS (
           UPDATE shelf_scans
              SET "AlertPending" = false
            WHERE "Id" IN (
              SELECT "Id" FROM shelf_scans
               WHERE "AlertPending" = true
               ORDER BY "CreatedAt"
               LIMIT 200
               FOR UPDATE SKIP LOCKED)
            RETURNING "Id", "TenantId", "LocationId", "ZoneId",
                      "MissingCount", "LowCount", "ResultJson"
         )
         SELECT c."Id"           AS scan_id,
                c."TenantId"     AS tenant_id,
                c."LocationId"   AS location_id,
                c."ZoneId"       AS zone_id,
                c."MissingCount" AS missing_count,
                c."LowCount"     AS low_count,
                c."ResultJson"->>'summary' AS summary,
                c."ResultJson"   AS result_json
           FROM claimed c`
      );

      for (const row of rows) {
        const names = extractShelfItemNames(row.result_json);
        await notificationQueue.add(
          "shelf-empty",
          {
            type:         "shelf_empty",
            scanId:       row.scan_id,
            tenantId:     row.tenant_id,
            storeId:      row.location_id,
            zoneId:       row.zone_id,
            missingCount: row.missing_count,
            lowCount:     row.low_count,
            missingItems: names.missing,
            lowItems:     names.low,
            summary:      row.summary,
          },
          {
            jobId: `shelf-empty-${row.scan_id}`,
            attempts: 3,
            backoff: { type: "exponential", delay: 60_000 },
          }
        );
      }

      await client.query("COMMIT");
      if (rows.length > 0) console.log(`[shelf-alert] enqueued ${rows.length} shelf alert(s)`);
    } catch (e) {
      await client.query("ROLLBACK").catch(() => {});
      throw e;
    }
  } finally {
    client.release();
  }
}

export function startShelfAlertWorker(): Worker {
  const worker = new Worker(
    "shelf-alert",
    async (_job: Job) => {
      await runShelfAlert();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("failed", (job, err) => {
    console.error(`[shelf-alert] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
