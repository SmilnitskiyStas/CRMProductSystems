import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

type CampaignRow = { queue_id: string; campaign_id: string; title: string; message: string; payload: unknown };
type ExpoTicket = { status: "ok" | "error"; details?: { error?: string } };

async function dispatchCampaignPushes(): Promise<void> {
  const client = await db.connect();
  try {
    await client.query("SET app.role = 'worker'");
    const campaigns = await client.query<CampaignRow>(`
      SELECT q."Id" AS queue_id, c."Id" AS campaign_id, c."Title" AS title, c."Message" AS message, q."Payload" AS payload
      FROM notification_queue q JOIN customer_message_campaigns c
        ON q."TenantId" = c."TenantId" AND q."Payload" @> jsonb_build_object('campaignId', c."Id"::text)
      WHERE q."EventType" = 'customer_message.created' AND q."Channel" = 'push'
        AND q."Status" IN ('pending', 'scheduled') AND (c."ScheduledAt" IS NULL OR c."ScheduledAt" <= NOW())
      ORDER BY q."CreatedAt" ASC LIMIT 20`);
    for (const campaign of campaigns.rows) {
      const tokens = await client.query<{ token: string }>(`
        SELECT DISTINCT a."PushToken" AS token FROM customer_message_recipients r
        JOIN loyalty_memberships m ON m."TenantId" = r."TenantId" AND m."CustomerId" = r."CustomerId" AND m."Status" = 'active'
        JOIN consumer_accounts a ON a."Id" = m."ConsumerAccountId" AND a."IsActive" = true
        WHERE r."CampaignId" = $1 AND a."PushToken" IS NOT NULL`, [campaign.campaign_id]);
      const valid = tokens.rows.map(x => x.token).filter(x => /^Expo(nent)?PushToken\[.+\]$/.test(x));
      let failed = 0;
      for (let i = 0; i < valid.length; i += 100) {
        const batch = valid.slice(i, i + 100).map(to => ({ to, sound: "default", title: campaign.title, body: campaign.message,
          data: { campaignId: campaign.campaign_id } }));
        try {
          const response = await fetch("https://exp.host/--/api/v2/push/send", {
            method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify(batch),
          });
          const body = await response.json() as { data?: ExpoTicket[] };
          failed += !response.ok ? batch.length : (body.data ?? []).filter(x => x.status !== "ok").length;
          const invalid = (body.data ?? []).map((x, index) => x.details?.error === "DeviceNotRegistered" ? batch[index]?.to : null).filter((x): x is string => Boolean(x));
          if (invalid.length) await client.query(`UPDATE consumer_accounts SET "PushToken" = NULL WHERE "PushToken" = ANY($1::text[])`, [invalid]);
        } catch { failed += batch.length; }
      }
      const status = failed > 0 && failed === valid.length ? "failed" : "sent";
      await client.query(
        `UPDATE notification_queue SET "Status" = $2, "SentAt" = CASE WHEN $3 THEN NOW() ELSE NULL END WHERE "Id" = $1`,
        [campaign.queue_id, status, status === "sent"],
      );
      await client.query(`UPDATE customer_message_campaigns SET "Status" = $2 WHERE "Id" = $1`, [campaign.campaign_id, status === "sent" ? "completed" : "failed"]);
    }
  } finally { client.release(); }
}

export function startCustomerMessagePushWorker(): Worker {
  const worker = new Worker("customer-message-push", async (_job: Job) => dispatchCampaignPushes(), { connection: redisConnection, concurrency: 1 });
  worker.on("failed", (job, err) => console.error(`[customer-message-push] job ${job?.id} failed:`, err.message));
  return worker;
}
