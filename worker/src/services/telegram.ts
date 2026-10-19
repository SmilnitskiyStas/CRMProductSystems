import { CircuitBreaker, TransientError } from "./circuit-breaker";

const TELEGRAM_BOT_TOKEN = process.env.TELEGRAM_BOT_TOKEN ?? "";
const TELEGRAM_API = `https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}`;

// A hung Telegram must not block a notification job forever, and a Telegram outage must
// fail fast (BullMQ retries with backoff) instead of every job waiting out the timeout.
const SEND_TIMEOUT_MS = 10_000;
const telegramBreaker = new CircuitBreaker("telegram");

// Returns "skipped" when the channel is not configured; throws on delivery error.
export async function sendTelegramMessage(chatId: string, text: string): Promise<"sent" | "skipped"> {
  if (!TELEGRAM_BOT_TOKEN) {
    console.warn("[telegram] TELEGRAM_BOT_TOKEN not set — skipping send");
    return "skipped";
  }

  return telegramBreaker.run(async () => {
    let res: Response;
    try {
      res = await fetch(`${TELEGRAM_API}/sendMessage`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          chat_id: chatId,
          text,
          parse_mode: "HTML",
        }),
        signal: AbortSignal.timeout(SEND_TIMEOUT_MS),
      });
    } catch (err) {
      // Network error / timeout → transient, counts towards opening the circuit.
      throw new TransientError(`Telegram unreachable: ${(err as Error).message}`);
    }

    if (!res.ok) {
      const body = await res.text();
      const message = `Telegram API error ${res.status}: ${body}`;
      // 5xx and 429 = Telegram-side trouble (transient); other 4xx = our request (bad chat id).
      throw res.status >= 500 || res.status === 429 ? new TransientError(message) : new Error(message);
    }
    return "sent" as const;
  });
}
