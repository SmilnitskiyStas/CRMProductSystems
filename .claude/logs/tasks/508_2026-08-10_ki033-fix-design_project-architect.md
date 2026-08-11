# TASK-508 — KI-033 fix design (store_scope silently corrupts marketing-analytics for store_manager/network_manager)

**Date:** 2026-08-10 (written 2026-08-11)
**Agent:** project-architect
**Status:** done
**Scope:** architecture decision + `.claude/docs/decisions.md` only; no C#/migration code changed
(that's TASK-509)

## Problem recap

`pos_transactions`' RESTRICTIVE `store_scope` policy (`20260719193545_
AddLocationStoreScopeRlsPolicies.cs`, ADR-022 Stage 3) only admits rows the caller has a
`user_locations` grant for, unless their role is in `('provider', 'provider_admin', 'worker',
'enterprise_admin')`. `store_manager`/`network_manager` — this module's actual target users —
aren't in that list, so every `MarketingAnalyticsRepository` query silently narrows to their
granted subset instead of the whole tenant. For store-migration this isn't just undercounting,
it reclassifies a genuinely-migrated customer as "not migrated" (full repro:
`.claude/logs/handoffs/504-to-backend_qa-tester.md`, KI-033 in `.claude/docs/known-issues.md`).

## Investigation

- Read `20260719193545_AddLocationStoreScopeRlsPolicies.cs` in full (doc comment + all 9 `CREATE
  POLICY` blocks) — confirmed `store_scope` is fail-closed-by-design and correct for the 9 tables
  it governs on every OTHER read path; the bug is specific to marketing-analytics' tenant-wide
  premise.
- Read `ITenantSessionOverride`/`TenantSessionOverride` (TASK-417) — the precedent this task was
  pointed at. Confirmed its `SET LOCAL`-inside-a-transaction mechanism is sound and reusable in
  shape, but its `Guid tenantId` parameter/security contract doesn't fit this problem (see ADR-028
  point 2 — different trust shape, no per-call value to vouch for).
- Read `MarketingAnalyticsRepository.cs` and `IMarketingAnalyticsRepository.cs` in full — 13
  methods; 12 of 13 query `pos_transactions` (directly or via `pos_transaction_items`), only
  `GetExportCustomersAsync` doesn't (customers-only).
- Read `MarketingAnalyticsService.cs`/`IMarketingAnalyticsService.cs` and
  `MarketingAnalyticsController.cs` — confirmed the controller-level `[Authorize(Policy =
  MarketingAnalyticsViewOrCapability)]` + `[RequireModule("marketing_analytics")]` gate is the
  complete trust boundary before any repository call; confirmed `ExplainSegmentAsync`'s Claude
  advisor call is a real external HTTP call that must never sit inside a DB transaction.
- **Checked option (a)'s risk directly, not by inference**: grepped every `CREATE POLICY`
  referencing `app.role` across all 40 migration files that mention it, then narrowed to the 5
  tables `MarketingAnalyticsRepository` touches (`pos_transactions`, `pos_transaction_items`,
  `items`, `customers`, `locations`). Traced `locations`/`items` back through their `V4*Rename`
  migrations (physical tables `stores`/no-rename-for-items) to confirm which RLS policies actually
  apply post-rename. Result: each of the 5 tables carries only the canonical
  `tenant_isolation`/`provider_bypass` (`app.role='provider'`)/`worker_bypass`
  (`app.role='worker'`) triad, plus `store_scope` on `pos_transactions` only.
  `current_setting('app.role')='enterprise_admin'` matches nothing else anywhere in the schema's
  RLS. So reusing `enterprise_admin` (option a) would be safe **today**, on exactly these 5
  tables — but rejected anyway for the future-policy-drift and misattribution reasons in ADR-028.
- Grepped `app.role` reads outside `Migrations/` (`AppDbContext.cs`,
  `TenantConnectionInterceptor.cs`, a few repositories/domain interfaces, test files) — confirmed
  no trigger or app-layer code logs/branches on `app.role` outside RLS `USING` clauses and
  `TenantConnectionInterceptor.GetSetSql()`, so no audit-log-misattribution vector exists today
  either — still choosing the dedicated-value approach as the durable choice, not because today's
  risk is high.
- Read `TenantConnectionInterceptor.cs`'s `ValidRoles` whitelist and `UserService.ValidRoles` —
  confirmed neither the JWT-claim role whitelist nor the real assignable-role list is a mechanism
  our new sentinel value could ever leak through; it's only ever set by the new override's own
  hardcoded `SET LOCAL` string.
- Read `LoyaltyService.cs`'s actual `ITenantSessionOverride` call sites (`JoinAsync`,
  `ResolvePreferredStoreAsync`, `LoadNetworkDetailsAsync`, `SetPreferredStoreAsync`,
  `ResolveCustomerCodeFormatAsync`) to confirm the established service-layer-wrap convention
  before deciding to deviate from it (repository-layer wrap instead — see ADR-028 point 3 for the
  reasoning).

## Decision

Full decision recorded as **ADR-028** in `.claude/docs/decisions.md`. Summary:

1. **Mechanism**: option (b), not (a) — a new dedicated RLS bypass value
   `'marketing_analytics_bypass'`, added only to `pos_transactions.store_scope`'s IN-list, not
   `enterprise_admin` reuse. Verified-safe-today evidence for (a) is recorded above and in the ADR,
   but rejected for future-drift/auditability reasons.
2. **New interface** `IAnalyticsRlsOverride` (`ShelfGuard.Application/Services/`) — parameterless
   `Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct = default)`, deliberately
   NOT an extension of `ITenantSessionOverride` (different trust shape — see ADR-028 point 2).
   Implementation `AnalyticsRlsOverride` (`ShelfGuard.Infrastructure/Services/`) mirrors
   `TenantSessionOverride`'s `BeginTransactionAsync` → `SET LOCAL app.role =
   'marketing_analytics_bypass'` → run action → `CommitAsync` shape exactly.
3. **Scope**: every method of `MarketingAnalyticsRepository` (all 13), wrapped inside the
   repository itself, not at `MarketingAnalyticsService`'s call sites — full reasoning in ADR-028
   point 3 (no per-call trust value to vouch for; repository-level invariant; guarantees future
   methods are covered by construction).
4. **Migration** (TASK-509, not written here): `pos_transactions.store_scope`'s `USING` clause
   gains `'marketing_analytics_bypass'` in its role IN-list. No other table's `store_scope` policy
   changes — the repository never touches the other 8.

## Verification

- `.claude/docs/decisions.md` updated: ADR-028 added, `Updated:` header bumped to 2026-08-11.
- No C#/migration files touched — design-only, per this task's explicit instruction (TASK-509 is
  the implementation step).
- Handoff written: `.claude/logs/handoffs/508-to-509_project-architect.md`.

## Next steps

TASK-509 (database-engineer + backend-developer): implement exactly the spec in the handoff — the
migration, the interface + implementation, and wrapping all 13 `MarketingAnalyticsRepository`
methods. TASK-510 (security-reviewer) should re-verify the override's blast radius against the
live schema once implemented (the same 5-table `app.role` audit this task did by hand, re-run
against whatever the schema looks like by then). TASK-511 (qa-tester) re-run TASK-504's exact
repro (`manager@demo.local` vs `ea@demo.local` on tenant `8abfbbb5-...`) and confirm
byte-identical responses without needing the `user_locations` backfill workaround. TASK-512
updates KI-033's status in `known-issues.md` once TASK-509/510/511 land — not done here,
deliberately left for that task per the existing task breakdown.
