import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

// v1-spec §8.3: cleanup.job — daily, archive sold_out batches older than 30 days.
// TASK-040 additionally purges aged event/log rows so the tables don't grow unbounded.
// stock_movements is a financial audit trail and is intentionally never purged.

const SOLD_OUT_ARCHIVE_DAYS = Number(process.env.CLEANUP_SOLD_OUT_DAYS ?? 30);
const NOTIFICATION_RETENTION_DAYS = Number(process.env.CLEANUP_NOTIFICATION_DAYS ?? 90);
const STOCK_EVENTS_RETENTION_DAYS = Number(process.env.CLEANUP_STOCK_EVENTS_DAYS ?? 180);
const ACTIVITY_LOGS_RETENTION_DAYS = Number(process.env.CLEANUP_ACTIVITY_LOGS_DAYS ?? 180);

async function runCleanup(): Promise<void> {
  const client = await db.connect();
  try {
    // Disable RLS for the worker — runs as privileged service role
    await client.query("SET app.role = 'worker'");

    // 1. Archive sold_out batches not touched for N days (LastCheckedAt is the
    //    most recent timestamp we have for when the batch reached sold_out)
    const archived = await client.query(
      `UPDATE product_stock
       SET "Status" = 'archived'
       WHERE "Status" = 'sold_out'
         AND "LastCheckedAt" < NOW() - make_interval(days => $1)`,
      [SOLD_OUT_ARCHIVE_DAYS]
    );

    // 2. Purge delivered/failed notification_queue rows past retention
    const notifications = await client.query(
      `DELETE FROM notification_queue
       WHERE "CreatedAt" < NOW() - make_interval(days => $1)
         AND "Status" <> 'pending'`,
      [NOTIFICATION_RETENTION_DAYS]
    );

    // 3. Purge aged stock_events (IoT/scan event stream, not the audit log)
    const stockEvents = await client.query(
      `DELETE FROM stock_events
       WHERE "CreatedAt" < NOW() - make_interval(days => $1)`,
      [STOCK_EVENTS_RETENTION_DAYS]
    );

    // 4. Purge aged activity_logs
    const activityLogs = await client.query(
      `DELETE FROM activity_logs
       WHERE "CreatedAt" < NOW() - make_interval(days => $1)`,
      [ACTIVITY_LOGS_RETENTION_DAYS]
    );

    console.log(
      `[cleanup] archived batches: ${archived.rowCount}, ` +
        `purged notifications: ${notifications.rowCount}, ` +
        `stock_events: ${stockEvents.rowCount}, ` +
        `activity_logs: ${activityLogs.rowCount}`
    );
  } finally {
    client.release();
  }
}

export function startCleanupWorker(): Worker {
  const worker = new Worker(
    "cleanup",
    async (job: Job) => {
      console.log(`[cleanup] job ${job.id} started`);
      await runCleanup();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("completed", (job) => {
    console.log(`[cleanup] job ${job.id} completed`);
  });

  worker.on("failed", (job, err) => {
    console.error(`[cleanup] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
