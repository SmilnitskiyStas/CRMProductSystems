import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";
import { sendTelegramMessage } from "../services/telegram";
import { sendEmail } from "../services/email";

// v1-spec §8.2/8.3: weekly_report — cron Sunday 08:00, recipients Store Manager +
// Director, channel Email. Email needs RESEND_API_KEY (deferred by user) and is
// skipped gracefully by sendEmail; Telegram is delivered as the working channel.

const REPORT_ROLES = ["store_manager", "network_manager", "enterprise_admin"];
const DEFAULT_CHANNELS = ["email", "telegram"];

type WeeklyStats = {
  tenantId: string;
  tenantName: string;
  newBatches: number;
  writeOffCount: number;
  writeOffLoss: number;
  safe: number;
  warning: number;
  critical: number;
  expired: number;
};

type Recipient = {
  id: string;
  email: string | null;
  telegram_chat_id: string | null;
};

// ── Message formatters ─────────────────────────────────────────────────────

function formatWeekRange(): string {
  const end = new Date();
  const start = new Date(end.getTime() - 7 * 86_400_000);
  const fmt = (d: Date) => d.toISOString().slice(0, 10);
  return `${fmt(start)} — ${fmt(end)}`;
}

function formatTelegramReport(s: WeeklyStats): string {
  return (
    `📊 <b>ShelfGuard — тижневий звіт</b>\n` +
    `<i>${s.tenantName} · ${formatWeekRange()}</i>\n\n` +
    `<b>За тиждень:</b>\n` +
    `➕ Нових партій: ${s.newBatches}\n` +
    `🗑 Списань: ${s.writeOffCount} (втрати: ${s.writeOffLoss.toFixed(2)} грн)\n\n` +
    `<b>Поточний стан полиць:</b>\n` +
    `🟢 Безпечні: ${s.safe}\n` +
    `🟡 Попередження: ${s.warning}\n` +
    `🔴 Критичні: ${s.critical}\n` +
    `💀 Протерміновані: ${s.expired}`
  );
}

function formatEmailReport(s: WeeklyStats): { subject: string; html: string } {
  const subject = `[ShelfGuard] Тижневий звіт — ${s.tenantName} (${formatWeekRange()})`;
  const html = `
    <h2>ShelfGuard — тижневий звіт</h2>
    <p>${s.tenantName} · ${formatWeekRange()}</p>
    <h3>За тиждень</h3>
    <table>
      <tr><td><b>Нових партій</b></td><td>${s.newBatches}</td></tr>
      <tr><td><b>Списань</b></td><td>${s.writeOffCount}</td></tr>
      <tr><td><b>Втрати від списань</b></td><td>${s.writeOffLoss.toFixed(2)} грн</td></tr>
    </table>
    <h3>Поточний стан полиць</h3>
    <table>
      <tr><td><b>Безпечні</b></td><td>${s.safe}</td></tr>
      <tr><td><b>Попередження</b></td><td>${s.warning}</td></tr>
      <tr><td><b>Критичні</b></td><td>${s.critical}</td></tr>
      <tr><td><b>Протерміновані</b></td><td>${s.expired}</td></tr>
    </table>
    <p style="color:#888;font-size:12px">ShelfGuard — система управління термінами придатності</p>
  `;
  return { subject, html };
}

// ── Main handler ───────────────────────────────────────────────────────────

async function runWeeklyReport(): Promise<void> {
  const client = await db.connect();
  try {
    // Disable RLS for the worker — runs as privileged service role
    await client.query("SET app.role = 'worker'");

    const statsRes = await client.query<{
      tenant_id: string;
      tenant_name: string;
      new_batches: string;
      write_off_count: string;
      write_off_loss: string;
      safe: string;
      warning: string;
      critical: string;
      expired: string;
    }>(`
      SELECT t."Id"   AS tenant_id,
             t."Name" AS tenant_name,
             (SELECT COUNT(*) FROM product_stock ps
              WHERE ps."TenantId" = t."Id"
                AND ps."AddedAt" >= NOW() - INTERVAL '7 days')          AS new_batches,
             (SELECT COUNT(*) FROM write_offs w
              WHERE w."TenantId" = t."Id"
                AND w."CreatedAt" >= NOW() - INTERVAL '7 days')         AS write_off_count,
             (SELECT COALESCE(SUM(w."TotalLossAmount"), 0) FROM write_offs w
              WHERE w."TenantId" = t."Id"
                AND w."CreatedAt" >= NOW() - INTERVAL '7 days')         AS write_off_loss,
             (SELECT COUNT(*) FROM product_stock ps
              WHERE ps."TenantId" = t."Id" AND ps."Quantity" > 0
                AND ps."Status" = 'safe')                               AS safe,
             (SELECT COUNT(*) FROM product_stock ps
              WHERE ps."TenantId" = t."Id" AND ps."Quantity" > 0
                AND ps."Status" = 'warning')                            AS warning,
             (SELECT COUNT(*) FROM product_stock ps
              WHERE ps."TenantId" = t."Id" AND ps."Quantity" > 0
                AND ps."Status" = 'critical')                           AS critical,
             (SELECT COUNT(*) FROM product_stock ps
              WHERE ps."TenantId" = t."Id" AND ps."Quantity" > 0
                AND ps."Status" = 'expired')                            AS expired
      FROM tenants t
      WHERE EXISTS (
        SELECT 1 FROM users u
        WHERE u."TenantId" = t."Id"
          AND u."Role" = ANY($1::text[])
          AND u."IsActive" = true
      )
    `, [REPORT_ROLES]);

    let sentCount = 0;

    for (const row of statsRes.rows) {
      const stats: WeeklyStats = {
        tenantId: row.tenant_id,
        tenantName: row.tenant_name,
        newBatches: Number(row.new_batches),
        writeOffCount: Number(row.write_off_count),
        writeOffLoss: Number(row.write_off_loss),
        safe: Number(row.safe),
        warning: Number(row.warning),
        critical: Number(row.critical),
        expired: Number(row.expired),
      };

      const usersRes = await client.query<Recipient>(
        `SELECT "Id"             AS id,
                "Email"          AS email,
                "TelegramChatId" AS telegram_chat_id
         FROM users
         WHERE "TenantId" = $1
           AND "Role" = ANY($2::text[])
           AND "IsActive" = true
           AND ("TelegramChatId" IS NOT NULL OR "Email" IS NOT NULL)`,
        [stats.tenantId, REPORT_ROLES]
      );
      if (usersRes.rows.length === 0) continue;

      // Respect explicit notification_settings; users without a row get defaults
      const settingsRes = await client.query<{ user_id: string; channel: string }>(
        `SELECT "UserId" AS user_id, "Channel" AS channel
         FROM notification_settings
         WHERE "UserId" = ANY($1::uuid[])
           AND "EventType" = 'weekly_report'
           AND "IsEnabled" = true`,
        [usersRes.rows.map((u) => u.id)]
      );
      const enabledMap = new Map<string, Set<string>>();
      for (const s of settingsRes.rows) {
        if (!enabledMap.has(s.user_id)) enabledMap.set(s.user_id, new Set());
        enabledMap.get(s.user_id)!.add(s.channel);
      }

      const telegramText = formatTelegramReport(stats);
      const emailContent = formatEmailReport(stats);

      for (const user of usersRes.rows) {
        const userChannels = enabledMap.get(user.id) ?? new Set(DEFAULT_CHANNELS);
        const sendPromises: Promise<void>[] = [];

        if (userChannels.has("telegram") && user.telegram_chat_id) {
          sendPromises.push(
            sendTelegramMessage(user.telegram_chat_id, telegramText).catch((e) =>
              console.error(`[weekly-report] telegram send failed for user ${user.id}:`, e.message)
            )
          );
        }

        if (userChannels.has("email") && user.email) {
          sendPromises.push(
            sendEmail({ to: user.email, ...emailContent }).catch((e) =>
              console.error(`[weekly-report] email send failed for user ${user.id}:`, e.message)
            )
          );
        }

        if (sendPromises.length === 0) continue;
        await Promise.all(sendPromises);

        await client.query(
          `INSERT INTO notification_queue
             ("TenantId", "UserId", "Channel", "EventType", "Payload", "Status", "RetryCount", "SentAt")
           VALUES ($1, $2, $3, 'weekly_report', $4::jsonb, 'sent', 0, NOW())`,
          [
            stats.tenantId,
            user.id,
            Array.from(userChannels).join(","),
            JSON.stringify(stats),
          ]
        );
        sentCount++;
      }
    }

    console.log(
      `[weekly-report] tenants: ${statsRes.rows.length}, recipients notified: ${sentCount}`
    );
  } finally {
    client.release();
  }
}

export function startWeeklyReportWorker(): Worker {
  const worker = new Worker(
    "weekly-report",
    async (job: Job) => {
      console.log(`[weekly-report] job ${job.id} started`);
      await runWeeklyReport();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("completed", (job) => {
    console.log(`[weekly-report] job ${job.id} completed`);
  });

  worker.on("failed", (job, err) => {
    console.error(`[weekly-report] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
