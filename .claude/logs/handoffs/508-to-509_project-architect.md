# TASK-508 handoff — KI-033 fix, ready to implement (TASK-509)

**From:** project-architect
**To:** database-engineer (migration), backend-developer (interface/impl/repository wrap)
**Date:** 2026-08-11
**Authority:** ADR-028 (`.claude/docs/decisions.md`)

Everything below is decided — implement exactly this, no re-litigation needed. Full reasoning
(including why option (a)/reusing `enterprise_admin` was rejected despite being verified-safe on
today's schema) is in ADR-028 and `.claude/logs/tasks/508_2026-08-10_ki033-fix-design_project-architect.md`.

## 1. Migration (database-engineer)

New EF Core migration. Only touches `pos_transactions`' `store_scope` policy — none of the other 8
`store_scope`-governed tables (`product_stock`, `daily_sales`, `pos_shifts`, `write_offs`,
`discounts`, `stock_receipts`, `stock_movements`, `stock_transfers`) change.

```sql
DROP POLICY IF EXISTS store_scope ON pos_transactions;
CREATE POLICY store_scope ON pos_transactions AS RESTRICTIVE
  USING (
    current_setting('app.role', true) IN (
      'provider', 'provider_admin', 'worker', 'enterprise_admin', 'marketing_analytics_bypass'
    )
    OR EXISTS (
         SELECT 1 FROM user_locations ul
         WHERE ul."UserId" = NULLIF(current_setting('app.user_id', true), '')::uuid
           AND ul."LocationId" = pos_transactions."LocationId"
       )
  );
```

`Down()`: restore the original 4-role IN-list (same `DROP POLICY IF EXISTS` + `CREATE POLICY`
shape, `20260719193545_AddLocationStoreScopeRlsPolicies.cs`'s exact original text).

Do NOT add `'marketing_analytics_bypass'` to `TenantConnectionInterceptor.ValidRoles` or
`UserService.ValidRoles` — it must never be settable from a JWT claim or assignable to a real user.
It is only ever written by the new `AnalyticsRlsOverride`'s own hardcoded `SET LOCAL` string
(below).

Recommend a new integration test mirroring `StoreScopeRlsIntegrationTests.cs`'s existing pattern:
as a session with `app.role = 'marketing_analytics_bypass'` set directly (no real JWT/role can
produce this — set it in the test the same way the existing bypass-role tests do), confirm a
`pos_transactions` row invisible to a `user_locations`-scoped session becomes visible; confirm the
literal string is rejected by `TenantConnectionInterceptor.BuildSetSql` when passed as a claim
role (add/extend a `TenantConnectionInterceptorTests.cs` case asserting `SET app.role =
'marketing_analytics_bypass'` is never emitted for any JWT role claim).

## 2. New interface + implementation (backend-developer)

`ShelfGuard.Application/Services/IAnalyticsRlsOverride.cs`:

```csharp
namespace ShelfGuard.Application.Services;

/// <summary>
/// Lets MarketingAnalyticsRepository's queries run under a session role that store_scope's
/// RESTRICTIVE policy on pos_transactions recognizes as exempt, for the duration of one
/// repository method only (TASK-508/KI-033, ADR-028).
///
/// SECURITY CONTRACT — read before adding a new call site: this is NOT a general-purpose
/// "bypass RLS" escape hatch. It may ONLY be called from inside
/// ShelfGuard.Infrastructure.Data.Repositories.MarketingAnalyticsRepository. The trust boundary
/// it relies on is established once, upstream, before any repository method runs: every
/// MarketingAnalyticsController action requires BOTH
/// [Authorize(Policy = AppPolicies.MarketingAnalyticsViewOrCapability)] AND
/// [RequireModule("marketing_analytics")], and every repository query is already scoped to the
/// caller's own JWT tenant_id (tenant_isolation is untouched by this override — it changes
/// app.role only, never app.tenant_id). A future call site outside that repository has NOT
/// inherited that trust boundary just by being in the same codebase — do not reuse this for any
/// other repository or table without re-deriving the same argument from scratch and updating
/// this contract.
///
/// Implemented with Postgres SET LOCAL app.role = 'marketing_analytics_bypass' inside an
/// explicit transaction — reverts automatically on commit or rollback, so it can never leak into
/// a query that runs after this call returns or into a later request reusing the same pooled
/// connection. 'marketing_analytics_bypass' is a value store_scope's own bypass IN-list
/// recognizes; it is not a real role, is never in TenantConnectionInterceptor.ValidRoles, and is
/// never assignable to any User/TenantRole — its only reason to exist is this one bypass check.
/// </summary>
public interface IAnalyticsRlsOverride
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct = default);
}
```

`ShelfGuard.Infrastructure/Services/AnalyticsRlsOverride.cs` — mirror
`TenantSessionOverride.cs` exactly (same file for reference), except:
- No `tenantId` parameter.
- The `SET LOCAL` statement is a fixed string literal, never built from any input:
  `await _db.Database.ExecuteSqlRawAsync("SET LOCAL app.role = 'marketing_analytics_bypass'", ct);`
  (no `EF1002` suppression needed here — there is nothing interpolated).
- Same `await using var tx = await _db.Database.BeginTransactionAsync(ct);` → `SET LOCAL` → run
  `action()` → `await tx.CommitAsync(ct);` shape.

Register in `ShelfGuard.Infrastructure/DependencyInjection.cs` next to
`ITenantSessionOverride`/`TenantSessionOverride`'s existing registration (same lifetime/pattern).

## 3. Wrap `MarketingAnalyticsRepository` (backend-developer)

Inject `IAnalyticsRlsOverride` into `MarketingAnalyticsRepository`'s constructor (alongside the
existing `AppDbContext _db`). Wrap **every** public method's existing body in
`_analyticsRlsOverride.ExecuteAsync(...)` — all 13:

`GetScoredCustomersAsync`, `GetCustomerBaseCountsAsync`, `GetTopProductsAsync`,
`GetBehaviorAsync`, `GetLtvAsync`, `GetAffinityAsync`, `GetBasketAsync`,
`GetExportCustomersAsync` (unaffected by `store_scope` today — `customers` has no such policy —
wrap anyway, uniform rule, harmless), `GetProductBuyerCustomerIdsAsync`,
`GetProductPairBuyerCustomerIdsAsync`, `GetActivePeriodCustomerCountAsync`,
`GetStoreMigrationFlowsAsync`, `GetStoreMigrationCustomersAsync`.

For a method that already issues multiple sequential `SqlQueryRaw` calls in one C# method (e.g.
`GetBehaviorAsync`'s `aggSql`/`daySql`/`hourSql`), wrap the WHOLE method body in one
`ExecuteAsync` call — one transaction covering all of that method's queries, not one per query.

Shape (pattern, apply to every method):
```csharp
public async Task<RfmCustomerBaseCountsRow> GetCustomerBaseCountsAsync(
    Guid tenantId, IReadOnlyList<Guid>? storeIds, CancellationToken ct = default)
{
    return await _analyticsRlsOverride.ExecuteAsync(async () =>
    {
        // ...existing method body, unchanged...
    }, ct);
}
```

Do NOT wrap anything in `MarketingAnalyticsService` — no call site there needs to change. In
particular, `ExplainSegmentAsync`'s `IMarketingAdvisor` (Claude API) call must stay outside any
override transaction; since the override only ever wraps one repository method's own DB work,
this is automatic as long as the wrap happens inside the repository, not around the service
method.

## Definition of done

- `dotnet build` clean, `dotnet test` green (including the new/extended
  `StoreScopeRlsIntegrationTests`/`TenantConnectionInterceptorTests` cases above).
- Live re-run of TASK-504's exact repro (`manager@demo.local` vs `ea@demo.local`,
  `GET /api/marketing-analytics/store-migration` on tenant `8abfbbb5-3190-4de9-9f91-f4de59101bca`)
  — both responses byte-identical, no `user_locations` backfill needed. Also re-check
  `GET /api/marketing-analytics/overview` for the same two callers.
- `grep -rn "marketing_analytics_bypass"` shows exactly: the migration, the two new
  interface/impl files, and their doc comments/tests — nowhere in
  `TenantConnectionInterceptor.ValidRoles`, `UserService.ValidRoles`, or any seed data.

Next: TASK-510 (security-reviewer) re-verifies the override's blast radius against the
then-current schema; TASK-511 (qa-tester) re-runs the store-migration + RFM overview repro as
above; TASK-512 updates KI-033's status in `.claude/docs/known-issues.md` once both pass.
