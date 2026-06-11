import { Worker, Job, Queue } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

const notificationQueue = new Queue("notifications", { connection: redisConnection });

type ExpiryStatus = "safe" | "warning" | "critical" | "expired";

function computeStatus(daysLeft: number): ExpiryStatus {
  if (daysLeft < 0)  return "expired";
  if (daysLeft <= 1) return "critical";
  if (daysLeft <= 3) return "warning";
  return "safe";
}

async function runExpiryCheck(): Promise<void> {
  const client = await db.connect();
  try {
    // Disable RLS for the worker — runs as privileged service role
    await client.query("SET app.role = 'worker'");

    const { rows } = await client.query<{
      id: string;
      tenant_id: string;
      product_id: string;
      store_id: string;
      batch_number: string | null;
      quantity: number;
      expiry_date: string; // DATE as string "YYYY-MM-DD"
      status: string;
      notified_warning_at: string | null;
      notified_critical_at: string | null;
    }>(`
      SELECT "Id"                 AS id,
             "TenantId"           AS tenant_id,
             "ProductId"          AS product_id,
             "StoreId"            AS store_id,
             "BatchNumber"        AS batch_number,
             "Quantity"           AS quantity,
             "ExpiryDate"         AS expiry_date,
             "Status"             AS status,
             "NotifiedWarningAt"  AS notified_warning_at,
             "NotifiedCriticalAt" AS notified_critical_at
      FROM product_stock
      WHERE "Quantity" > 0
    `);

    const today = new Date();
    today.setUTCHours(0, 0, 0, 0);

    let updated = 0;
    let notified = 0;

    for (const row of rows) {
      const expiry = new Date(row.expiry_date + "T00:00:00Z");
      const msPerDay = 86_400_000;
      const daysLeft = Math.floor((expiry.getTime() - today.getTime()) / msPerDay);
      const newStatus = computeStatus(daysLeft);

      const statusChanged = newStatus !== row.status;
      const now = new Date().toISOString();

      // Determine if we need to send a notification
      const shouldNotifyWarning =
        newStatus === "warning" && row.notified_warning_at === null;
      const shouldNotifyCritical =
        newStatus === "critical" && row.notified_critical_at === null;
      const shouldNotifyExpired =
        newStatus === "expired" && row.notified_critical_at === null; // reuse critical flag for expired

      if (statusChanged || shouldNotifyWarning || shouldNotifyCritical || shouldNotifyExpired) {
        await client.query(
          `UPDATE product_stock
           SET "Status"             = $1,
               "LastCheckedAt"      = $2,
               "NotifiedWarningAt"  = CASE WHEN $3 THEN $2::timestamptz ELSE "NotifiedWarningAt"  END,
               "NotifiedCriticalAt" = CASE WHEN $4 THEN $2::timestamptz ELSE "NotifiedCriticalAt" END
           WHERE "Id" = $5`,
          [
            newStatus,
            now,
            shouldNotifyWarning,
            shouldNotifyCritical || shouldNotifyExpired,
            row.id,
          ]
        );
        updated++;

        if (shouldNotifyWarning || shouldNotifyCritical || shouldNotifyExpired) {
          await notificationQueue.add(
            "expiry-alert",
            {
              type:        "expiry_alert",
              tenantId:    row.tenant_id,
              storeId:     row.store_id,
              productId:   row.product_id,
              batchNumber: row.batch_number,
              status:      newStatus,
              daysLeft,
              quantity:    row.quantity,
            },
            { attempts: 3, backoff: { type: "exponential", delay: 60_000 } }
          );
          notified++;
        }
      }
    }

    console.log(
      `[expiry-check] processed ${rows.length} batches — updated: ${updated}, notified: ${notified}`
    );
  } finally {
    client.release();
  }
}

export function startExpiryCheckWorker(): Worker {
  const worker = new Worker(
    "expiry-check",
    async (job: Job) => {
      console.log(`[expiry-check] job ${job.id} started`);
      await runExpiryCheck();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("completed", (job) => {
    console.log(`[expiry-check] job ${job.id} completed`);
  });

  worker.on("failed", (job, err) => {
    console.error(`[expiry-check] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
