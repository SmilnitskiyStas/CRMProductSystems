// TASK-339 / ADR-018 §2: Postgres outbox dispatch job.
//
// Cron: every minute ("* * * * *"), same shape as fiscalization-retry.job.ts.
//
// Three backend (ASP.NET Core) services — ReceiptService.ReceiveAsync,
// SupplierChatService.SendMessageAsync, SupplierAgreementService.MarkSignedAsync — have no
// BullMQ producer of their own, so instead they insert a broadcast-intent row directly into
// notification_queue via INotificationRepository.EnqueueAsync: UserId = null,
// Channel = "system", Status = "pending", with Title/StoreId/Payload/EventType populated.
//
// This job polls those rows, resolves recipients by role (same role-matrix + notification_settings
// pattern as handleExpiryAlert/handleIotAlert in notification.job.ts), delivers, writes real
// per-user×channel rows via the existing logNotifications, then marks the intent row
// Status = 'dispatched' (terminal — excluded from GetHistoryAsync's Channel <> 'system' filter
// regardless, but 'dispatched' also keeps it out of future polls).
//
// TASK-342 / ADR-019 §5: permission-grant-expiry.job.ts enqueues a SECOND kind of outbox
// row — UserId IS NOT NULL (the grant recipient specifically, not a role broadcast). Those
// rows skip the role-matrix entirely and are delivered straight to that one user (their own
// notification_settings for the event type still apply). See dispatchTargeted() below.

import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";
import type { PoolClient } from "pg";
import { sendTelegramMessage } from "../services/telegram";
import { sendEmail } from "../services/email";
import { deliver, logNotifications, type DeliveryOutcome } from "../services/notification-log";

const BATCH_LIMIT = 50;

// ── Role → event subscription matrix for outbox-originated events ──────────
// Judgment call (CLAUDE.md: objective-best-practice, no product sign-off needed) — mirrors
// the shape of EXPIRY_EVENT_ROLES in notification.job.ts. Revisit if product wants a
// narrower/broader audience per event.
const DISPATCH_EVENT_ROLES: Record<string, { roles: string[]; channels: string[] }> = {
  "receipt.created": {
    roles: ["merchandiser", "store_manager", "network_manager", "enterprise_admin"],
    channels: ["telegram", "push"],
  },
  "supplier.message": {
    roles: ["store_manager", "network_manager", "enterprise_admin"],
    channels: ["telegram", "push"],
  },
  "supplier_agreement.signed": {
    roles: ["network_manager", "enterprise_admin"],
    channels: ["telegram", "email"],
  },
};

// TASK-342 / ADR-019 §5: default channel set for targeted (UserId IS NOT NULL) outbox rows,
// keyed by event type — no role matrix involved, the recipient is already fixed.
const TARGETED_EVENT_CHANNELS: Record<string, string[]> = {
  "access.temporary_expiring_soon": ["telegram", "push"],
  "access.temporary_expired": ["telegram", "push"],
  // TASK-456: forgot/reset-password link. No "push" — deliberate, unlike the access.* rows
  // above (push delivery isn't implemented at all yet; email/Telegram are the only channels
  // that can actually carry a clickable link today).
  "auth.password_reset_requested": ["email", "telegram"],
};

// TASK-456: shape of the outbox row's Payload jsonb for auth.password_reset_requested — `pg`
// auto-parses jsonb columns, so row.payload already arrives as a JS object, not a string.
type PasswordResetPayload = { resetUrl?: string; expiresInMinutes?: number };

type PendingIntentRow = {
  id: string;
  tenant_id: string;
  store_id: string | null;
  user_id: string | null;
  title: string | null;
  event_type: string;
  payload: unknown;
};

function formatText(row: PendingIntentRow): string {
  if (row.event_type === "auth.password_reset_requested") {
    const payload = row.payload as PasswordResetPayload | null;
    const resetUrl = payload?.resetUrl ?? "";
    const expiresInMinutes = payload?.expiresInMinutes ?? 30;
    return (
      `🔑 <b>ShelfGuard</b>\n\n` +
      `Хтось (можливо ви) запросив відновлення пароля. Перейдіть за посиланням протягом ` +
      `${expiresInMinutes} хвилин: <a href="${resetUrl}">${resetUrl}</a>\n\n` +
      `Якщо це не ви — проігноруйте це повідомлення.`
    );
  }

  const icons: Record<string, string> = {
    "receipt.created": "📦",
    "supplier.message": "💬",
    "supplier_agreement.signed": "✍️",
    "access.temporary_expiring_soon": "⏳",
    "access.temporary_expired": "🔒",
  };
  const icon = icons[row.event_type] ?? "🔔";
  return `${icon} <b>ShelfGuard</b>\n\n${row.title ?? "Нове сповіщення"}`;
}

function formatEmail(row: PendingIntentRow): { subject: string; html: string } {
  if (row.event_type === "auth.password_reset_requested") {
    const payload = row.payload as PasswordResetPayload | null;
    const resetUrl = payload?.resetUrl ?? "";
    const expiresInMinutes = payload?.expiresInMinutes ?? 30;
    const subject = "[ShelfGuard] Відновлення пароля";
    const html = `
      <h2>ShelfGuard</h2>
      <p>Хтось (можливо ви) запросив відновлення пароля. Перейдіть за посиланням протягом ${expiresInMinutes} хвилин:</p>
      <p><a href="${resetUrl}">${resetUrl}</a></p>
      <p>Якщо це не ви — проігноруйте цей лист.</p>
      <p style="color:#888;font-size:12px">ShelfGuard — система управління термінами придатності</p>
    `;
    return { subject, html };
  }

  const subject = `[ShelfGuard] ${row.title ?? "Нове сповіщення"}`;
  const html = `
    <h2>ShelfGuard</h2>
    <p>${row.title ?? "Нове сповіщення"}</p>
    <p style="color:#888;font-size:12px">ShelfGuard — система управління термінами придатності</p>
  `;
  return { subject, html };
}

// TASK-342 / ADR-019 §5: targeted delivery — the outbox row already names its one
// recipient (UserId), so skip the role matrix and deliver straight to them. Their own
// notification_settings for the event type still gate each channel, same as the
// role-broadcast path; no settings row → fall back to TARGETED_EVENT_CHANNELS defaults.
async function dispatchTargeted(
  client: PoolClient,
  row: PendingIntentRow & { user_id: string }
): Promise<void> {
  const defaultChannels = TARGETED_EVENT_CHANNELS[row.event_type];
  if (!defaultChannels) {
    console.warn(
      `[notification-dispatch] targeted intent ${row.id}: unknown eventType ${row.event_type} — marking dispatched`
    );
    await client.query(`UPDATE notification_queue SET "Status" = 'dispatched' WHERE "Id" = $1`, [row.id]);
    return;
  }

  const userRes = await client.query<{
    id: string;
    email: string;
    telegram_chat_id: string | null;
    push_token: string | null;
  }>(
    `SELECT "Id" AS id, "Email" AS email, "TelegramChatId" AS telegram_chat_id, "PushToken" AS push_token
     FROM users
     WHERE "Id" = $1 AND "IsActive" = true`,
    [row.user_id]
  );

  const user = userRes.rows[0];
  if (!user) {
    await client.query(`UPDATE notification_queue SET "Status" = 'dispatched' WHERE "Id" = $1`, [row.id]);
    return;
  }

  const settingsRes = await client.query<{ channel: string }>(
    `SELECT "Channel" AS channel
     FROM notification_settings
     WHERE "UserId" = $1 AND "EventType" = $2 AND "IsEnabled" = true`,
    [user.id, row.event_type]
  );

  // Explicit settings win; no settings row → fall back to event defaults (same rule as
  // the role-broadcast path's enabledMap ?? channels fallback).
  const userChannels = settingsRes.rows.length > 0
    ? new Set(settingsRes.rows.map((r) => r.channel))
    : new Set(defaultChannels);
  const activeChannels = Array.from(userChannels).filter((c) => defaultChannels.includes(c));

  if (activeChannels.length === 0) {
    await client.query(`UPDATE notification_queue SET "Status" = 'dispatched' WHERE "Id" = $1`, [row.id]);
    return;
  }

  const telegramText = formatText(row);
  const emailContent = formatEmail(row);

  const outcomes: DeliveryOutcome[] = [];
  for (const channel of activeChannels) {
    if (channel === "telegram") {
      outcomes.push(
        user.telegram_chat_id
          ? await deliver("telegram", () => sendTelegramMessage(user.telegram_chat_id!, telegramText))
          : { channel: "telegram", status: "skipped", error: "no telegram_chat_id" }
      );
    } else if (channel === "email") {
      outcomes.push(
        user.email
          ? await deliver("email", () => sendEmail({ to: user.email, ...emailContent }))
          : { channel: "email", status: "skipped", error: "no email" }
      );
    } else if (channel === "push") {
      outcomes.push({ channel: "push", status: "skipped", error: "push channel not implemented" });
    }
  }

  for (const o of outcomes) {
    if (o.status === "failed") {
      console.error(`[notification-dispatch] targeted ${o.channel} send failed for user ${user.id}: ${o.error}`);
    }
  }

  // TASK-460 (HIGH, TASK-458 security review): logNotifications() persists `payload` verbatim
  // into notification_queue rows that GET /api/notifications/history returns to ANY
  // authenticated same-tenant user (no per-user scoping there) — for auth.password_reset_requested
  // that payload is a live, unhashed, single-use reset token, so logging it verbatim turns a
  // routine history read into an account-takeover primitive. Redact before logging only; the
  // real resetUrl was already used above (formatText/formatEmail) for the actual send. Any future
  // targeted event whose payload carries a bearer secret should redact the same way here.
  const logPayload =
    row.event_type === "auth.password_reset_requested"
      ? { expiresInMinutes: (row.payload as PasswordResetPayload | null)?.expiresInMinutes ?? 30 }
      : row.payload;

  await logNotifications(client, {
    tenantId: row.tenant_id,
    userId: user.id,
    eventType: row.event_type,
    payload: logPayload,
    outcomes,
  });

  await client.query(`UPDATE notification_queue SET "Status" = 'dispatched' WHERE "Id" = $1`, [row.id]);
}

async function dispatchOne(row: PendingIntentRow): Promise<void> {
  const client = await db.connect();
  try {
    // 2026-07-14 (Block 2 pre-launch audit follow-up): notification_queue's tenant_isolation
    // no longer has a fail-open branch for RESET-state connections (see
    // 20260714180000_FixFailOpenTenantIsolationOnReset — the old branch was a P0 cross-tenant
    // leak). This job never set app.tenant_id, so it needs worker_bypass instead — same
    // pattern as every other worker/src/jobs/*.job.ts.
    await client.query("SET app.role = 'worker'");

    // Targeted rows (UserId set) bypass the role matrix entirely — deliver to that one user.
    if (row.user_id) {
      await dispatchTargeted(client, row as PendingIntentRow & { user_id: string });
      return;
    }

    const matrix = DISPATCH_EVENT_ROLES[row.event_type];
    if (!matrix) {
      console.warn(`[notification-dispatch] intent ${row.id}: unknown eventType ${row.event_type} — marking dispatched`);
      await client.query(`UPDATE notification_queue SET "Status" = 'dispatched' WHERE "Id" = $1`, [row.id]);
      return;
    }

    const { roles, channels } = matrix;

    const usersRes = await client.query<{
      id: string;
      email: string;
      telegram_chat_id: string | null;
      push_token: string | null;
    }>(
      `SELECT "Id"             AS id,
              "Email"          AS email,
              "TelegramChatId" AS telegram_chat_id,
              "PushToken"      AS push_token
       FROM users
       WHERE "TenantId" = $1
         AND "Role" = ANY($2::text[])
         AND "IsActive" = true
         AND ("TelegramChatId" IS NOT NULL OR "PushToken" IS NOT NULL OR "Email" IS NOT NULL)`,
      [row.tenant_id, roles]
    );

    if (usersRes.rows.length === 0) {
      await client.query(`UPDATE notification_queue SET "Status" = 'dispatched' WHERE "Id" = $1`, [row.id]);
      return;
    }

    const settingsRes = await client.query<{ user_id: string; channel: string }>(
      `SELECT "UserId" AS user_id, "Channel" AS channel
       FROM notification_settings
       WHERE "UserId" = ANY($1::uuid[])
         AND "EventType" = $2
         AND "IsEnabled" = true`,
      [usersRes.rows.map((u) => u.id), row.event_type]
    );

    const enabledMap = new Map<string, Set<string>>();
    for (const s of settingsRes.rows) {
      if (!enabledMap.has(s.user_id)) enabledMap.set(s.user_id, new Set());
      enabledMap.get(s.user_id)!.add(s.channel);
    }

    const telegramText = formatText(row);
    const emailContent = formatEmail(row);

    for (const user of usersRes.rows) {
      // Explicit settings win; no settings row → fall back to role defaults.
      const userChannels = enabledMap.get(user.id) ?? new Set(channels);
      const activeChannels = Array.from(userChannels).filter((c) => channels.includes(c));

      const outcomes: DeliveryOutcome[] = [];
      for (const channel of activeChannels) {
        if (channel === "telegram") {
          outcomes.push(
            user.telegram_chat_id
              ? await deliver("telegram", () => sendTelegramMessage(user.telegram_chat_id!, telegramText))
              : { channel: "telegram", status: "skipped", error: "no telegram_chat_id" }
          );
        } else if (channel === "email") {
          outcomes.push(
            user.email
              ? await deliver("email", () => sendEmail({ to: user.email, ...emailContent }))
              : { channel: "email", status: "skipped", error: "no email" }
          );
        } else if (channel === "push") {
          outcomes.push({ channel: "push", status: "skipped", error: "push channel not implemented" });
        }
      }
      if (outcomes.length === 0) continue;

      for (const o of outcomes) {
        if (o.status === "failed") {
          console.error(`[notification-dispatch] ${o.channel} send failed for user ${user.id}: ${o.error}`);
        }
      }

      await logNotifications(client, {
        tenantId: row.tenant_id,
        userId: user.id,
        eventType: row.event_type,
        payload: row.payload,
        outcomes,
      });
    }

    await client.query(`UPDATE notification_queue SET "Status" = 'dispatched' WHERE "Id" = $1`, [row.id]);
  } finally {
    client.release();
  }
}

async function runNotificationDispatch(): Promise<void> {
  const client = await db.connect();
  let pending: PendingIntentRow[];
  try {
    // See dispatchOne() above for why this SET is now required.
    await client.query("SET app.role = 'worker'");

    const res = await client.query<PendingIntentRow>(
      `SELECT "Id"         AS id,
              "TenantId"   AS tenant_id,
              "LocationId" AS store_id,
              "UserId"     AS user_id,
              "Title"      AS title,
              "EventType"  AS event_type,
              "Payload"    AS payload
       FROM notification_queue
       WHERE "Status" = 'pending' AND "Channel" = 'system'
       ORDER BY "CreatedAt" ASC
       LIMIT $1`,
      [BATCH_LIMIT]
    );
    pending = res.rows;
  } finally {
    client.release();
  }

  if (pending.length === 0) return;

  console.log(`[notification-dispatch] found ${pending.length} pending outbox intent(s)`);

  for (const row of pending) {
    try {
      await dispatchOne(row);
    } catch (e) {
      console.error(`[notification-dispatch] intent ${row.id} failed: ${(e as Error).message}`);
    }
  }
}

export function startNotificationDispatchWorker(): Worker {
  const worker = new Worker(
    "notification-dispatch",
    async (job: Job) => {
      console.log(`[notification-dispatch] job ${job.id} started`);
      await runNotificationDispatch();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("completed", (job) => console.log(`[notification-dispatch] job ${job.id} completed`));
  worker.on("failed", (job, err) => console.error(`[notification-dispatch] job ${job?.id} failed:`, err.message));

  return worker;
}
