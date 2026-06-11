import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";
import { sendTelegramMessage } from "../services/telegram";

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
  let stores: { id: string; name: string }[];
  try {
    const res = await client.query<{ id: string; name: string }>(
      'SELECT "Id" AS id, "Name" AS name FROM stores WHERE "IsActive"',
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

      await notifyManagers(store.id, store.name, order.items.length);
    } catch (e) {
      console.error(`[ai-order] ${store.name}: ${(e as Error).message}`);
    }
  }
}

async function notifyManagers(storeId: string, storeName: string, itemCount: number): Promise<void> {
  const client = await db.connect();
  try {
    const { rows: managers } = await client.query<{ telegram_chat_id: string }>(
      `SELECT "TelegramChatId" AS telegram_chat_id
       FROM users
       WHERE "IsActive"
         AND "TelegramChatId" IS NOT NULL
         AND "Role" IN ('store_manager', 'network_manager', 'enterprise_admin')`,
    );

    const text =
      `🤖 <b>ShelfGuard — AI замовлення готове</b>\n\n` +
      `<b>Магазин:</b> ${storeName}\n` +
      `<b>Позицій:</b> ${itemCount}\n\n` +
      `Перегляньте і підтвердіть у розділі «AI Замовлення».`;

    for (const m of managers) {
      await sendTelegramMessage(m.telegram_chat_id, text).catch((e) =>
        console.error(`[ai-order] telegram failed: ${e.message}`),
      );
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
