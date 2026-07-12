// TASK-342 / ADR-019 §4: Temporary permission grant expiry notifications.
//
// Cron: every 15 minutes ("*/15 * * * *") — matches the JWT refresh cadence already
// accepted as the propagation delay for effective-permission changes (ADR-019 §2).
//
// user_permission_grants (schema: TASK-341, backend/ShelfGuard.Domain/Entities/UserPermissionGrant.cs)
// has no C#-side writer for these two scans — the worker queries Postgres directly, same
// convention as expiry-check.job.ts/weekly-report.job.ts ("SET app.role = 'worker'" to run
// as the privileged service role across every tenant).
//
// Two scans per run:
//   1. Expiring within 24h, not yet revoked, not yet notified (NotifiedExpiringAt IS NULL)
//      → outbox row EventType = "access.temporary_expiring_soon".
//   2. Already past ExpiresAt, not yet revoked, not yet notified (NotifiedExpiredAt IS NULL)
//      → outbox row EventType = "access.temporary_expired".
//
// Both insert a TARGETED outbox row into notification_queue — UserId = the grant recipient
// (not a broadcast like receipt.created/supplier.message) — which notification-dispatch.job.ts
// now delivers straight to that user instead of doing role-matrix fan-out (see the
// `row.user_id` branch added there in this same task).

import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

const EXPIRING_SOON_WINDOW = "24 hours";
const BATCH_LIMIT = 200;

type ExpiringGrantRow = {
  id: string;
  tenant_id: string;
  user_id: string;
  permission_key: string;
  expires_at: string;
};

const PAGE_LABELS: Record<string, string> = {
  dashboard: "Дашборд",
  inventory: "Каталог товарів",
  stock: "Залишки",
  receipts: "Поставки",
  transfers: "Переміщення",
  "write-offs": "Списання",
  analytics: "Аналітика",
  users: "Користувачі",
  settings: "Налаштування",
};

function pageLabel(key: string): string {
  return PAGE_LABELS[key] ?? key;
}

// ── Scan 1: expiring within 24h ─────────────────────────────────────────────

async function processExpiringSoon(): Promise<number> {
  const client = await db.connect();
  try {
    await client.query("SET app.role = 'worker'");

    const { rows } = await client.query<ExpiringGrantRow>(
      `SELECT "Id"            AS id,
              "TenantId"      AS tenant_id,
              "UserId"        AS user_id,
              "PermissionKey" AS permission_key,
              "ExpiresAt"::text AS expires_at
       FROM user_permission_grants
       WHERE "RevokedAt" IS NULL
         AND "NotifiedExpiringAt" IS NULL
         AND "ExpiresAt" > NOW()
         AND "ExpiresAt" <= NOW() + INTERVAL '${EXPIRING_SOON_WINDOW}'
       ORDER BY "ExpiresAt" ASC
       LIMIT $1`,
      [BATCH_LIMIT]
    );

    for (const grant of rows) {
      const label = pageLabel(grant.permission_key);
      const payload = JSON.stringify({
        grantId: grant.id,
        permissionKey: grant.permission_key,
        expiresAt: grant.expires_at,
      });

      await client.query(
        `INSERT INTO notification_queue
           ("TenantId", "UserId", "Title", "Channel", "EventType", "Payload", "Status")
         VALUES ($1, $2, $3, 'system', 'access.temporary_expiring_soon', $4::jsonb, 'pending')`,
        [
          grant.tenant_id,
          grant.user_id,
          `Тимчасовий доступ «${label}» закінчується протягом доби`,
          payload,
        ]
      );

      await client.query(
        `UPDATE user_permission_grants SET "NotifiedExpiringAt" = NOW() WHERE "Id" = $1`,
        [grant.id]
      );
    }

    return rows.length;
  } finally {
    client.release();
  }
}

// ── Scan 2: already expired ─────────────────────────────────────────────────

async function processJustExpired(): Promise<number> {
  const client = await db.connect();
  try {
    await client.query("SET app.role = 'worker'");

    const { rows } = await client.query<ExpiringGrantRow>(
      `SELECT "Id"            AS id,
              "TenantId"      AS tenant_id,
              "UserId"        AS user_id,
              "PermissionKey" AS permission_key,
              "ExpiresAt"::text AS expires_at
       FROM user_permission_grants
       WHERE "RevokedAt" IS NULL
         AND "ExpiresAt" < NOW()
         AND "NotifiedExpiredAt" IS NULL
       ORDER BY "ExpiresAt" ASC
       LIMIT $1`,
      [BATCH_LIMIT]
    );

    for (const grant of rows) {
      const label = pageLabel(grant.permission_key);
      const payload = JSON.stringify({
        grantId: grant.id,
        permissionKey: grant.permission_key,
        expiresAt: grant.expires_at,
      });

      await client.query(
        `INSERT INTO notification_queue
           ("TenantId", "UserId", "Title", "Channel", "EventType", "Payload", "Status")
         VALUES ($1, $2, $3, 'system', 'access.temporary_expired', $4::jsonb, 'pending')`,
        [
          grant.tenant_id,
          grant.user_id,
          `Тимчасовий доступ «${label}» закінчився`,
          payload,
        ]
      );

      await client.query(
        `UPDATE user_permission_grants SET "NotifiedExpiredAt" = NOW() WHERE "Id" = $1`,
        [grant.id]
      );
    }

    return rows.length;
  } finally {
    client.release();
  }
}

async function runPermissionGrantExpiry(): Promise<void> {
  const expiringSoonCount = await processExpiringSoon();
  const expiredCount = await processJustExpired();

  if (expiringSoonCount > 0 || expiredCount > 0) {
    console.log(
      `[permission-grant-expiry] expiring-soon: ${expiringSoonCount}, just-expired: ${expiredCount}`
    );
  }
}

// ── Worker export ──────────────────────────────────────────────────────────

export function startPermissionGrantExpiryWorker(): Worker {
  const worker = new Worker(
    "permission-grant-expiry",
    async (job: Job) => {
      console.log(`[permission-grant-expiry] job ${job.id} started`);
      await runPermissionGrantExpiry();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("completed", (job) =>
    console.log(`[permission-grant-expiry] job ${job.id} completed`)
  );

  worker.on("failed", (job, err) =>
    console.error(`[permission-grant-expiry] job ${job?.id} failed:`, err.message)
  );

  return worker;
}
