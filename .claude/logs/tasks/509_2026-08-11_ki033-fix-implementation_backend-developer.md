# TASK-509: KI-033 fix implementation (migration + backend)

**Status:** done
**Agents:** database-engineer + backend-developer (combined per orchestrator instruction)
**Authority:** ADR-028, TASK-508 handoff (`.claude/logs/handoffs/508-to-509_project-architect.md`)

## What changed

1. **Migration** `20260811110212_AddMarketingAnalyticsBypassToPosTransactionsStoreScope.cs` —
   drops/recreates `pos_transactions`' `store_scope` RESTRICTIVE policy, adding
   `'marketing_analytics_bypass'` as a 5th value to the existing 4-role IN-list. `Down()`
   restores the original 4-role text exactly. No other store_scope-governed table touched.
   Applied to local dev Postgres (port 5435) via `dotnet ef database update`.
2. `ShelfGuard.Application/Services/IAnalyticsRlsOverride.cs` +
   `ShelfGuard.Infrastructure/Services/AnalyticsRlsOverride.cs` — mirrors
   `ITenantSessionOverride`/`TenantSessionOverride` exactly (per handoff §2): `SET LOCAL app.role
   = 'marketing_analytics_bypass'` inside an explicit transaction, fixed literal string, no
   `tenantId` param, no EF1002 suppression needed. Registered in
   `ShelfGuard.Infrastructure/DependencyInjection.cs` next to `ITenantSessionOverride` (same
   `AddScoped` lifetime).
3. `MarketingAnalyticsRepository.cs` — constructor now takes `IAnalyticsRlsOverride`; all 13
   public methods wrap their existing bodies in `_analyticsRlsOverride.ExecuteAsync(...)`
   (`GetBehaviorAsync`'s 3 sequential queries wrapped in one `ExecuteAsync`, not three).
   `MarketingAnalyticsService.cs` untouched — no call site changes needed.
4. Tests: `MarketingAnalyticsRepositoryIntegrationTests.cs` updated to pass
   `new AnalyticsRlsOverride(db)` at each of the 11 `new MarketingAnalyticsRepository(db)` call
   sites. Added `StoreScopeRlsIntegrationTests.MarketingAnalyticsBypassRole_SeesAllLocations...`
   (bypass role sees a pos_transactions row an unassigned user_locations session cannot) +
   `ScopedRole_WithoutBypass_CannotSeePosTransactions_OutsideOwnLocation` (control case). Added
   `TenantConnectionInterceptorTests.BuildSetSql_rejects_marketing_analytics_bypass_as_a_role_claim`.

## Verification

- `dotnet build` — clean, 0 errors, 1 pre-existing unrelated warning (MarketplaceServiceTests.cs).
- `dotnet test` (full suite, not filtered) — **1400/1400 passed** after applying the migration to
  the dev DB (first run showed 1 expected failure before the migration was applied there).
- Live repro (local `dotnet run --project ShelfGuard.Api` against dev DB, tenant
  `8abfbbb5-3190-4de9-9f91-f4de59101bca`): temporarily removed the 2 `user_locations` grants QA
  (TASK-504) had backfilled for `manager@demo.local`, confirmed the account was back to its
  original under-scoped state (2/4 locations), then compared vs `ea@demo.local`:
  - `GET /marketing-analytics/store-migration?period=6m` (no store filter) — **byte-identical**.
  - `GET /marketing-analytics/overview?period=6m` — identical on every field; only
    `calculatedAt` differs (expected, wall-clock timestamp from two separate calls).
  - Restored the 2 `user_locations` rows afterward (exact `Id`/`CreatedAt` preserved) so the DB
    is back in the state QA (TASK-504) left it.
- `grep -rn "marketing_analytics_bypass" backend/` (excluding bin/obj) — only in: the migration,
  `IAnalyticsRlsOverride.cs`, `AnalyticsRlsOverride.cs`, `MarketingAnalyticsRepository.cs`'s doc
  comment, and the 2 test files. Confirmed absent from `TenantConnectionInterceptor.ValidRoles`,
  `UserService.ValidRoles`, and `DbSeeder.cs`.

## Next

TASK-510 (security-reviewer) — handoff at `.claude/logs/handoffs/509-to-510_backend-developer.md`.
