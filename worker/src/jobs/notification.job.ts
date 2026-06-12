import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";
import { sendTelegramMessage } from "../services/telegram";
import { sendEmail } from "../services/email";
import { deliver, logNotifications, type DeliveryOutcome } from "../services/notification-log";

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

export type TempAlertPayload = {
  type: "temp_alert";
  tenantId: string;
  storeId: string;
  zoneId: string | null;
  deviceId: string;
  temperature: number;
  threshold: number | null;
};

export type IotOfflinePayload = {
  type: "iot_offline";
  tenantId: string;
  storeId: string;
  deviceId: string;
  deviceName: string;
};

type NotificationPayload = ExpiryAlertPayload | TempAlertPayload | IotOfflinePayload;

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
         cp."Name" AS product_name,
         s."Name"  AS store_name
       FROM catalog_products cp
       JOIN stores s ON s."Id" = $2
       WHERE cp."Id" = $1`,
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
      `SELECT "Id"             AS id,
              "Email"          AS email,
              "FullName"       AS full_name,
              "Role"           AS role,
              "TelegramChatId" AS telegram_chat_id,
              "PushToken"      AS push_token
       FROM users
       WHERE "TenantId" = $1
         AND "Role" = ANY($2::text[])
         AND "IsActive" = true
         AND ("TelegramChatId" IS NOT NULL OR "PushToken" IS NOT NULL OR "Email" IS NOT NULL)`,
      [payload.tenantId, roles]
    );

    if (usersRes.rows.length === 0) return;

    // Check notification_settings — only send if user has enabled this event+channel
    const settingsRes = await client.query<{ user_id: string; channel: string }>(
      `SELECT "UserId" AS user_id, "Channel" AS channel
       FROM notification_settings
       WHERE "UserId" = ANY($1::uuid[])
         AND "EventType" = 'product.' || $2
         AND "IsEnabled" = true`,
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
          console.error(`[notifications] ${o.channel} send failed for user ${user.id}: ${o.error}`);
        }
      }

      await logNotifications(client, {
        tenantId: payload.tenantId,
        userId: user.id,
        eventType: `product.${payload.status}`,
        payload,
        outcomes,
      });
    }
  } finally {
    client.release();
  }
}

// ── IoT alerts (v3-spec §4) ────────────────────────────────────────────────

// Send a plain telegram alert to the given roles and log per-channel outcomes
async function handleIotAlert(
  tenantId: string,
  storeId: string,
  roles: string[],
  eventType: string,
  text: string,
  payload: unknown
): Promise<void> {
  const client = await db.connect();
  try {
    const storeRes = await client.query<{ name: string }>(
      `SELECT "Name" AS name FROM stores WHERE "Id" = $1`, [storeId]);
    const storeName = storeRes.rows[0]?.name ?? "Невідомий магазин";
    const fullText = `${text}\n<b>Магазин:</b> ${storeName}`;

    const usersRes = await client.query<{ id: string; telegram_chat_id: string | null }>(
      `SELECT "Id" AS id, "TelegramChatId" AS telegram_chat_id
       FROM users
       WHERE "TenantId" = $1 AND "Role" = ANY($2::text[]) AND "IsActive" = true`,
      [tenantId, roles]
    );

    for (const user of usersRes.rows) {
      const outcome = user.telegram_chat_id
        ? await deliver("telegram", () => sendTelegramMessage(user.telegram_chat_id!, fullText))
        : { channel: "telegram", status: "skipped" as const, error: "no telegram_chat_id" };

      if (outcome.status === "failed") {
        console.error(`[notifications] ${eventType} telegram failed for user ${user.id}: ${outcome.error}`);
      }
      await logNotifications(client, {
        tenantId, userId: user.id, eventType, payload, outcomes: [outcome],
      });
    }
  } finally {
    client.release();
  }
}

function handleTempAlert(p: TempAlertPayload): Promise<void> {
  const text =
    `🌡 <b>ShelfGuard — ТЕМПЕРАТУРНИЙ АЛЕРТ</b>\n\n` +
    `<b>Температура:</b> ${p.temperature}°C (поріг ${p.threshold ?? "—"}°C)`;
  return handleIotAlert(
    p.tenantId, p.storeId,
    ["store_manager", "network_manager", "enterprise_admin"],
    "iot.temp_alert", text, p
  );
}

function handleIotOffline(p: IotOfflinePayload): Promise<void> {
  const text =
    `📵 <b>ShelfGuard — пристрій офлайн</b>\n\n` +
    `<b>Пристрій:</b> ${p.deviceName}\n` +
    `Немає даних понад 30 хвилин.`;
  return handleIotAlert(p.tenantId, p.storeId, ["store_manager"], "iot.offline", text, p);
}

// ── Worker ─────────────────────────────────────────────────────────────────

export function startNotificationWorker(): Worker {
  const worker = new Worker(
    "notifications",
    async (job: Job<NotificationPayload>) => {
      console.log(`[notifications] job ${job.id} type=${job.data.type}`);

      if (job.data.type === "expiry_alert") {
        await handleExpiryAlert(job.data);
      } else if (job.data.type === "temp_alert") {
        await handleTempAlert(job.data);
      } else if (job.data.type === "iot_offline") {
        await handleIotOffline(job.data);
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
