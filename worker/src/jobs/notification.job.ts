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

export type ShelfEmptyPayload = {
  type: "shelf_empty";
  scanId: string;
  tenantId: string;
  storeId: string;
  zoneId: string;
  missingCount: number;
  lowCount: number;
  missingItems: string[];
  lowItems: string[];
  summary: string | null;
};

export type PromoNoSalesPayload = {
  type: "promo_no_sales";
  tenantId: string;
  storeId: string;
  storeName: string;
  items: {
    productName: string;
    promoName: string;
    storeStock: number;
    unitsSold: number;
    expectedUnits: number | null;
    days: number;
    reason: "no_sales" | "low_sales";
  }[];
};

type NotificationPayload =
  | ExpiryAlertPayload | TempAlertPayload | IotOfflinePayload | ShelfEmptyPayload | PromoNoSalesPayload;

// ── EventType keys must match backend ValidEventTypes and frontend types.ts ──

const STATUS_TO_EVENT_TYPE: Record<ExpiryAlertPayload["status"], string> = {
  warning:  "stock.expiry_warning",
  critical: "stock.expiry_critical",
  expired:  "stock.expired",
};

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
    // Block 11 audit: this consumer never set app.role='worker', unlike expiry-check.job.ts
    // (the producer that enqueues onto this same "notifications" queue) and every other
    // worker/src/jobs/*.ts file. Under the post-Block-2 fail-closed RLS policy
    // (20260714180000_FixFailOpenTenantIsolationOnReset), the queries below against
    // items/locations/users/notification_settings return zero rows on any pooled connection
    // that hasn't incidentally inherited app.role from another job reusing the same physical
    // pg connection (node-pg does not reset session state on release) — correctness was
    // depending on connection-pool luck, not a guarantee. Explicit SET fixes it properly.
    await client.query("SET app.role = 'worker'");

    // Fetch product name and store name for readable messages
    // NOTE (TASK-360 / Block 9 audit): "catalog_products" was renamed to "items" in
    // 20260616042437_V4ItemsRename and "stores" was renamed to "locations" in
    // 20260615183318_V4LocationsRename. Querying the old names threw "relation does not
    // exist" on every call — every expiry warning/critical/expired Telegram/push/email
    // notification has silently failed since those migrations (same bug class as TASK-358's
    // ai-order.job.ts/weather-fetch.job.ts finding).
    const metaRes = await client.query<{ product_name: string; store_name: string }>(
      `SELECT
         i."Name" AS product_name,
         s."Name" AS store_name
       FROM items i
       JOIN locations s ON s."Id" = $2
       WHERE i."Id" = $1`,
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
    const eventType = STATUS_TO_EVENT_TYPE[payload.status];
    const settingsRes = await client.query<{ user_id: string; channel: string }>(
      `SELECT "UserId" AS user_id, "Channel" AS channel
       FROM notification_settings
       WHERE "UserId" = ANY($1::uuid[])
         AND "EventType" = $2
         AND "IsEnabled" = true`,
      [usersRes.rows.map((u) => u.id), eventType]
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
        eventType,
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
    // Block 11 audit: see the SET app.role='worker' note in handleExpiryAlert above — same
    // missing-SET bug, same fix, this is the temp_alert/iot_offline delivery path (v3-spec §4).
    await client.query("SET app.role = 'worker'");

    // NOTE (TASK-360): "stores" renamed to "locations" — see comment in handleExpiryAlert above.
    const storeRes = await client.query<{ name: string }>(
      `SELECT "Name" AS name FROM locations WHERE "Id" = $1`, [storeId]);
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

// ── Shelf photo check (shelf.empty) ────────────────────────────────────────

const SHELF_EMPTY_ROLES = ["merchandiser", "store_manager", "network_manager", "enterprise_admin"];
const SHELF_EMPTY_DEFAULT_CHANNELS = ["telegram", "email"];

function escapeHtml(s: string): string {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

function itemList(names: string[], max = 15): string {
  const shown = names.slice(0, max).map(escapeHtml).join(", ");
  return names.length > max ? `${shown} і ще ${names.length - max}` : shown;
}

export function formatShelfEmptyText(p: ShelfEmptyPayload, storeName: string, zoneName: string): string {
  const lines: string[] = [
    "🛒 <b>ShelfGuard — порожня полиця</b>",
    "",
    `<b>Магазин:</b> ${escapeHtml(storeName)}`,
    `<b>Зона:</b> ${escapeHtml(zoneName)}`,
  ];
  if (p.missingItems.length > 0) lines.push(`<b>Відсутні (${p.missingCount}):</b> ${itemList(p.missingItems)}`);
  if (p.lowItems.length > 0) lines.push(`<b>Закінчуються (${p.lowCount}):</b> ${itemList(p.lowItems)}`);
  if (p.missingItems.length === 0 && p.lowItems.length === 0) {
    lines.push(`<b>Відсутні:</b> ${p.missingCount}, <b>закінчуються:</b> ${p.lowCount}`);
  }
  if (p.summary) lines.push("", escapeHtml(p.summary));
  return lines.join("\n");
}

async function handleShelfEmpty(p: ShelfEmptyPayload): Promise<void> {
  const eventType = "shelf.empty";
  const client = await db.connect();
  try {
    await client.query("SET app.role = 'worker'");

    const metaRes = await client.query<{ store_name: string; zone_name: string }>(
      `SELECT l."Name" AS store_name, z."Name" AS zone_name
       FROM locations l
       LEFT JOIN location_zones z ON z."Id" = $2
       WHERE l."Id" = $1`,
      [p.storeId, p.zoneId]
    );
    const storeName = metaRes.rows[0]?.store_name ?? "Невідомий магазин";
    const zoneName  = metaRes.rows[0]?.zone_name  ?? "—";

    const usersRes = await client.query<{ id: string; email: string; telegram_chat_id: string | null }>(
      `SELECT "Id" AS id, "Email" AS email, "TelegramChatId" AS telegram_chat_id
       FROM users
       WHERE "TenantId" = $1
         AND "Role" = ANY($2::text[])
         AND "IsActive" = true
         AND ("TelegramChatId" IS NOT NULL OR "Email" IS NOT NULL)`,
      [p.tenantId, SHELF_EMPTY_ROLES]
    );
    if (usersRes.rows.length === 0) return;

    const settingsRes = await client.query<{ user_id: string; channel: string }>(
      `SELECT "UserId" AS user_id, "Channel" AS channel
       FROM notification_settings
       WHERE "UserId" = ANY($1::uuid[]) AND "EventType" = $2 AND "IsEnabled" = true`,
      [usersRes.rows.map((u) => u.id), eventType]
    );
    const enabledMap = new Map<string, Set<string>>();
    for (const row of settingsRes.rows) {
      if (!enabledMap.has(row.user_id)) enabledMap.set(row.user_id, new Set());
      enabledMap.get(row.user_id)!.add(row.channel);
    }

    const telegramText = formatShelfEmptyText(p, storeName, zoneName);
    const emailContent = {
      subject: `[ShelfGuard] Порожня полиця: ${storeName} / ${zoneName}`,
      html: telegramText.split("\n").join("<br>"),
    };

    for (const user of usersRes.rows) {
      const userChannels = enabledMap.get(user.id) ?? new Set(SHELF_EMPTY_DEFAULT_CHANNELS);
      const activeChannels = Array.from(userChannels).filter((c) => SHELF_EMPTY_DEFAULT_CHANNELS.includes(c));

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
        }
      }
      if (outcomes.length === 0) continue;

      for (const o of outcomes) {
        if (o.status === "failed") {
          console.error(`[notifications] ${o.channel} send failed for user ${user.id}: ${o.error}`);
        }
      }
      await logNotifications(client, { tenantId: p.tenantId, userId: user.id, eventType, payload: p, outcomes });
    }
  } finally {
    client.release();
  }
}

// ── Promo item without sales (promo.no_sales) ──────────────────────────────

const PROMO_NO_SALES_ROLES = ["merchandiser", "store_manager", "network_manager", "enterprise_admin"];
const PROMO_NO_SALES_DEFAULT_CHANNELS = ["telegram", "email"];

export function formatPromoNoSalesText(p: PromoNoSalesPayload): string {
  const max = 10;
  const lines: string[] = [
    "🏷 <b>ShelfGuard — акційні товари без продажів</b>",
    "",
    `<b>Магазин:</b> ${escapeHtml(p.storeName)}`,
    "Товар в акції, на залишку є, але продажів немає або майже немає. Ймовірно, він не виставлений на полицю або стоїть не на місці — проведіть ревізію.",
    "",
  ];
  for (const i of p.items.slice(0, max)) {
    const expected = i.expectedUnits !== null ? `, очікувалось ~${i.expectedUnits}` : "";
    lines.push(
      `• ${escapeHtml(i.productName)} — «${escapeHtml(i.promoName)}»: продано ${i.unitsSold} за ${i.days} дн.${expected}, залишок ${i.storeStock}`
    );
  }
  if (p.items.length > max) lines.push(`… і ще ${p.items.length - max}`);
  return lines.join("\n");
}

async function handlePromoNoSales(p: PromoNoSalesPayload): Promise<void> {
  const eventType = "promo.no_sales";
  const client = await db.connect();
  try {
    await client.query("SET app.role = 'worker'");

    // enterprise_admin / network_manager see every store; store_manager / merchandiser only the
    // stores assigned to them (primary store or user_locations).
    const usersRes = await client.query<{ id: string; email: string; telegram_chat_id: string | null }>(
      `SELECT u."Id" AS id, u."Email" AS email, u."TelegramChatId" AS telegram_chat_id
       FROM users u
       WHERE u."TenantId" = $1
         AND u."Role" = ANY($2::text[])
         AND u."IsActive" = true
         AND (u."TelegramChatId" IS NOT NULL OR u."Email" IS NOT NULL)
         AND (u."Role" IN ('enterprise_admin', 'network_manager')
              OR u."StoreId" = $3
              OR EXISTS (SELECT 1 FROM user_locations ul WHERE ul."UserId" = u."Id" AND ul."LocationId" = $3))`,
      [p.tenantId, PROMO_NO_SALES_ROLES, p.storeId]
    );
    if (usersRes.rows.length === 0) return;

    const settingsRes = await client.query<{ user_id: string; channel: string }>(
      `SELECT "UserId" AS user_id, "Channel" AS channel
       FROM notification_settings
       WHERE "UserId" = ANY($1::uuid[]) AND "EventType" = $2 AND "IsEnabled" = true`,
      [usersRes.rows.map((u) => u.id), eventType]
    );
    const enabledMap = new Map<string, Set<string>>();
    for (const row of settingsRes.rows) {
      if (!enabledMap.has(row.user_id)) enabledMap.set(row.user_id, new Set());
      enabledMap.get(row.user_id)!.add(row.channel);
    }

    const telegramText = formatPromoNoSalesText(p);
    const emailContent = {
      subject: `[ShelfGuard] Акційні товари без продажів: ${p.storeName}`,
      html: telegramText.split("\n").join("<br>"),
    };

    for (const user of usersRes.rows) {
      const userChannels = enabledMap.get(user.id) ?? new Set(PROMO_NO_SALES_DEFAULT_CHANNELS);
      const activeChannels = Array.from(userChannels).filter((c) => PROMO_NO_SALES_DEFAULT_CHANNELS.includes(c));

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
        }
      }
      if (outcomes.length === 0) continue;

      for (const o of outcomes) {
        if (o.status === "failed") {
          console.error(`[notifications] ${o.channel} send failed for user ${user.id}: ${o.error}`);
        }
      }
      await logNotifications(client, { tenantId: p.tenantId, userId: user.id, eventType, payload: p, outcomes });
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
      } else if (job.data.type === "temp_alert") {
        await handleTempAlert(job.data);
      } else if (job.data.type === "iot_offline") {
        await handleIotOffline(job.data);
      } else if (job.data.type === "shelf_empty") {
        await handleShelfEmpty(job.data);
      } else if (job.data.type === "promo_no_sales") {
        await handlePromoNoSales(job.data);
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
