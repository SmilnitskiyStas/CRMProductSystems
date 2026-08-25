import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

// TASK-619 / plan §3 "Worker-задача" (goofy-bubbling-naur.md): nightly RFM-like tier
// recompute for the loyalty tier ladder landed in TASK-613 (schema) / TASK-615 (admin CRUD +
// PosService accrual-multiplier/discount integration). Structure mirrors weekly-report.job.ts:
// direct `pg` queries via the shared `db` pool, `SET app.role = 'worker'` up front so the
// RLS `worker_bypass` policies on loyalty_tier_definitions/loyalty_tier_change_history apply
// (this job spans every tenant — no per-request app.tenant_id exists to key off). Deliberately
// NOT the callback-into-API pattern ai-order.job.ts uses — that file's own comments document a
// history of silent RLS/table-rename bugs from that indirection (missing `SET app.role`,
// stale table name after the v4 Store→Location rename); direct SQL avoids that whole class of
// bug the same way weekly-report.job.ts already does.
//
// RFM quintile shape mirrors
// backend/ShelfGuard.Application/Features/MarketingAnalytics (MarketingAnalyticsRepository's
// GetScoredCustomersAsync): recencyScore = 6 - NTILE(5) OVER (ORDER BY days-since ASC),
// frequencyScore/monetaryScore = NTILE(5) OVER (ORDER BY ... ASC) — same "lower rank = lower
// score" convention, each in [1,5]. Composite score is the plan's confirmed equal-weight
// default (R+F+M)/3 — do not silently change the weighting here.
//
// Population per tenant: loyalty_memberships with Status = 'active' AND at least one
// loyalty_ledger_entries row with EntryType = 'accrual'. Recency = days since the most recent
// accrual ledger entry; Frequency = count of accrual entries; Monetary = sum of the linked
// pos_transactions.TotalAmount (LEFT JOIN — an accrual entry with no PosTransactionId, which
// per LoyaltyLedgerEntry's own doc shouldn't happen for EntryType = 'accrual', contributes 0
// rather than dropping the membership from the population).
//
// LoyaltyMembership.CurrentTierId/CompositeScore/TierScoreUpdatedAt are written ONLY by this
// job (see LoyaltyMembership.cs's own doc comment) — Balance is never touched here, so this
// job cannot collide with the xmin optimistic-concurrency token PosService/LoyaltyService use
// for concurrent Balance updates during checkout/manual adjustment.

type TenantRow = { id: string };

type TierRow = {
  id: string;
  sort_order: number;
  min_composite_score: string; // numeric → pg returns as string
};

type MembershipScoreRow = {
  membershipId: string;
  currentTierId: string | null;
  currentCompositeScore: string; // numeric → pg returns as string
  recencyScore: number;
  frequencyScore: number;
  monetaryScore: number;
};

export type TierRung = { id: string; sortOrder: number; minCompositeScore: number };

// ── Pure logic (DB-free, kept separate for testability — worker/ has no test harness today,
//    per TASK-619's brief; these two functions are exported so one can be added later without
//    reshaping the job) ─────────────────────────────────────────────────────────────────────

/** Equal-weight RFM composite, rounded to the same 4-decimal precision as the DB column. */
export function computeCompositeScore(
  recencyScore: number,
  frequencyScore: number,
  monetaryScore: number
): number {
  const raw = (recencyScore + frequencyScore + monetaryScore) / 3;
  return Math.round(raw * 10000) / 10000;
}

/**
 * Highest-ranked tier the composite score qualifies for, or null if it clears none of them.
 * `tiersDescBySortOrder` must already be sorted by SortOrder DESC (the caller does this in
 * SQL) — this just returns the first rung whose threshold the score clears.
 */
export function pickQualifyingTier(
  tiersDescBySortOrder: TierRung[],
  compositeScore: number
): TierRung | null {
  for (const tier of tiersDescBySortOrder) {
    if (compositeScore >= tier.minCompositeScore) return tier;
  }
  return null;
}

// A score difference smaller than half the DB column's smallest unit (decimal(18,4)) is
// treated as "unchanged" — avoids float round-trip noise causing a pointless write every night.
const SCORE_EPSILON = 0.00005;

async function runLoyaltyTierRecompute(): Promise<void> {
  const client = await db.connect();
  try {
    // Required for the worker_bypass RLS policies on loyalty_tier_definitions /
    // loyalty_tier_change_history (and the canonical policies on loyalty_memberships /
    // loyalty_ledger_entries) — same mechanism as weekly-report.job.ts / expiry-check.job.ts.
    await client.query("SET app.role = 'worker'");

    const tenantsRes = await client.query<TenantRow>(`
      SELECT t."Id" AS id
      FROM tenants t
      JOIN loyalty_program_settings s ON s."TenantId" = t."Id"
      WHERE s."IsEnabled" = true
    `);

    let membershipsScored = 0;
    let tierChanges = 0;

    for (const tenant of tenantsRes.rows) {
      const tiersRes = await client.query<TierRow>(
        `SELECT "Id" AS id, "SortOrder" AS sort_order, "MinCompositeScore" AS min_composite_score
         FROM loyalty_tier_definitions
         WHERE "TenantId" = $1
         ORDER BY "SortOrder" DESC`,
        [tenant.id]
      );
      const tiers: TierRung[] = tiersRes.rows.map((t) => ({
        id: t.id,
        sortOrder: t.sort_order,
        minCompositeScore: Number(t.min_composite_score),
      }));

      const scoresRes = await client.query<MembershipScoreRow>(
        `WITH accrual_entries AS (
           SELECT l."MembershipId" AS membership_id,
                  l."CreatedAt"    AS created_at,
                  t."TotalAmount"  AS total_amount
           FROM loyalty_ledger_entries l
           JOIN loyalty_memberships m ON m."Id" = l."MembershipId"
           LEFT JOIN pos_transactions t ON t."Id" = l."PosTransactionId"
           WHERE l."TenantId" = $1
             AND l."EntryType" = 'accrual'
             AND m."Status" = 'active'
         ),
         agg AS (
           SELECT membership_id,
                  COUNT(*)::int                                               AS frequency,
                  COALESCE(SUM(total_amount), 0)                              AS monetary,
                  (CURRENT_DATE - (MAX(created_at) AT TIME ZONE 'UTC')::date) AS days_since_last_accrual
           FROM accrual_entries
           GROUP BY membership_id
         )
         SELECT a.membership_id                                                    AS "membershipId",
                m."CurrentTierId"                                                  AS "currentTierId",
                m."CompositeScore"                                                 AS "currentCompositeScore",
                (6 - NTILE(5) OVER (ORDER BY a.days_since_last_accrual ASC))::int  AS "recencyScore",
                (NTILE(5) OVER (ORDER BY a.frequency ASC))::int                    AS "frequencyScore",
                (NTILE(5) OVER (ORDER BY a.monetary ASC))::int                     AS "monetaryScore"
         FROM agg a
         JOIN loyalty_memberships m ON m."Id" = a.membership_id`,
        [tenant.id]
      );

      for (const row of scoresRes.rows) {
        membershipsScored++;

        const compositeScore = computeCompositeScore(
          row.recencyScore,
          row.frequencyScore,
          row.monetaryScore
        );
        const currentCompositeScore = Number(row.currentCompositeScore);
        const newTier = pickQualifyingTier(tiers, compositeScore);
        const newTierId = newTier?.id ?? null;

        const tierChanged = newTierId !== row.currentTierId;
        const scoreChanged = Math.abs(compositeScore - currentCompositeScore) > SCORE_EPSILON;

        if (tierChanged) {
          await client.query(
            `UPDATE loyalty_memberships
             SET "CurrentTierId" = $1, "CompositeScore" = $2, "TierScoreUpdatedAt" = NOW()
             WHERE "Id" = $3`,
            [newTierId, compositeScore.toFixed(4), row.membershipId]
          );
          await client.query(
            `INSERT INTO loyalty_tier_change_history
               ("TenantId", "MembershipId", "FromTierId", "ToTierId", "FromScore", "ToScore", "ChangedAt")
             VALUES ($1, $2, $3, $4, $5, $6, NOW())`,
            [
              tenant.id,
              row.membershipId,
              row.currentTierId,
              newTierId,
              currentCompositeScore.toFixed(4),
              compositeScore.toFixed(4),
            ]
          );
          tierChanges++;
        } else if (scoreChanged) {
          // Score drifted within the same tier band (matters for progress-to-next-tier
          // display) — update the score/timestamp only, no history row (that's for tier
          // transitions only).
          await client.query(
            `UPDATE loyalty_memberships
             SET "CompositeScore" = $1, "TierScoreUpdatedAt" = NOW()
             WHERE "Id" = $2`,
            [compositeScore.toFixed(4), row.membershipId]
          );
        }
        // else: nothing changed — skip the write entirely.
      }
    }

    console.log(
      `[loyalty-tier-recompute] tenants: ${tenantsRes.rows.length}, ` +
        `memberships scored: ${membershipsScored}, tier changes: ${tierChanges}`
    );
  } finally {
    client.release();
  }
}

export function startLoyaltyTierRecomputeWorker(): Worker {
  const worker = new Worker(
    "loyalty-tier-recompute",
    async (job: Job) => {
      console.log(`[loyalty-tier-recompute] job ${job.id} started`);
      await runLoyaltyTierRecompute();
    },
    { connection: redisConnection, concurrency: 1 }
  );

  worker.on("completed", (job) => {
    console.log(`[loyalty-tier-recompute] job ${job.id} completed`);
  });

  worker.on("failed", (job, err) => {
    console.error(`[loyalty-tier-recompute] job ${job?.id} failed:`, err.message);
  });

  return worker;
}
