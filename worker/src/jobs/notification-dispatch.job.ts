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

import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";
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

type PendingIntentRow = {
  id: string;
  tenant_id: string;
  store_id: string | null;
  title: string | null;
  event_type: string;
  payload: unknown;
};

function formatText(row: PendingIntentRow): string {
  const icons: Record<string, string> = {
    "receipt.created": "📦",
    "supplier.message": "💬",
    "supplier_agreement.signed": "✍️",
  };
  const icon = icons[row.event_type] ?? "🔔";
  return `${icon} <b>ShelfGuard</b>\n\n${row.title ?? "Нове сповіщення"}`;
}

function formatEmail(row: PendingIntentRow): { subject: string; html: string } {
  const subject = `[ShelfGuard] ${row.title ?? "Нове сповіщення"}`;
  const html = `
    <h2>ShelfGuard</h2>
    <p>${row.title ?? "Нове сповіщення"}</p>
    <p style="color:#888;font-size:12px">ShelfGuard — система управління термінами придатності</p>
  `;
  return { subject, html };
}

async function dispatchOne(row: PendingIntentRow): Promise<void> {
  const client = await db.connect();
  try {
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
    const res = await client.query<PendingIntentRow>(
      `SELECT "Id"         AS id,
              "TenantId"   AS tenant_id,
              "LocationId" AS store_id,
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
