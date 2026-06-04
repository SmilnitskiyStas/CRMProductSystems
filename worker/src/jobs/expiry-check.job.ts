import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";

// TODO (TASK-008): query product_stock, update statuses, enqueue notifications
export function startExpiryCheckWorker(): Worker {
  const worker = new Worker(
    "expiry-check",
    async (job: Job) => {
      console.log(`[expiry-check] job ${job.id} — placeholder`);
    },
    { connection: redisConnection }
  );

  worker.on("failed", (job, err) => {
    console.error(`[expiry-check] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
