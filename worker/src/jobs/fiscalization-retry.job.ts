// TASK-069: Fiscalization retry job
//
// Cron: every 5 minutes  (cron pattern "*/5 * * * *")
//
// 1. Calls GET /api/pos/sales/pending-fiscalization to fetch pos_transactions that are:
//    - Status = 'pending_fiscalization'
//    - RetryCount < 5
//    - CreatedAt < now - 30 s   (avoids racing with the immediate post-sale async attempt)
// 2. For each pending transaction calls POST /api/pos/sales/{id}/fiscalize.
// 3. Logs fiscal_code on success, error on failure.
//
// Auth: re-uses the same WORKER_API_EMAIL/WORKER_API_PASSWORD service account pattern
// as ai-order.job.ts — the worker logs in via /api/auth/login and sends a Bearer token.
// The new backend endpoints require AtLeastStoreManager, which the service account role satisfies.

import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";

const API_BASE = process.env.API_BASE_URL ?? "http://localhost:5100";
const SERVICE_EMAIL = process.env.WORKER_API_EMAIL ?? "";
const SERVICE_PASSWORD = process.env.WORKER_API_PASSWORD ?? "";

// ── Types (mirror PosDtos.cs response shapes) ──────────────────────────────

type PendingFiscalizationItem = {
  id: string;
  shiftId: string;
  tenantId: string;
  createdAt: string;
  retryCount: number;
  total: number;
};

type FiscalizeResult = {
  ok: boolean;
  fiscalNumber?: string | null;
  error?: string | null;
};

// ── Auth ───────────────────────────────────────────────────────────────────

async function login(): Promise<string> {
  const res = await fetch(`${API_BASE}/api/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: SERVICE_EMAIL, password: SERVICE_PASSWORD }),
  });
  if (!res.ok) throw new Error(`service login failed: HTTP ${res.status}`);
  const body = (await res.json()) as { accessToken?: string };
  if (!body.accessToken) throw new Error("service login returned no token");
  return body.accessToken;
}

// ── Core logic ─────────────────────────────────────────────────────────────

async function runFiscalizationRetry(): Promise<void> {
  if (!SERVICE_EMAIL || !SERVICE_PASSWORD) {
    console.warn("[fiscalization-retry] WORKER_API_EMAIL/PASSWORD not set — skipping");
    return;
  }

  const token = await login();

  // 1. Fetch all pending transactions
  const listRes = await fetch(`${API_BASE}/api/pos/sales/pending-fiscalization`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!listRes.ok) {
    const body = await listRes.text();
    throw new Error(`GET pending-fiscalization failed: HTTP ${listRes.status} — ${body.slice(0, 200)}`);
  }

  const pending = (await listRes.json()) as PendingFiscalizationItem[];

  if (pending.length === 0) {
    console.log("[fiscalization-retry] no pending transactions — nothing to do");
    return;
  }

  console.log(`[fiscalization-retry] found ${pending.length} pending transaction(s)`);

  let succeeded = 0;
  let failed = 0;

  // 2. Attempt fiscalization for each transaction sequentially to avoid
  //    hammering the fiscal provider with burst requests.
  for (const tx of pending) {
    try {
      const res = await fetch(`${API_BASE}/api/pos/sales/${tx.id}/fiscalize`, {
        method: "POST",
        headers: { Authorization: `Bearer ${token}` },
      });

      if (!res.ok) {
        const body = await res.text();
        console.warn(
          `[fiscalization-retry] tx ${tx.id} HTTP ${res.status} — ${body.slice(0, 150)}`
        );
        failed++;
        continue;
      }

      const result = (await res.json()) as FiscalizeResult;

      if (result.ok) {
        console.log(
          `[fiscalization-retry] tx ${tx.id} OK — fiscal_code=${result.fiscalNumber ?? "—"}`
        );
        succeeded++;
      } else {
        // Retry #N failed; backend already incremented RetryCount and may have set
        // fiscalization_failed if the limit was reached.
        console.warn(
          `[fiscalization-retry] tx ${tx.id} attempt failed (retry #${tx.retryCount + 1}): ${result.error ?? "unknown"}`
        );
        failed++;
      }
    } catch (e) {
      console.error(`[fiscalization-retry] tx ${tx.id} exception: ${(e as Error).message}`);
      failed++;
    }
  }

  console.log(
    `[fiscalization-retry] done — succeeded: ${succeeded}, failed/pending: ${failed}`
  );
}

// ── Worker export ──────────────────────────────────────────────────────────

export function startFiscalizationRetryWorker(): Worker {
  const worker = new Worker(
    "fiscalization-retry",
    async (job: Job) => {
      console.log(`[fiscalization-retry] job ${job.id} started`);
      await runFiscalizationRetry();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("completed", (job) =>
    console.log(`[fiscalization-retry] job ${job.id} completed`)
  );

  worker.on("failed", (job, err) =>
    console.error(`[fiscalization-retry] job ${job?.id} failed:`, err.message)
  );

  return worker;
}
