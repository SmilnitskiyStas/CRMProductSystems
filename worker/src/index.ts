import { Queue } from "bullmq";
import { redisConnection } from "./redis";
import { startExpiryCheckWorker } from "./jobs/expiry-check.job";
import { startStockSnapshotWorker } from "./jobs/stock-snapshot.job";
import { startNotificationWorker } from "./jobs/notification.job";
import { startWeeklyReportWorker } from "./jobs/weekly-report.job";
import { startCleanupWorker } from "./jobs/cleanup.job";
import { startWeatherFetchWorker } from "./jobs/weather-fetch.job";
import { startAiOrderWorker } from "./jobs/ai-order.job";
import { startTelegramListener } from "./jobs/telegram-listener";
import { startMqttListener } from "./jobs/mqtt-listener";
import { startFiscalizationRetryWorker } from "./jobs/fiscalization-retry.job";
import { startNotificationDispatchWorker } from "./jobs/notification-dispatch.job";
import { startPermissionGrantExpiryWorker } from "./jobs/permission-grant-expiry.job";
import { startLoyaltyTierRecomputeWorker } from "./jobs/loyalty-tier-recompute.job";
import { startLoyaltyAnnualResetWorker } from "./jobs/loyalty-annual-reset.job";
import { startLoyaltyBonusExpiryWorker } from "./jobs/loyalty-bonus-expiry.job";
import { startSupplierMetricsRecomputeWorker } from "./jobs/supplier-metrics-recompute.job";

async function scheduleRecurringJobs(): Promise<void> {
  const expiryQueue = new Queue("expiry-check", { connection: redisConnection });
  // Drop the legacy scheduler id left in Redis by an older worker version —
  // otherwise the job fires twice every hour.
  await expiryQueue.removeJobScheduler("expiry-check-hourly").catch(() => {});
  await expiryQueue.upsertJobScheduler("expiry-check-cron", { pattern: "0 * * * *" }, { name: "expiry-check" });

  // TASK-336: daily 00:10 — snapshot product_stock status counts, right after the
  // hourly expiry-check cycle has settled each batch's status for the day.
  const stockSnapshotQueue = new Queue("stock-snapshot", { connection: redisConnection });
  await stockSnapshotQueue.upsertJobScheduler("stock-snapshot-cron", { pattern: "10 0 * * *" }, { name: "stock-snapshot" });

  const weeklyQueue = new Queue("weekly-report", { connection: redisConnection });
  await weeklyQueue.upsertJobScheduler("weekly-report-cron", { pattern: "0 8 * * 0" }, { name: "weekly-report" });

  const cleanupQueue = new Queue("cleanup", { connection: redisConnection });
  await cleanupQueue.upsertJobScheduler("cleanup-cron", { pattern: "0 3 * * *" }, { name: "cleanup" });

  // TASK-653 / plan §"Worker-задача" (eventual-whistling-rabbit.md): daily 02:00 — recompute
  // marketplace supplier performance aggregates (delivery time overall + by region, chat
  // response median, cancellation rate, order accuracy). 02:00 is a deliberately clean slot
  // between the hourly loyalty jobs and cleanup (03:00) / loyalty-tier (04:00) / ai-order (05:00).
  const supplierMetricsQueue = new Queue("supplier-metrics-recompute", { connection: redisConnection });
  await supplierMetricsQueue.upsertJobScheduler(
    "supplier-metrics-recompute-cron",
    { pattern: "0 2 * * *" },
    { name: "supplier-metrics-recompute" }
  );

  // TASK-619 / plan §3: daily 04:00 — recompute loyalty tier ladder RFM scores per tenant with
  // loyalty enabled, after cleanup (03:00), before weather-fetch/ai-order (05:00-06:00).
  const loyaltyTierRecomputeQueue = new Queue("loyalty-tier-recompute", { connection: redisConnection });
  await loyaltyTierRecomputeQueue.upsertJobScheduler(
    "loyalty-tier-recompute-cron",
    { pattern: "0 4 * * *" },
    { name: "loyalty-tier-recompute" }
  );
  const loyaltyAnnualResetQueue = new Queue("loyalty-annual-reset", { connection: redisConnection });
  await loyaltyAnnualResetQueue.upsertJobScheduler("loyalty-annual-reset-cron", { pattern: "5 * * * *" }, { name: "loyalty-annual-reset" });
  const loyaltyBonusExpiryQueue = new Queue("loyalty-bonus-expiry", { connection: redisConnection });
  await loyaltyBonusExpiryQueue.upsertJobScheduler("loyalty-bonus-expiry-cron", { pattern: "35 * * * *" }, { name: "loyalty-bonus-expiry" });

  // v2-spec §6: daily 06:00 — fetch 7-day forecast for every store with coordinates
  const weatherQueue = new Queue("weather-fetch", { connection: redisConnection });
  await weatherQueue.upsertJobScheduler("weather-fetch-cron", { pattern: "0 6 * * *" }, { name: "weather-fetch" });

  // v2-spec §7: daily 05:00 — AI order suggestion per store + manager notification
  const aiOrderQueue = new Queue("ai-order", { connection: redisConnection });
  await aiOrderQueue.upsertJobScheduler("ai-order-cron", { pattern: "0 5 * * *" }, { name: "ai-order" });

  // v3-spec §3: every 5 min — retry pending_fiscalization transactions (TASK-069)
  const fiscalRetryQueue = new Queue("fiscalization-retry", { connection: redisConnection });
  await fiscalRetryQueue.upsertJobScheduler(
    "fiscalization-retry-cron",
    { pattern: "*/5 * * * *" },
    { name: "fiscalization-retry" }
  );

  // ADR-018 §2: every minute — dispatch Postgres outbox rows (Channel = 'system',
  // Status = 'pending') enqueued by backend services with no BullMQ producer of their own
  // (TASK-339)
  const notificationDispatchQueue = new Queue("notification-dispatch", { connection: redisConnection });
  await notificationDispatchQueue.upsertJobScheduler(
    "notification-dispatch-cron",
    { pattern: "* * * * *" },
    { name: "notification-dispatch" }
  );

  // ADR-019 §4: every 15 min — scan user_permission_grants for expiring-soon (24h) /
  // just-expired temporary grants and enqueue targeted outbox rows (TASK-342)
  const permissionGrantExpiryQueue = new Queue("permission-grant-expiry", { connection: redisConnection });
  await permissionGrantExpiryQueue.upsertJobScheduler(
    "permission-grant-expiry-cron",
    { pattern: "*/15 * * * *" },
    { name: "permission-grant-expiry" }
  );
}

async function main(): Promise<void> {
  console.log("[worker] Starting ShelfGuard background worker…");

  await scheduleRecurringJobs();

  startExpiryCheckWorker();
  startStockSnapshotWorker();
  startNotificationWorker();
  startWeeklyReportWorker();
  startCleanupWorker();
  startSupplierMetricsRecomputeWorker();
  startLoyaltyTierRecomputeWorker();
  startLoyaltyAnnualResetWorker();
  startLoyaltyBonusExpiryWorker();
  startWeatherFetchWorker();
  startAiOrderWorker();
  startTelegramListener();
  startMqttListener();
  startFiscalizationRetryWorker();
  startNotificationDispatchWorker();
  startPermissionGrantExpiryWorker();

  console.log("[worker] All workers started. Waiting for jobs…");
}

main().catch((err) => {
  console.error("[worker] Fatal error:", err);
  process.exit(1);
});
