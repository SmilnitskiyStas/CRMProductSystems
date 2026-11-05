// TASK-755: automatic supplier-order DRAFTS for non-own-production items, PER TENANT.
//
// Cron: daily 07:00. Same isolation pattern as production-demand.job.ts (TASK-754): candidate
// tenants are listed straight from Postgres (worker role, READ ONLY tx, SET LOCAL), then ONE
// request per tenant to POST {INTERNAL_API_BASE_URL}/api/internal/replenishment-drafts/run
// { tenantId } with X-Internal-Job-Key. The server runs the calculation inside a transaction
// bound to exactly that tenant's RLS context, creates/refreshes untaken auto DRAFT stock receipts
// (one per store x supplier) and enqueues `order.auto_draft` (delivered by notification-dispatch).
// A failure in one tenant never stops the others. Unset INTERNAL_JOB_KEY -> job skipped.

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
  drafts: { id: string; storeLocationName: string; supplierName: string | null; lineCount: number }[];
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
          AND EXISTS (SELECT 1 FROM locations l
                       WHERE l."TenantId" = t."Id" AND l."IsActive" AND l."Type" <> 'production')
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
    const res = await fetch(`${INTERNAL_API_BASE}/api/internal/replenishment-drafts/run`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Internal-Job-Key": INTERNAL_JOB_KEY },
      body: JSON.stringify({ tenantId: tenant.id }),
      signal: controller.signal,
    });

    if (res.status === 200) {
      const body = (await res.json()) as RunResponse;
      for (const d of body.drafts) {
        console.log(
          `[replenishment-drafts] ${tenant.name} (${tenant.id}) · ${d.storeLocationName}: draft ${d.id} · ${d.supplierName ?? "без постачальника"} (${d.lineCount} lines)`,
        );
      }
      console.log(`[replenishment-drafts] ${tenant.name} (${tenant.id}): ${body.drafts.length} draft(s) created/updated`);
      return "completed";
    }

    const text = (await res.text()).slice(0, 200);
    if (res.status === 422 || res.status === 404) {
      console.log(`[replenishment-drafts] ${tenant.name} (${tenant.id}): skipped (HTTP ${res.status}) ${text}`);
      return "skipped";
    }
    console.warn(`[replenishment-drafts] ${tenant.name} (${tenant.id}): HTTP ${res.status} — ${text}`);
    return "failed";
  } catch (e) {
    console.error(`[replenishment-drafts] ${tenant.name} (${tenant.id}): ${(e as Error).message}`);
    return "failed";
  } finally {
    clearTimeout(timer);
  }
}

async function runReplenishmentDrafts(): Promise<void> {
  if (INTERNAL_JOB_KEY.length < MIN_KEY_LENGTH) {
    console.warn("[replenishment-drafts] INTERNAL_JOB_KEY not set (or < 32 chars) — skipping");
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
    `[replenishment-drafts] done: ${tenants.length} tenant(s) — ${totals.completed} completed, ${totals.skipped} skipped, ${totals.failed} failed`,
  );
}

export function startReplenishmentDraftsWorker(): Worker {
  const worker = new Worker(
    "replenishment-drafts",
    async (job: Job) => {
      console.log(`[replenishment-drafts] job ${job.id} started`);
      await runReplenishmentDrafts();
    },
    { connection: redisConnection, concurrency: 1 },
  );

  worker.on("completed", (job) => console.log(`[replenishment-drafts] job ${job.id} completed`));
  worker.on("failed", (job, err) => console.error(`[replenishment-drafts] job ${job?.id} failed:`, err.message));

  return worker;
}
