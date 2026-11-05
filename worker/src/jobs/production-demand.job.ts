// TASK-752 / TASK-754: automatic demand analysis for own-production items, PER TENANT.
//
// Cron: daily 05:30 (after ai-order at 05:00).
//
// TASK-754 replaced the single service-account login (WORKER_API_EMAIL — analysed only that
// account's tenant) with one isolated run per tenant:
//   1. Enumerate candidate tenants straight from Postgres (worker role, read-only SELECT inside a
//      transaction with SET LOCAL — never a session-wide SET on a pooled connection): active
//      tenants with the `production` module AND >= 1 active location of Type 'production'.
//      This list is only a hint; the API re-verifies everything.
//   2. For EACH tenant: POST {INTERNAL_API_BASE_URL}/api/internal/production-demand/run
//      { tenantId } with the X-Internal-Job-Key shared secret. The server runs the generation in
//      a DB transaction bound to exactly that tenant's RLS context (no bypass), creates/refreshes
//      ONE auto DRAFT per store (never submitted) and enqueues `production_request.auto_draft`
//      (delivered by notification-dispatch.job.ts).
//   3. A failure in one tenant is logged and never stops the others.
//
// INTERNAL_API_BASE_URL points at api_core's loopback-only port — the public gateway blocks
// /api/internal/. Unset INTERNAL_JOB_KEY → job skipped (the endpoint is disabled server-side too).

import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

const INTERNAL_API_BASE = process.env.INTERNAL_API_BASE_URL ?? "http://localhost:5110";
const INTERNAL_JOB_KEY = process.env.INTERNAL_JOB_KEY ?? "";
const MIN_KEY_LENGTH = 32; // must match InternalJobKeyFilter.MinKeyLength
const REQUEST_TIMEOUT_MS = 120_000;

interface CandidateTenant {
  id: string;
  name: string;
}

interface RunResponse {
  tenantId: string;
  status: "completed" | "skipped" | "not_found";
  reason?: string | null;
  drafts: { id: string; storeLocationName: string; source: string; lineCount: number }[];
}

async function listCandidateTenants(): Promise<CandidateTenant[]> {
  const client = await db.connect();
  try {
    await client.query("BEGIN READ ONLY");
    // Worker role is needed to see locations across tenants (fail-closed RLS otherwise).
    // SET LOCAL: reverts at COMMIT/ROLLBACK, never leaks to the pool's next user.
    await client.query("SET LOCAL app.role = 'worker'");
    const res = await client.query<CandidateTenant>(
      `SELECT t."Id" AS id, t."Name" AS name
         FROM tenants t
        WHERE t."IsActive"
          AND EXISTS (SELECT 1 FROM jsonb_array_elements_text(t."Modules") m
                       WHERE lower(m) = 'production')
          AND EXISTS (SELECT 1 FROM locations l
                       WHERE l."TenantId" = t."Id" AND l."IsActive" AND l."Type" = 'production')
        ORDER BY t."Name"`,
    );
    await client.query("COMMIT");
    return res.rows;
  } catch (e) {
    await client.query("ROLLBACK").catch(() => undefined);
    throw e;
  } finally {
    client.release();
  }
}

async function runForTenant(tenant: CandidateTenant): Promise<"completed" | "skipped" | "failed"> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    const res = await fetch(`${INTERNAL_API_BASE}/api/internal/production-demand/run`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Internal-Job-Key": INTERNAL_JOB_KEY },
      body: JSON.stringify({ tenantId: tenant.id }),
      signal: controller.signal,
    });

    if (res.status === 200) {
      const body = (await res.json()) as RunResponse;
      for (const d of body.drafts) {
        console.log(
          `[production-demand] ${tenant.name} (${tenant.id}) · ${d.storeLocationName}: draft ${d.id} (${d.lineCount} lines, ${d.source})`,
        );
      }
      console.log(`[production-demand] ${tenant.name} (${tenant.id}): ${body.drafts.length} draft(s) created/updated`);
      return "completed";
    }

    const text = (await res.text()).slice(0, 200);
    if (res.status === 422 || res.status === 404) {
      console.log(`[production-demand] ${tenant.name} (${tenant.id}): skipped (HTTP ${res.status}) ${text}`);
      return "skipped";
    }
    console.warn(`[production-demand] ${tenant.name} (${tenant.id}): HTTP ${res.status} — ${text}`);
    return "failed";
  } catch (e) {
    console.error(`[production-demand] ${tenant.name} (${tenant.id}): ${(e as Error).message}`);
    return "failed";
  } finally {
    clearTimeout(timer);
  }
}

async function runProductionDemandAnalysis(): Promise<void> {
  if (INTERNAL_JOB_KEY.length < MIN_KEY_LENGTH) {
    console.warn("[production-demand] INTERNAL_JOB_KEY not set (or < 32 chars) — skipping");
    return;
  }

  const tenants = await listCandidateTenants();
  const totals = { completed: 0, skipped: 0, failed: 0 };
  // Sequential on purpose: one tenant's transaction at a time keeps DB load flat and makes the
  // per-tenant log readable. Each tenant is fully independent (own request, own server-side tx).
  for (const tenant of tenants) {
    totals[await runForTenant(tenant)]++;
  }
  console.log(
    `[production-demand] done: ${tenants.length} tenant(s) — ${totals.completed} completed, ${totals.skipped} skipped, ${totals.failed} failed`,
  );
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
