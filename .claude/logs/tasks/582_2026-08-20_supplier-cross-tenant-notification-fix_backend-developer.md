# TASK-582: Supplier mark-signed 500 (cross-tenant RLS violation on notification_queue)

**Status:** done

## What changed

1. **`SupplierAgreementService.MarkSignedAsync`** (backend/ShelfGuard.Application/Features/Marketplace/SupplierAgreementService.cs) —
   added `ITenantSessionOverride` to the constructor; wrapped the enqueue-notification +
   `SaveChangesAsync` tail in `_tenantSessionOverride.ExecuteAsync(agreement.ClientTenantId, ...)`.
   `notification_queue`'s `tenant_isolation` RLS only allows `TenantId = session tenant OR NULL`,
   but the row is inserted with `TenantId = agreement.ClientTenantId` while the session is
   authenticated as the supplier — raw insert threw an uncaught `PostgresException` 42501. Only
   one call site in the file (`EnqueueSignedNotificationAsync`, checked for others — none found).

2. **`SupplierChatService.SendMessageAsync`** (backend/ShelfGuard.Application/Features/Marketplace/SupplierChatService.cs) —
   same bug, not yet hit by a real user: `EnqueueSupplierMessageNotificationAsync` fires only on
   the supplier→client branch. Added `ITenantSessionOverride`; wrapped just that branch's
   enqueue + `SaveChangesAsync` in `ExecuteAsync(session.ClientTenantId, ...)`. Client→supplier
   branch untouched (no notify, already writes fine under the sender's own tenant).

3. **Global exception handler** (backend/ShelfGuard.Api/Infrastructure/GlobalExceptionHandler.cs,
   new file) — `IExceptionHandler` returning a clean `500 { "error": "..." }`, logs the full
   exception server-side only. Registered via `AddExceptionHandler<GlobalExceptionHandler>()` +
   `AddProblemDetails()`; `app.UseExceptionHandler()` placed in Program.cs right after the
   security-headers block, **before** `UseCors()` so CORS headers still land on error responses
   (this is what was masking the 500 as a CORS error in the browser).

4. Both services' constructors changed — updated `SupplierAgreementServiceTests`/
   `SupplierChatServiceTests` with an `ITenantSessionOverride` mock using the project's existing
   pure-pass-through convention (`LoyaltyServiceTests`/`ConsumerContentServiceTests`).

5. Added `SupplierAgreementMarkSignedRlsIntegrationTests` (new file, `TENANT_ISOLATION_TESTS`
   collection) — live-Postgres regression test, real repos + real `TenantSessionOverride`
   against the local dev DB (`crmproductsystems-postgres-1`, port 5435), throwaway
   `rls_audit_test_role`. Two tests: (a) `MarkSignedAsync` under a real supplier RLS session
   succeeds and `notification_queue` gets the correct row (`TenantId = ClientTenantId`,
   `Status = pending`); (b) negative control proving the raw insert (no override) throws
   `42501` — confirms the root cause is real, not hypothetical.

## Build / test

- `dotnet build`: clean, 0 errors (1 pre-existing unrelated warning).
- `dotnet test`: **1750/1750 passed** (1748 pre-existing + 2 new), 0 failed.
- Manual live-DB verification: done via the new integration tests against local dev Postgres —
  confirmed 200-equivalent success (no exception, DTO returned, status → active) and confirmed
  `notification_queue` receives the row with `TenantId = ClientTenantId`. Post-test cleanup
  verified (0 leftover rows in `tenants`/`supplier_agreements`/`notification_queue`).
- Did not exercise the HTTP endpoint itself (no running local API + JWT flow set up for this
  task) — verification went through the real service + real repos + real Postgres RLS instead,
  which exercises the exact code path and RLS policy that was failing.

## Notes

- No EF migrations — RLS policies unchanged, this is an application-layer fix only.
- `SupplierChatService`'s bug was unreported (not yet hit in prod) — fixed proactively per the
  approved plan since it's the identical pattern.
