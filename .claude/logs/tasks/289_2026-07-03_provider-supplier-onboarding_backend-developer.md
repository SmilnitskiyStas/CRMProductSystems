# TASK-289 — Provider-path supplier onboarding + cabinet backfill + role guard

**Agent:** backend-developer · **Date:** 2026-07-03 · **Status:** done

## Prior run left undone
`SupplierOnboarding.CreateOwnerManaged` existed but was unused anywhere. Provider wizard
(`ProviderService.CreateTenantAsync`) didn't create Supplier/Profile for supplier tenants;
at least one such tenant on prod has no profile. No cabinet backfill. No role validation
on `POST /api/provider/tenants/{id}/users`. No tests for any of this.

## What was completed this run
- `ITenantRepository`: added `AddPendingAsync` (deferred, no immediate SaveChanges) +
  `AddSupplierAsync`/`AddSupplierProfileAsync`, implemented in `TenantRepository`.
- `ProviderService.CreateTenantAsync` (`Provider/ProviderService.cs:116-128`): calls
  `SupplierOnboarding.CreateOwnerManaged` for `business_type == "supplier"`, single
  `SaveChangesAsync` — same one-transaction shape as `TenantAdminService`.
- `TenantAdminService.CreateTenantAsync` refactored to call the same `SupplierOnboarding`
  helper instead of inlining the entity construction (dedup).
- `IMarketplaceRepository` + `MarketplaceRepository`: added
  `GetTenantOnboardingInfoAsync` (business_type + name, no RLS) and
  `GetOrCreateOwnerManagedProfileAsync` (persists a not-yet-saved Supplier/Profile pair,
  race-safe: catches `DbUpdateException` on the unique-index race, detaches, re-fetches —
  same pattern as `GetOrCreatePlatformTenantIdAsync`, BUG-012).
- `SupplierCabinetService.ResolveAsync`: on missing owner-managed profile, checks the
  tenant's business_type and lazily backfills via the repo method above. No-op for
  non-supplier tenants and when a profile already exists.
- `CreateTenantUserRequest.Role` + validation in `ProviderService.CreateTenantUserAsync`:
  supplier tenant → must be `supplier_admin`; any other tenant → must be `enterprise_admin`;
  mismatch → 400. (Frontend already had `role` wired per TASK-290/BUG-013 logs — no frontend
  change needed.)
- Tests added: `ShelfGuard.Tests/Provider/ProviderServiceTests.cs` (onboarding hook fires for
  supplier tenants / skipped for others; role guard both directions),
  `SupplierCabinetServiceTests` (+3: lazy backfill for supplier tenant, no-op for non-supplier,
  no-op when profile already exists).

## Verification
- `dotnet build` — 0 errors, 0 warnings.
- `dotnet test` — 513/513 passed (was 506 before this task; +7 new tests, no regressions).

## Not done / left for follow-up
- None outstanding for this task's scope. `.claude/tasks/current.md` TASK-289 marked `done`.
