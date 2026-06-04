import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";

// TODO (TASK-008+): archive old stock_events and activity_logs
export function startCleanupWorker(): Worker {
  const worker = new Worker(
    "cleanup",
    async (job: Job) => {
      console.log(`[cleanup] job ${job.id} — placeholder`);
    },
    { connection: redisConnection }
  );

  worker.on("failed", (job, err) => {
    console.error(`[cleanup] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
