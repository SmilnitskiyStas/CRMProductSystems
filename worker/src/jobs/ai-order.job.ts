import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";
import { sendTelegramMessage } from "../services/telegram";
import { deliver, logNotifications, type DeliveryOutcome } from "../services/notification-log";

const EVENT_TYPE = "order.replenishment_suggested";
const NOTIFY_ROLES = ["store_manager", "network_manager", "enterprise_admin"];
const DEFAULT_CHANNELS = ["telegram"]; // only telegram is implemented for this event today

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

async function runAiOrderGeneration(): Promise<void> {
  if (!SERVICE_EMAIL || !SERVICE_PASSWORD) {
    console.warn("[ai-order] WORKER_API_EMAIL/PASSWORD not set — skipping");
    return;
  }

  const token = await login();

  const client = await db.connect();
  let stores: { id: string; tenant_id: string; name: string }[];
  try {
    const res = await client.query<{ id: string; tenant_id: string; name: string }>(
      'SELECT "Id" AS id, "TenantId" AS tenant_id, "Name" AS name FROM stores WHERE "IsActive"',
    );
    stores = res.rows;
  } finally {
    client.release();
  }

  for (const store of stores) {
    try {
      const res = await fetch(`${API_BASE}/api/ai-orders/generate`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ storeId: store.id }),
      });

      if (!res.ok) {
        const body = await res.text();
        console.warn(`[ai-order] ${store.name}: HTTP ${res.status} — ${body.slice(0, 150)}`);
        continue;
      }

      const order = (await res.json()) as { id: string; items: unknown[] };
      console.log(`[ai-order] ${store.name}: suggestion ${order.id} (${order.items.length} items)`);

      await notifyManagers(store.tenant_id, store.id, store.name, order.id, order.items.length);
    } catch (e) {
      console.error(`[ai-order] ${store.name}: ${(e as Error).message}`);
    }
  }
}

// TASK-339 / ADR-018 §2: rewired from a direct sendTelegramMessage loop to the same
// in-process pattern as handleIotAlert/handleExpiryAlert in notification.job.ts — role
// lookup scoped to the tenant, notification_settings respected (with role defaults as
// fallback), delivery outcomes logged via logNotifications. No outbox hop needed here:
// this job already runs in the Node worker with direct DB access.
async function notifyManagers(
  tenantId: string,
  storeId: string,
  storeName: string,
  orderId: string,
  itemCount: number,
): Promise<void> {
  const client = await db.connect();
  try {
    const usersRes = await client.query<{ id: string; telegram_chat_id: string | null }>(
      `SELECT "Id" AS id, "TelegramChatId" AS telegram_chat_id
       FROM users
       WHERE "TenantId" = $1 AND "Role" = ANY($2::text[]) AND "IsActive" = true`,
      [tenantId, NOTIFY_ROLES],
    );

    if (usersRes.rows.length === 0) return;

    const settingsRes = await client.query<{ user_id: string; channel: string }>(
      `SELECT "UserId" AS user_id, "Channel" AS channel
       FROM notification_settings
       WHERE "UserId" = ANY($1::uuid[]) AND "EventType" = $2 AND "IsEnabled" = true`,
      [usersRes.rows.map((u) => u.id), EVENT_TYPE],
    );

    const enabledMap = new Map<string, Set<string>>();
    for (const s of settingsRes.rows) {
      if (!enabledMap.has(s.user_id)) enabledMap.set(s.user_id, new Set());
      enabledMap.get(s.user_id)!.add(s.channel);
    }

    const text =
      `🤖 <b>ShelfGuard — AI замовлення готове</b>\n\n` +
      `<b>Магазин:</b> ${storeName}\n` +
      `<b>Позицій:</b> ${itemCount}\n\n` +
      `Перегляньте і підтвердіть у розділі «AI Замовлення».`;
    const payload = { type: "ai_order_suggestion", tenantId, storeId, storeName, orderId, itemCount };

    for (const user of usersRes.rows) {
      const userChannels = enabledMap.get(user.id) ?? new Set(DEFAULT_CHANNELS);
      const activeChannels = Array.from(userChannels).filter((c) => DEFAULT_CHANNELS.includes(c));

      const outcomes: DeliveryOutcome[] = [];
      for (const channel of activeChannels) {
        if (channel === "telegram") {
          outcomes.push(
            user.telegram_chat_id
              ? await deliver("telegram", () => sendTelegramMessage(user.telegram_chat_id!, text))
              : { channel: "telegram", status: "skipped", error: "no telegram_chat_id" },
          );
        }
      }
      if (outcomes.length === 0) continue;

      for (const o of outcomes) {
        if (o.status === "failed") {
          console.error(`[ai-order] ${o.channel} send failed for user ${user.id}: ${o.error}`);
        }
      }

      await logNotifications(client, {
        tenantId,
        userId: user.id,
        eventType: EVENT_TYPE,
        payload,
        outcomes,
      });
    }
  } finally {
    client.release();
  }
}

export function startAiOrderWorker(): Worker {
  const worker = new Worker(
    "ai-order",
    async (job: Job) => {
      console.log(`[ai-order] job ${job.id} started`);
      await runAiOrderGeneration();
    },
    { connection: redisConnection, concurrency: 1 },
  );

  worker.on("completed", (job) => console.log(`[ai-order] job ${job.id} completed`));
  worker.on("failed", (job, err) => console.error(`[ai-order] job ${job?.id} failed:`, err.message));

  return worker;
}
