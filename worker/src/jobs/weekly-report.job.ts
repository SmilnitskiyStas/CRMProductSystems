import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";

// TODO (TASK-017): generate and send weekly analytics report
export function startWeeklyReportWorker(): Worker {
  const worker = new Worker(
    "weekly-report",
    async (job: Job) => {
      console.log(`[weekly-report] job ${job.id} — placeholder`);
    },
    { connection: redisConnection }
  );

  worker.on("failed", (job, err) => {
    console.error(`[weekly-report] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
