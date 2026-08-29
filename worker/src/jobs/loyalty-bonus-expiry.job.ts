import { Job, Worker } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

export function startLoyaltyBonusExpiryWorker(): Worker {
  return new Worker("loyalty-bonus-expiry", async (_job: Job) => {
    const client = await db.connect();
    try {
      await client.query("BEGIN");
      await client.query("SET LOCAL app.role = 'worker'");
      const rows = await client.query<{ id: string; tenant_id: string; membership_id: string; remaining: string }>(`
        SELECT l."Id" AS id, l."TenantId" AS tenant_id, l."MembershipId" AS membership_id,
               l."RemainingAmount"::text AS remaining
        FROM loyalty_bonus_lots l
        WHERE l."RemainingAmount" > 0 AND l."ExpiresAt" <= NOW()
        ORDER BY l."MembershipId", l."ExpiresAt"
        FOR UPDATE`);
      const grouped = new Map<string, { tenantId: string; amount: number; lotIds: string[] }>();
      for (const row of rows.rows) {
        const value = grouped.get(row.membership_id) ?? { tenantId: row.tenant_id, amount: 0, lotIds: [] };
        value.amount += Number(row.remaining); value.lotIds.push(row.id); grouped.set(row.membership_id, value);
      }
      for (const [membershipId, value] of grouped) {
        const membership = await client.query<{ balance: string }>(
          `SELECT "Balance"::text AS balance FROM loyalty_memberships WHERE "Id" = $1 FOR UPDATE`,
          [membershipId]
        );
        const currentBalance = Number(membership.rows[0]?.balance ?? 0);
        const deducted = Math.min(currentBalance, value.amount);
        const balanceAfter = currentBalance - deducted;
        await client.query(`UPDATE loyalty_memberships SET "Balance" = $1 WHERE "Id" = $2`, [balanceAfter, membershipId]);
        await client.query(`UPDATE loyalty_bonus_lots SET "RemainingAmount" = 0 WHERE "Id" = ANY($1::uuid[])`, [value.lotIds]);
        if (deducted > 0) {
          await client.query(`INSERT INTO loyalty_ledger_entries ("Id", "TenantId", "MembershipId", "EntryType", "Amount", "BalanceAfter", "Note", "CreatedAt") VALUES (gen_random_uuid(), $1, $2, 'expiry', $3, $4, 'Завершився строк дії бонусів', NOW())`, [value.tenantId, membershipId, -deducted, balanceAfter]);
        }
      }
      await client.query("COMMIT");
    } catch (error) { await client.query("ROLLBACK"); throw error; }
    finally { client.release(); }
  }, { connection: redisConnection });
}
