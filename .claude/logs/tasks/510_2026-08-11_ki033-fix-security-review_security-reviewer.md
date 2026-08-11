# TASK-510: Security review — KI-033 fix (`marketing_analytics_bypass` RLS override)

**Agent:** security-reviewer
**Date:** 2026-08-11
**Status:** done — **verdict: SHIP.** No blocking findings. 0 confirmed issues across all 5 items
the TASK-509 handoff asked to verify, plus 2 additional checks of my own (item 6/7 from the brief).

## Context

Read `.claude/docs/known-issues.md` KI-033, `.claude/docs/decisions.md` ADR-028,
`.claude/logs/handoffs/508-to-509_project-architect.md` (design spec), and
`.claude/logs/handoffs/509-to-510_backend-developer.md` (implementation handoff, 5 items to
verify) before reviewing. Did not trust the implementer's self-report — independently re-derived
each claim from source.

## Verification, item by item

**1. Blast radius — OK.** Grepped every `current_setting('app.role'` occurrence across all 36
migration files that reference it. All `provider_bypass`/`worker_bypass` policies use `=
'provider'`/`= 'worker'` or a small explicit `IN (...)` list of real role names — none contain
`marketing_analytics_bypass` and none use `NOT IN`/`<>`/`!=`/`LIKE`/`ANY(...)` negation-style
conditions on `app.role` anywhere in the schema (checked explicitly — a negated condition would be
the one shape where adding a new role value elsewhere could inadvertently satisfy an unrelated
policy just by *not* matching a listed exclusion; found none). Read the other 8
`store_scope`-governed tables' policies directly
(`20260719193545_AddLocationStoreScopeRlsPolicies.cs`) — each still carries only the original
4-role list (`provider, provider_admin, worker, enterprise_admin`), confirming the migration
touched only `pos_transactions`.

**2. Reachability — OK.** `grep -rn "marketing_analytics_bypass" backend/` (excl. bin/obj) returns
exactly 6 files: the migration, `IAnalyticsRlsOverride.cs`, `AnalyticsRlsOverride.cs`,
`MarketingAnalyticsRepository.cs` (doc comment only), and the 2 test files. Read
`TenantConnectionInterceptor.ValidRoles` (9 entries + `consumer`), `UserService.ValidRoles` (8
entries), and `DbSeeder.cs`'s role-assignment lines directly — confirmed absent from all three.

**3. Transaction-scoping guarantee — OK.** Read `AnalyticsRlsOverride.ExecuteAsync` side by side
with the already-trusted `TenantSessionOverride.ExecuteAsync` — structurally identical:
`BeginTransactionAsync` → `SET LOCAL` (fixed string literal here, vs. a Guid-interpolated literal
there — same "no injection surface" reasoning) → `action()` → `CommitAsync`, with `await using`
guaranteeing the transaction (and therefore the `SET LOCAL`) is disposed/rolled back on any
exception from `action`, including one that unwinds before `CommitAsync` is reached. No
divergence found. Confirmed no ambient-transaction caller exists today (`grep
BeginTransaction|_analyticsRlsOverride` in `MarketingAnalyticsService.cs` → no matches), so the
"must not be called with an already-open ambient transaction" caveat documented on
`ITenantSessionOverride` (not needed by any current caller) has no live violation here either.

**4. Call-site containment — OK, with one soft-control note (not blocking).**
`IAnalyticsRlsOverride`/`MarketingAnalyticsRepository` cross-referenced — the interface is injected
into exactly one class (`MarketingAnalyticsRepository`'s constructor); grep confirms no other
repository/service references either symbol. DI registration (`DependencyInjection.cs:250-251`) is
a standard `AddScoped`, identical shape/lifetime to `ITenantSessionOverride`'s own registration
immediately above it. Note: because the interface and its DI registration are both `public`,
nothing at the compiler level stops a future developer from injecting `IAnalyticsRlsOverride` into
an unrelated repository — the containment is enforced by the "SECURITY CONTRACT — read before
adding a new call site" doc comment, a convention rather than a hard control. This is **not a new
or weaker pattern introduced by this fix** — it is a verbatim structural mirror of
`ITenantSessionOverride`'s own doc comment (same opening phrase, same shape), which has held up
since TASK-417. Flagging for the record, not as a fix requirement.

**5. Trust-boundary claim — OK, re-verified against current source, not trusted from the doc
comment.** Read `MarketingAnalyticsController.cs` directly: `[Authorize(Policy =
AppPolicies.MarketingAnalyticsViewOrCapability)]` and `[RequireModule("marketing_analytics")]` are
both applied at the **class** level (lines 36-37), so every action — including the TASK-502
additions `GET store-migration`, `GET store-migration/customers`, and `POST
exports/store-migration` — inherits both gates with no method-level override anywhere in the file.
Also read `RequireModuleAttribute`/`RequireModuleFilter` itself: it's a real `IAsyncActionFilter`
that 403s (`{"error":"Module not activated"}`) unless the caller's JWT `tenant_id` resolves to a
tenant with `marketing_analytics` in `Tenant.Modules`; the only bypass is the `provider` role
(no tenant context by design, unrelated to this fix).

## Additional checks beyond the 5-item list

**6. `tenant_isolation` independence — OK.** Read the migration that originally created
`pos_transactions`' RLS (`20260612155425_V3PosFoundation.cs:179-189`):
`tenant_isolation` is `"TenantId" = current_setting('app.tenant_id', true)::uuid` — a PERMISSIVE
policy keyed exclusively on `app.tenant_id`, with `provider_bypass` (`app.role = 'provider'`,
PERMISSIVE) as the only other PERMISSIVE policy on the table. `store_scope` is RESTRICTIVE, so it
ANDs on top of the `tenant_isolation OR provider_bypass` PERMISSIVE set rather than granting an
independent path in. `AnalyticsRlsOverride` only ever issues `SET LOCAL app.role = ...` — it never
touches `app.tenant_id` — so the override structurally cannot widen which tenant a query sees,
only which locations within the caller's own already-correct tenant. As defense in depth (not
required for this conclusion but worth noting): every one of the 13 repository queries also
hard-filters `WHERE t."TenantId" = {0}` with the caller's own JWT-derived `tenantId` parameter, so
even a hypothetical RLS misconfiguration would still be caught at the SQL level.

**7. Any other code path into `MarketingAnalyticsRepository` — OK, none found.** Grepped
`IMarketingAnalyticsRepository`/`MarketingAnalyticsRepository` codebase-wide: the only non-test,
non-DI consumer is `MarketingAnalyticsService.cs`, and `IMarketingAnalyticsService` itself is only
consumed by `MarketingAnalyticsController.cs` (plus its own DI registration). No background
job/worker, no other controller, no test helper exposed outside the test project reaches the
repository without going through the controller's `[Authorize]`+`[RequireModule]` gate first.

## Verification performed independently (not just re-reading the implementer's claims)

- `dotnet build ShelfGuard.sln` — 0 errors, 0 warnings, clean.
- Ran the 3 new/extended tests directly against the local dev Postgres (`crmproductsystems-postgres-1`,
  port 5435, confirmed via `docker port`) rather than trusting the "1400/1400 green" claim at face
  value:
  - `StoreScopeRlsIntegrationTests.MarketingAnalyticsBypassRole_SeesAllLocationsPosTransactions_EvenWithoutAnyUserLocationsRow`
    — passed, 357ms (real DB round-trip, not a soft-skip — soft-skips return in low single-digit ms).
  - `StoreScopeRlsIntegrationTests.ScopedRole_WithoutBypass_CannotSeePosTransactions_OutsideOwnLocation`
    — passed, 136ms, same real-DB evidence.
  - `TenantConnectionInterceptorTests.BuildSetSql_rejects_marketing_analytics_bypass_as_a_role_claim`
    — passed (pure unit test, asserts `SET app.role` is omitted entirely when the claim value is
    `marketing_analytics_bypass`).
  - Full `TenantConnectionInterceptorTests` + `StoreScopeRlsIntegrationTests` filter: 35/35 passed.
- Read the migration's `Down()` and confirmed it restores the exact original 4-role IN-list text
  from `20260719193545_AddLocationStoreScopeRlsPolicies.cs`.

## Overall verdict

**SHIP.** All 5 items from the TASK-509 handoff independently re-verified from source (not
trusted), plus 2 extra checks (tenant-boundary independence, alternate-reachability sweep). One
LOW/informational note (call-site containment is convention-enforced, not compiler-enforced) —
not a new weakness, mirrors the already-accepted `ITenantSessionOverride` pattern exactly, not
blocking. No exploitable gap found. Cleared to proceed to TASK-511 (qa-tester).

## Files reviewed

- `backend/ShelfGuard.Infrastructure/Migrations/20260811110212_AddMarketingAnalyticsBypassToPosTransactionsStoreScope.cs`
  (+ `.Designer.cs`)
- `backend/ShelfGuard.Application/Services/IAnalyticsRlsOverride.cs`,
  `backend/ShelfGuard.Infrastructure/Services/AnalyticsRlsOverride.cs`
- `backend/ShelfGuard.Infrastructure/Services/TenantSessionOverride.cs`,
  `backend/ShelfGuard.Application/Services/ITenantSessionOverride.cs` (precedent comparison)
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/MarketingAnalyticsRepository.cs` (all 13
  methods)
- `backend/ShelfGuard.Api/Controllers/MarketingAnalyticsController.cs`
- `backend/ShelfGuard.Infrastructure/Authorization/RequireModuleAttribute.cs`
- `backend/ShelfGuard.Infrastructure/Interceptors/TenantConnectionInterceptor.cs`
- `backend/ShelfGuard.Application/Features/Users/UserService.cs` (`ValidRoles` only)
- `backend/ShelfGuard.Infrastructure/Data/DbSeeder.cs` (grep/read only)
- `backend/ShelfGuard.Infrastructure/Migrations/20260719193545_AddLocationStoreScopeRlsPolicies.cs`,
  `20260612155425_V3PosFoundation.cs` (`tenant_isolation`/`store_scope` origin)
- `backend/ShelfGuard.Tests/Infrastructure/StoreScopeRlsIntegrationTests.cs`,
  `TenantConnectionInterceptorTests.cs`
- All 36 migration files referencing `current_setting('app.role'` (grep sweep for item 1)
- `.claude/docs/decisions.md` (ADR-028), `.claude/docs/known-issues.md` (KI-033)

## Git

Not committed (repo convention — main session/user commits; this task only produced a task log +
forward handoff, no source changes).
