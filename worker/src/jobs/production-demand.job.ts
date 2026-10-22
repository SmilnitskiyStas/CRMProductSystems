// TASK-752: automatic demand analysis for own-production items (stage 3).
//
// Cron: daily 05:30 (after ai-order at 05:00). Calls POST /api/production-requests/auto-generate
// with the worker service account (same WORKER_API_EMAIL/PASSWORD pattern as ai-order.job.ts).
// The server creates/refreshes ONE auto DRAFT request per store (never submitted automatically)
// and enqueues the `production_request.auto_draft` outbox notification, which
// notification-dispatch.job.ts delivers to the store managers.
//
// Limitation (same as ai-order): the service account belongs to ONE tenant, so only that
// tenant's stores are analysed. Tenants without a production location get HTTP 422 (skipped).

import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";

const API_BASE = process.env.API_BASE_URL ?? "http://localhost:5100";
const SERVICE_EMAIL = process.env.WORKER_API_EMAIL ?? "";
const SERVICE_PASSWORD = process.env.WORKER_API_PASSWORD ?? "";

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

async function runProductionDemandAnalysis(): Promise<void> {
  if (!SERVICE_EMAIL || !SERVICE_PASSWORD) {
    console.warn("[production-demand] WORKER_API_EMAIL/PASSWORD not set — skipping");
    return;
  }

  const token = await login();
  const res = await fetch(`${API_BASE}/api/production-requests/auto-generate`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({}),
  });

  if (!res.ok) {
    const body = await res.text();
    console.warn(`[production-demand] HTTP ${res.status} — ${body.slice(0, 200)}`);
    return;
  }

  const drafts = (await res.json()) as { id: string; storeLocationName: string; lines: unknown[] }[];
  for (const d of drafts) {
    console.log(`[production-demand] ${d.storeLocationName}: draft ${d.id} (${d.lines.length} lines)`);
  }
  console.log(`[production-demand] done: ${drafts.length} draft(s) created/updated`);
}

export function startProductionDemandWorker(): Worker {
  const worker = new Worker(
    "production-demand",
    async (job: Job) => {
      console.log(`[production-demand] job ${job.id} started`);
      await runProductionDemandAnalysis();
    },
    { connection: redisConnection, concurrency: 1 },
  );

  worker.on("completed", (job) => console.log(`[production-demand] job ${job.id} completed`));
  worker.on("failed", (job, err) => console.error(`[production-demand] job ${job?.id} failed:`, err.message));

  return worker;
}
