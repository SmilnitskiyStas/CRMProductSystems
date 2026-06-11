import { Queue } from "bullmq";
import { redisConnection } from "./redis";
import { startExpiryCheckWorker } from "./jobs/expiry-check.job";
import { startNotificationWorker } from "./jobs/notification.job";
import { startWeeklyReportWorker } from "./jobs/weekly-report.job";
import { startCleanupWorker } from "./jobs/cleanup.job";

async function scheduleRecurringJobs(): Promise<void> {
  const expiryQueue = new Queue("expiry-check", { connection: redisConnection });
  // Drop the legacy scheduler id left in Redis by an older worker version —
  // otherwise the job fires twice every hour.
  await expiryQueue.removeJobScheduler("expiry-check-hourly").catch(() => {});
  await expiryQueue.upsertJobScheduler("expiry-check-cron", { pattern: "0 * * * *" }, { name: "expiry-check" });

  const weeklyQueue = new Queue("weekly-report", { connection: redisConnection });
  await weeklyQueue.upsertJobScheduler("weekly-report-cron", { pattern: "0 8 * * 0" }, { name: "weekly-report" });

  const cleanupQueue = new Queue("cleanup", { connection: redisConnection });
  await cleanupQueue.upsertJobScheduler("cleanup-cron", { pattern: "0 3 * * *" }, { name: "cleanup" });
}

async function main(): Promise<void> {
  console.log("[worker] Starting ShelfGuard background worker…");

  await scheduleRecurringJobs();

  startExpiryCheckWorker();
  startNotificationWorker();
  startWeeklyReportWorker();
  startCleanupWorker();

  console.log("[worker] All workers started. Waiting for jobs…");
}

main().catch((err) => {
  console.error("[worker] Fatal error:", err);
  process.exit(1);
});
