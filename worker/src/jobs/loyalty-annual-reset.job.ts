import { Job, Worker } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

export function startLoyaltyAnnualResetWorker(): Worker {
  return new Worker("loyalty-annual-reset", async (_job: Job) => {
    const client = await db.connect();
    try {
      await client.query("BEGIN");
      await client.query("SET LOCAL app.role = 'worker'");
      const due = await client.query<{ id: string; tenant_id: string }>(`
        SELECT "Id" AS id, "TenantId" AS tenant_id
        FROM loyalty_program_settings
        WHERE "AnnualBonusResetEnabled" = TRUE
          AND "AnnualBonusResetMonth" = EXTRACT(MONTH FROM (NOW() AT TIME ZONE "BonusResetTimeZone"))
          AND "AnnualBonusResetDay" = EXTRACT(DAY FROM (NOW() AT TIME ZONE "BonusResetTimeZone"))
          AND "AnnualBonusResetHour" = EXTRACT(HOUR FROM (NOW() AT TIME ZONE "BonusResetTimeZone"))
          AND COALESCE("LastAnnualBonusResetYear", 0) <> EXTRACT(YEAR FROM (NOW() AT TIME ZONE "BonusResetTimeZone"))
        FOR UPDATE`);

      for (const setting of due.rows) {
        await client.query(`
          INSERT INTO loyalty_ledger_entries
            ("Id", "TenantId", "MembershipId", "EntryType", "Amount", "BalanceAfter", "Note", "CreatedAt")
          SELECT gen_random_uuid(), "TenantId", "Id", 'expiry', -"Balance", 0,
                 'Щорічне обнулення бонусів', NOW()
          FROM loyalty_memberships
          WHERE "TenantId" = $1 AND "Balance" > 0`, [setting.tenant_id]);
        await client.query(`UPDATE loyalty_memberships SET "Balance" = 0 WHERE "TenantId" = $1 AND "Balance" > 0`, [setting.tenant_id]);
        await client.query(`UPDATE loyalty_bonus_lots SET "RemainingAmount" = 0 WHERE "TenantId" = $1 AND "RemainingAmount" > 0`, [setting.tenant_id]);
        await client.query(`UPDATE loyalty_program_settings SET "LastAnnualBonusResetYear" = EXTRACT(YEAR FROM (NOW() AT TIME ZONE "BonusResetTimeZone")), "UpdatedAt" = NOW() WHERE "Id" = $1`, [setting.id]);
      }
      await client.query("COMMIT");
    } catch (error) {
      await client.query("ROLLBACK");
      throw error;
    } finally { client.release(); }
  }, { connection: redisConnection });
}
