// TASK-673 QA harness — enqueue + process ONE supplier-metrics-recompute job against dev infra.
// Not committed. Run: REDIS_URL=... DATABASE_URL=... npx tsx qa673-run-snapshot.ts
import { Queue } from "bullmq";
import { redisConnection } from "./src/redis";
import { startSupplierMetricsRecomputeWorker } from "./src/jobs/supplier-metrics-recompute.job";

async function main() {
  const worker = startSupplierMetricsRecomputeWorker();
  const queue = new Queue("supplier-metrics-recompute", { connection: redisConnection });

  const done = new Promise<void>((resolve, reject) => {
    worker.on("completed", () => resolve());
    worker.on("failed", (_j, err) => reject(err));
  });

  await queue.add("supplier-metrics-recompute", {});
  console.log("[qa673] job enqueued, waiting…");

  await done;
  console.log("[qa673] job completed");

  await worker.close();
  await queue.close();
  process.exit(0);
}

main().catch((e) => {
  console.error("[qa673] FAILED", e);
  process.exit(1);
});
