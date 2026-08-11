# TASK-509 handoff — KI-033 fix implemented, ready for security review (TASK-510)

**From:** backend-developer (+ database-engineer role, combined per orchestrator)
**To:** security-reviewer
**Date:** 2026-08-11
**Authority:** ADR-028, `.claude/logs/handoffs/508-to-509_project-architect.md`

Implemented exactly what the TASK-508 handoff specified — no design deviations. Full task log:
`.claude/logs/tasks/509_2026-08-11_ki033-fix-implementation_backend-developer.md`.

## Files changed

- `backend/ShelfGuard.Infrastructure/Migrations/20260811110212_AddMarketingAnalyticsBypassToPosTransactionsStoreScope.cs`
  (+ `.Designer.cs`) — adds `'marketing_analytics_bypass'` to `pos_transactions`' `store_scope`
  RESTRICTIVE policy IN-list (now 5 values). No other table's RLS policy changes.
- `backend/ShelfGuard.Application/Services/IAnalyticsRlsOverride.cs` (new)
- `backend/ShelfGuard.Infrastructure/Services/AnalyticsRlsOverride.cs` (new) — `SET LOCAL
  app.role = 'marketing_analytics_bypass'` inside an explicit transaction, fixed literal string
  (no interpolation, no injection surface).
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` — registers
  `IAnalyticsRlsOverride`/`AnalyticsRlsOverride` as `AddScoped`, next to `ITenantSessionOverride`.
- `backend/ShelfGuard.Infrastructure/Data/Repositories/MarketingAnalyticsRepository.cs` — all 13
  public methods now run their unchanged body inside `_analyticsRlsOverride.ExecuteAsync(...)`.
  `MarketingAnalyticsService.cs` has zero changes — nothing to review there.
- Tests: `MarketingAnalyticsRepositoryIntegrationTests.cs` (constructor call sites updated),
  `StoreScopeRlsIntegrationTests.cs` (+2 tests), `TenantConnectionInterceptorTests.cs` (+1 test).

## What to verify

1. **Blast radius of the new bypass value.** `'marketing_analytics_bypass'` only appears in
   `pos_transactions`' `store_scope` policy — confirm no other RLS policy on any table
   incidentally checks `current_setting('app.role', true) IN (...)` in a way that would also
   start matching this new value (the other 8 store_scope tables, and any unrelated
   role-conditioned policy elsewhere in the schema).
2. **Reachability.** `grep -rn "marketing_analytics_bypass" backend/` (excluding bin/obj) must
   show ONLY: the migration, `IAnalyticsRlsOverride.cs`, `AnalyticsRlsOverride.cs`,
   `MarketingAnalyticsRepository.cs`'s doc comment, and the 2 test files — confirmed absent from
   `TenantConnectionInterceptor.ValidRoles`, `UserService.ValidRoles`, and `DbSeeder.cs`. Verify
   independently rather than trusting this claim.
3. **Transaction-scoping guarantee.** `AnalyticsRlsOverride.ExecuteAsync` uses `SET LOCAL` inside
   `BeginTransactionAsync`/`CommitAsync`, same shape as `TenantSessionOverride` — confirm this
   really cannot leak `app.role = 'marketing_analytics_bypass'` onto a later query on the same
   pooled connection (e.g. via an exception path, or EF Core's own ambient-transaction reuse).
4. **Call-site containment.** Confirm `IAnalyticsRlsOverride` is only injected into
   `MarketingAnalyticsRepository` — not into `MarketingAnalyticsService` or any other repository
   — and that DI registration doesn't accidentally make it easy to grab from elsewhere.
5. **Trust-boundary claim in the doc comment** — `IAnalyticsRlsOverride`'s XML doc asserts the
   override is safe because every controller action requires
   `[Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)]` +
   `[RequireModule("marketing_analytics")]`, and every repository query stays scoped to the
   caller's own JWT `tenant_id`. Worth independently confirming both premises still hold against
   the current `MarketingAnalyticsController.cs`.

## Verification already done (see task log for full detail)

- `dotnet build` clean; `dotnet test` full suite — 1400/1400 green.
- Live repro against tenant `8abfbbb5-3190-4de9-9f91-f4de59101bca`: `manager@demo.local`
  (store_manager, restored to its true under-scoped 2/4-location grant state for the test) vs
  `ea@demo.local` (enterprise_admin) — `GET /marketing-analytics/store-migration?period=6m`
  byte-identical; `GET /marketing-analytics/overview?period=6m` identical apart from the
  wall-clock `calculatedAt` field.

## Next

TASK-511 (qa-tester) re-runs the store-migration + RFM overview repro after your review passes;
TASK-512 updates KI-033's status in `.claude/docs/known-issues.md` once both TASK-510 and
TASK-511 pass. Neither `.claude/docs/known-issues.md` nor any frontend file was touched by this
task — out of scope per the brief.
