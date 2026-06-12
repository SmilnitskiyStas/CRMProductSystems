import { db } from "../db";
import { sendTelegramMessage } from "../services/telegram";

const TOKEN = process.env.TELEGRAM_BOT_TOKEN ?? "";
const API = `https://api.telegram.org/bot${TOKEN}`;

type TgUpdate = {
  update_id: number;
  message?: {
    text?: string;
    chat: { id: number; first_name?: string };
  };
};

/**
 * /start <code> account-linking listener (v1-spec §8.1).
 * Long-polls getUpdates; on a valid one-time code binds users.TelegramChatId.
 * Single consumer per bot token — runs only inside this worker.
 */
async function handleStart(chatId: number, firstName: string, code: string | null): Promise<void> {
  if (!code) {
    await sendTelegramMessage(String(chatId),
      `👋 Вітаю, ${firstName}!\n\n` +
      `Це бот сповіщень <b>ShelfGuard</b>: критичні терміни придатності та AI-замовлення.\n\n` +
      `Щоб прив'язати акаунт, відкрийте застосунок → Профіль → Сповіщення — ` +
      `і перейдіть за згенерованим посиланням.`);
    return;
  }

  const client = await db.connect();
  try {
    const { rows } = await client.query<{ id: string; user_id: string; full_name: string }>(
      `SELECT c."Id" AS id, c."UserId" AS user_id, u."FullName" AS full_name
       FROM telegram_link_codes c
       JOIN users u ON u."Id" = c."UserId"
       WHERE c."Code" = $1 AND c."UsedAt" IS NULL AND c."ExpiresAt" > NOW()
       LIMIT 1`,
      [code.toUpperCase()],
    );

    if (rows.length === 0) {
      await sendTelegramMessage(String(chatId),
        `❌ Код недійсний або прострочений.\n` +
        `Згенеруйте новий у застосунку: Профіль → Сповіщення.`);
      return;
    }

    const link = rows[0];
    await client.query(
      `UPDATE users SET "TelegramChatId" = $1 WHERE "Id" = $2`,
      [String(chatId), link.user_id],
    );
    await client.query(
      `UPDATE telegram_link_codes SET "UsedAt" = NOW() WHERE "Id" = $1`,
      [link.id],
    );

    console.log(`[telegram] linked chat ${chatId} to user ${link.user_id}`);
    await sendTelegramMessage(String(chatId),
      `✅ Акаунт прив'язано, <b>${link.full_name}</b>!\n\n` +
      `Тепер ви отримуватимете сповіщення про критичні терміни придатності ` +
      `та готові AI-замовлення.`);
  } finally {
    client.release();
  }
}

export function startTelegramListener(): void {
  if (!TOKEN) {
    console.warn("[telegram] TELEGRAM_BOT_TOKEN not set — listener disabled");
    return;
  }

  let offset = 0;

  const loop = async (): Promise<void> => {
    // eslint-disable-next-line no-constant-condition
    while (true) {
      try {
        const res = await fetch(`${API}/getUpdates?timeout=50&offset=${offset}`, {
          signal: AbortSignal.timeout(60_000),
        });
        if (!res.ok) {
          console.error(`[telegram] getUpdates HTTP ${res.status}`);
          await new Promise((r) => setTimeout(r, 5_000));
          continue;
        }

        const body = (await res.json()) as { ok: boolean; result: TgUpdate[] };
        for (const update of body.result ?? []) {
          offset = update.update_id + 1;

          const text = update.message?.text?.trim();
          const chat = update.message?.chat;
          if (!text || !chat) continue;

          if (text.startsWith("/start")) {
            const code = text.split(/\s+/)[1] ?? null;
            await handleStart(chat.id, chat.first_name ?? "колего", code).catch((e) =>
              console.error(`[telegram] handleStart failed: ${(e as Error).message}`),
            );
          }
        }
      } catch (e) {
        // network blip / timeout — back off briefly and keep polling
        console.error(`[telegram] poll error: ${(e as Error).message}`);
        await new Promise((r) => setTimeout(r, 5_000));
      }
    }
  };

  void loop();
  console.log("[telegram] /start listener polling…");
}
