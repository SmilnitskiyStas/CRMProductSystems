# TASK-619 — Loyalty tier-recompute nightly worker job

**Status:** done · **Agent:** devops-engineer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §3 "Worker-задача". Handoffs read:
`.claude/logs/handoffs/613-to-backend_database-engineer.md` (schema),
`.claude/logs/handoffs/615-to-frontend_backend-developer.md` (tier ladder DTO shape).

## What was built

`worker/src/jobs/loyalty-tier-recompute.job.ts` — new nightly cron (`0 4 * * *`, after
`cleanup` at 03:00, before `weather-fetch`/`ai-order`), structured exactly like
`weekly-report.job.ts`: direct `pg` queries via the shared `db` pool, `SET app.role = 'worker'`
up front (required for `worker_bypass` RLS on `loyalty_tier_definitions`/
`loyalty_tier_change_history`, confirmed against the live migration's policy SQL). Deliberately
not the callback-into-API pattern `ai-order.job.ts` uses — that file's own comments document a
history of silent bugs from that indirection (missing `SET app.role`, stale table name after
the v4 rename).

**Composite score formula** — equal-weight `(R+F+M)/3`, rounded to 4 decimals (matches the
`decimal(18,4)` column). RFM quintiles mirror
`MarketingAnalyticsRepository.GetScoredCustomersAsync`'s shape: `recencyScore = 6 - NTILE(5)
OVER (ORDER BY days-since ASC)`, `frequencyScore`/`monetaryScore` = plain `NTILE(5)` ascending.
Population per tenant: `loyalty_memberships` with `Status = 'active'` and at least one
`loyalty_ledger_entries` row with `EntryType = 'accrual'`. Recency = days since the most recent
accrual entry's `CreatedAt`; Frequency = count of accrual entries; Monetary = sum of the linked
`pos_transactions.TotalAmount` (LEFT JOIN, so an accrual with no linked transaction contributes
0 rather than dropping the membership).

Tier selection: for each tenant, `loyalty_tier_definitions` fetched once, ordered by
`SortOrder DESC`; the first rung whose `MinCompositeScore <= score` wins (or `null` if the score
clears none). Factored the two pure pieces (`computeCompositeScore`, `pickQualifyingTier`) out
of the DB I/O and exported them, per the brief — `worker/` has no test harness at all today
(checked: no `*.test.ts` under `worker/src`, no jest/vitest in `package.json`), so no test file
was invented, but the logic is now isolated for one to be added later.

**Write behavior** — only writes `CurrentTierId`/`CompositeScore`/`TierScoreUpdatedAt`, never
`Balance` (confirmed via `LoyaltyMembership.cs`'s own doc comment: those three fields are
written *only* by this job, to avoid the `xmin` concurrency token PosService/LoyaltyService use
for `Balance`). Tier changed → update all three + insert `loyalty_tier_change_history` row.
Score drifted but tier unchanged → update score/timestamp only, no history row. Neither changed
(comparison uses a small epsilon to absorb float/decimal round-trip noise) → no write at all.

**Registration** — `worker/src/index.ts`: import + `Queue`/`upsertJobScheduler` block added
after `cleanup`, `startLoyaltyTierRecomputeWorker()` added to the startup list after
`startCleanupWorker()`.

## Verification

- `npx tsc --noEmit` and `npm run build` inside `worker/`: clean, no errors.
- `npm run lint`: pre-existing gap, not introduced here — `eslint` isn't actually installed in
  `worker/node_modules` (script exists in `package.json` but no `eslint` devDependency), same
  for every other job file in this package.
- Manual SQL dry-run against the dev Postgres container (`crmproductsystems-postgres-1`,
  `Host=localhost;Port=5435;Database=crm;Username=shelfguard_app_dev`, matches the TASK-613
  handoff): ran the exact tenant/tier/RFM-scoring query with `SET app.role='worker'` — 2
  loyalty-enabled tenants found, 0 tier definitions and 0 qualifying memberships in dev data
  today (no accrual ledger entries yet), zero SQL errors.
- Followed up with a synthetic end-to-end dry run wrapped in `BEGIN`/`ROLLBACK`: inserted one
  accrual ledger entry + one tier definition for a real dev membership, re-ran the RFM query
  (got sane scores: recency 5, frequency 1, monetary 1), then executed the actual
  UPDATE-membership + INSERT-history write path and confirmed both rows landed correctly
  (`CurrentTierId`/`CompositeScore`/`TierScoreUpdatedAt` set, history row with the right
  `FromScore`/`ToScore`/`ToTierId`) before rolling back — dev DB left untouched.

## Not done (out of scope for this task, later waves per plan §5)

Frontend loyalty-tiers admin page, `CustomerDetail.tsx` tier tab, `/customer-support` inbox —
all separate waves. `mobile/` untouched (owned by a separate concurrent agent).
