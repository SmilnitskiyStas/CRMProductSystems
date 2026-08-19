# TASK-528 — Centralized ITenantContext / ICurrentTenantService

**Status:** done
**Agent:** backend-developer
**Date:** 2026-08-17

## Scope actually delivered (narrowed per orchestrator's brief)

Full batch migration of all 46 controllers using the `ResolveTenantId()`/`GetTenantId()` pattern
was explicitly descoped by the orchestrator (large, high-risk, mostly unrelated to this
initiative). Delivered instead:

1. New `ITenantContext` (`backend/ShelfGuard.Application/Services/ITenantContext.cs`) —
   `Guid? TenantId { get; }`, resolves the `tenant_id` JWT claim, `null` when absent/invalid.
2. Impl `TenantContext` (`backend/ShelfGuard.Infrastructure/Services/TenantContext.cs`) —
   `IHttpContextAccessor`-backed, registered `AddScoped` in
   `backend/ShelfGuard.Infrastructure/DependencyInjection.cs`.
3. Migrated controllers (constructor-injected `ITenantContext`, per-controller
   `ResolveTenantId()`/`GetTenantId()` helper removed, all call sites switched to
   `_tenantContext.TenantId`):
   - `BannersController`
   - `LoyaltyController`
   - `LoyaltySettingsController`
4. Deliberately left untouched: `ITenantSessionOverride` and its call sites
   (`ConsumerContentController`, `ConsumerLoyaltyController`, `LoyaltyService.JoinAsync`/
   `GetAvailableNetworksAsync`) — different responsibility (assuming a specific *other* tenant's
   RLS context for a cross-tenant/consumer session), not touched or merged.
   `ConsumerAuthController` checked — doesn't read `tenant_id` at all, out of scope.
5. Remaining ~40 controllers still use their own `ResolveTenantId()`/`GetTenantId()` helper —
   left as-is by design, documented as migrate-opportunistically in `.claude/docs/backend-structure.md`.

## Behavior note

`LoyaltyController.GetTenantId()` previously did **not** reject `Guid.Empty`
(`Guid.TryParse(claim, out var id) ? id : null`), while `BannersController`/
`LoyaltySettingsController` did (`... && id != Guid.Empty ? id : null`). `ITenantContext` uses the
stricter (empty-rejecting) check for all three, matching 2 of 3 prior implementations. This is not
a real behavior change: no `Tenant.Id` is ever `Guid.Empty` in the database (EF-generated), and
`TenantConnectionInterceptor.BuildSetSql` already treats a missing/unparseable `tenant_id` claim as
the null-UUID sentinel rather than a real value — so a genuine empty-GUID claim never occurs in
practice for an authenticated staff request.

## Build / test

- `dotnet build ShelfGuard.sln` — success, 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln --no-build` — **1411/1411 passed**, 0 failed, 0 skipped.
- No test directly instantiated the three migrated controllers (verified via grep), so no test
  code needed updating for the new constructor parameter.

## Files changed

- `backend/ShelfGuard.Application/Services/ITenantContext.cs` (new)
- `backend/ShelfGuard.Infrastructure/Services/TenantContext.cs` (new)
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` (DI registration)
- `backend/ShelfGuard.Api/Controllers/BannersController.cs`
- `backend/ShelfGuard.Api/Controllers/LoyaltyController.cs`
- `backend/ShelfGuard.Api/Controllers/LoyaltySettingsController.cs`
- `.claude/docs/backend-structure.md` (new pattern + honest migration-in-progress status)

`git status` confirmed scope is limited to exactly these 7 files (plus pre-existing unrelated dirty
files already present before this task started, e.g. TASK-527's EF migration on `Tenant.cs`/
`AppDbContextModelSnapshot.cs` — not touched by this task).
