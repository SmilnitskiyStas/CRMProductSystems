import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";
import { sendTelegramMessage } from "../services/telegram";
import { sendEmail } from "../services/email";

// ── Payload types ──────────────────────────────────────────────────────────

export type ExpiryAlertPayload = {
  type: "expiry_alert";
  tenantId: string;
  storeId: string;
  productId: string;
  batchNumber: string | null;
  status: "warning" | "critical" | "expired";
  daysLeft: number;
  quantity: number;
};

type NotificationPayload = ExpiryAlertPayload;

// ── Role → event subscription matrix (from v1-spec section 8.2) ────────────

const EXPIRY_EVENT_ROLES: Record<
  ExpiryAlertPayload["status"],
  { roles: string[]; channels: string[] }
> = {
  warning:  { roles: ["merchandiser", "store_manager", "network_manager", "enterprise_admin"], channels: ["telegram", "push"] },
  critical: { roles: ["merchandiser", "store_manager", "network_manager", "enterprise_admin"], channels: ["telegram", "push", "email"] },
  expired:  { roles: ["store_manager", "network_manager", "enterprise_admin"], channels: ["telegram", "email"] },
};

// ── Message formatters ─────────────────────────────────────────────────────

function formatExpiryText(p: ExpiryAlertPayload, productName: string, storeName: string): string {
  const icons: Record<string, string> = { warning: "⚠️", critical: "🔴", expired: "💀" };
  const icon  = icons[p.status] ?? "❗";
  const daysText =
    p.daysLeft < 0  ? `протермінований на ${Math.abs(p.daysLeft)} д.` :
    p.daysLeft === 0 ? "закінчується сьогодні" :
    `залишилось ${p.daysLeft} д.`;

  return (
    `${icon} <b>ShelfGuard — ${p.status.toUpperCase()}</b>\n\n` +
    `<b>Товар:</b> ${productName}\n` +
    `<b>Магазин:</b> ${storeName}\n` +
    `<b>Партія:</b> ${p.batchNumber ?? "—"}\n` +
    `<b>Кількість:</b> ${p.quantity}\n` +
    `<b>Термін:</b> ${daysText}`
  );
}

function formatExpiryEmail(p: ExpiryAlertPayload, productName: string, storeName: string): { subject: string; html: string } {
  const labels: Record<string, string> = { warning: "Попередження", critical: "КРИТИЧНО", expired: "ПРОТЕРМІНОВАНО" };
  const daysText =
    p.daysLeft < 0  ? `протерміновано на ${Math.abs(p.daysLeft)} д.` :
    p.daysLeft === 0 ? "закінчується сьогодні" :
    `залишилось ${p.daysLeft} д.`;

  const subject = `[ShelfGuard] ${labels[p.status] ?? p.status}: ${productName}`;
  const html = `
    <h2>ShelfGuard — сповіщення про термін придатності</h2>
    <table>
      <tr><td><b>Статус</b></td><td>${labels[p.status] ?? p.status}</td></tr>
      <tr><td><b>Товар</b></td><td>${productName}</td></tr>
      <tr><td><b>Магазин</b></td><td>${storeName}</td></tr>
      <tr><td><b>Партія</b></td><td>${p.batchNumber ?? "—"}</td></tr>
      <tr><td><b>Кількість</b></td><td>${p.quantity}</td></tr>
      <tr><td><b>Термін</b></td><td>${daysText}</td></tr>
    </table>
    <p style="color:#888;font-size:12px">ShelfGuard — система управління термінами придатності</p>
  `;
  return { subject, html };
}

// ── Main handler ───────────────────────────────────────────────────────────

async function handleExpiryAlert(payload: ExpiryAlertPayload): Promise<void> {
  const { roles, channels } = EXPIRY_EVENT_ROLES[payload.status] ?? { roles: [], channels: [] };
  if (roles.length === 0) return;

  const client = await db.connect();
  try {
    // Fetch product name and store name for readable messages
    const metaRes = await client.query<{ product_name: string; store_name: string }>(
      `SELECT
         cp.name  AS product_name,
         s.name   AS store_name
       FROM catalog_products cp
       JOIN stores s ON s.id = $2
       WHERE cp.id = $1`,
      [payload.productId, payload.storeId]
    );
    const productName = metaRes.rows[0]?.product_name ?? "Невідомий товар";
    const storeName   = metaRes.rows[0]?.store_name   ?? "Невідомий магазин";

    // Fetch target users: correct tenant, correct role, active, with at least one contact
    const usersRes = await client.query<{
      id: string;
      email: string;
      full_name: string;
      role: string;
      telegram_chat_id: string | null;
      push_token: string | null;
    }>(
      `SELECT id, email, full_name, role, telegram_chat_id, push_token
       FROM users
       WHERE tenant_id = $1
         AND role = ANY($2::text[])
         AND is_active = true
         AND (telegram_chat_id IS NOT NULL OR push_token IS NOT NULL OR email IS NOT NULL)`,
      [payload.tenantId, roles]
    );

    if (usersRes.rows.length === 0) return;

    // Check notification_settings — only send if user has enabled this event+channel
    const settingsRes = await client.query<{ user_id: string; channel: string }>(
      `SELECT user_id, channel
       FROM notification_settings
       WHERE user_id = ANY($1::uuid[])
         AND event_type = 'product.' || $2
         AND is_enabled = true`,
      [usersRes.rows.map((u) => u.id), payload.status]
    );

    const enabledMap = new Map<string, Set<string>>();
    for (const row of settingsRes.rows) {
      if (!enabledMap.has(row.user_id)) enabledMap.set(row.user_id, new Set());
      enabledMap.get(row.user_id)!.add(row.channel);
    }

    const telegramText = formatExpiryText(payload, productName, storeName);
    const emailContent = formatExpiryEmail(payload, productName, storeName);

    for (const user of usersRes.rows) {
      // If user has explicit settings, respect them; if no settings row → apply role defaults
      const userChannels = enabledMap.get(user.id) ?? new Set(channels);

      const sendPromises: Promise<void>[] = [];

      if (userChannels.has("telegram") && user.telegram_chat_id && channels.includes("telegram")) {
        sendPromises.push(
          sendTelegramMessage(user.telegram_chat_id, telegramText).catch((e) =>
            console.error(`[notifications] telegram send failed for user ${user.id}:`, e.message)
          )
        );
      }

      if (userChannels.has("email") && channels.includes("email")) {
        sendPromises.push(
          sendEmail({ to: user.email, ...emailContent }).catch((e) =>
            console.error(`[notifications] email send failed for user ${user.id}:`, e.message)
          )
        );
      }

      await Promise.all(sendPromises);

      // Log to notification_queue
      await client.query(
        `INSERT INTO notification_queue
           (tenant_id, user_id, channel, event_type, payload, status, sent_at)
         VALUES ($1, $2, $3, $4, $5::jsonb, 'sent', NOW())`,
        [
          payload.tenantId,
          user.id,
          Array.from(userChannels).join(","),
          `product.${payload.status}`,
          JSON.stringify(payload),
        ]
      );
    }
  } finally {
    client.release();
  }
}

// ── Worker ─────────────────────────────────────────────────────────────────

export function startNotificationWorker(): Worker {
  const worker = new Worker(
    "notifications",
    async (job: Job<NotificationPayload>) => {
      console.log(`[notifications] job ${job.id} type=${job.data.type}`);

      if (job.data.type === "expiry_alert") {
        await handleExpiryAlert(job.data);
      } else {
        console.warn(`[notifications] unknown job type: ${(job.data as any).type}`);
      }
    },
    {
      connection: redisConnection,
      concurrency: 5,
    }
  );

  worker.on("completed", (job) => {
    console.log(`[notifications] job ${job.id} completed`);
  });

  worker.on("failed", (job, err) => {
    console.error(`[notifications] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
