const TELEGRAM_BOT_TOKEN = process.env.TELEGRAM_BOT_TOKEN ?? "";
const TELEGRAM_API = `https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}`;

// Returns "skipped" when the channel is not configured; throws on delivery error.
export async function sendTelegramMessage(chatId: string, text: string): Promise<"sent" | "skipped"> {
  if (!TELEGRAM_BOT_TOKEN) {
    console.warn("[telegram] TELEGRAM_BOT_TOKEN not set — skipping send");
    return "skipped";
  }

  const res = await fetch(`${TELEGRAM_API}/sendMessage`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      chat_id: chatId,
      text,
      parse_mode: "HTML",
    }),
  });

  if (!res.ok) {
    const body = await res.text();
    throw new Error(`Telegram API error ${res.status}: ${body}`);
  }
  return "sent";
}
