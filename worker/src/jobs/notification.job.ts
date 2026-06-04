import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";

// TODO (TASK-017): send Telegram / push / email notifications
export function startNotificationWorker(): Worker {
  const worker = new Worker(
    "notifications",
    async (job: Job) => {
      console.log(`[notifications] job ${job.id} — placeholder`, job.data);
    },
    {
      connection: redisConnection,
      concurrency: 5,
    }
  );

  worker.on("failed", (job, err) => {
    console.error(`[notifications] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
