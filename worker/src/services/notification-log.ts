import type { PoolClient } from "pg";

export type DeliveryStatus = "sent" | "skipped" | "failed";

export type DeliveryOutcome = {
  channel: string;
  status: DeliveryStatus;
  error?: string;
};

// Runs one channel delivery and maps the result/exception to a DeliveryOutcome.
// `send` returns "skipped" when the channel is not configured and throws on error.
export async function deliver(
  channel: string,
  send: () => Promise<"sent" | "skipped">
): Promise<DeliveryOutcome> {
  try {
    return { channel, status: await send() };
  } catch (e) {
    const error = e instanceof Error ? e.message : String(e);
    return { channel, status: "failed", error };
  }
}

// One notification_queue row per channel with the accurate delivery status (TASK-042)
export async function logNotifications(
  client: PoolClient,
  params: {
    tenantId: string;
    userId: string;
    eventType: string;
    payload: unknown;
    outcomes: DeliveryOutcome[];
  }
): Promise<void> {
  for (const o of params.outcomes) {
    await client.query(
      `INSERT INTO notification_queue
         ("TenantId", "UserId", "Channel", "EventType", "Payload", "Status", "RetryCount", "SentAt", "Error")
       VALUES ($1, $2, $3, $4, $5::jsonb, $6::text, 0,
               CASE WHEN $6::text = 'sent' THEN NOW() ELSE NULL END, $7)`,
      [
        params.tenantId,
        params.userId,
        o.channel,
        params.eventType,
        JSON.stringify(params.payload),
        o.status,
        o.error ?? null,
      ]
    );
  }
}
