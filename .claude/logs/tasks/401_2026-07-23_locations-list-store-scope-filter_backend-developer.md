# TASK-401 — Locations list store-scope filter (ADR-022 Stage 3 companion)

**Date:** 2026-07-23 · **Agent:** backend-developer · **Status:** done (not committed — main session commits with Stage 3 merge)

## What was done

`GET /api/locations` now narrows the returned list for store-scoped roles:

- **Admin tier** (provider / provider_admin / enterprise_admin, and any non-scoped role): full tenant list, no assignment lookup.
- **Scoped roles** (network_manager, store_manager, merchandiser, storekeeper, cashier, staff) with ≥1 `user_locations` row: only assigned locations.
- **Scoped roles with 0 rows: fail-open** — full list returned. Deliberate transitional semantics (documented in code): pre-Stage-2-backfill users must not get an empty StoreSelector (frontend takes `stores[0]`, hides on empty). Data protection itself is the Stage 3 RESTRICTIVE RLS; this filter is cosmetic. Missing tenant/user claims also fail open (defensive).

## Changed files

- `backend/ShelfGuard.Application/Features/Locations/ILocationService.cs` — `GetAllAsync(Guid? tenantId, Guid? userId, string? role, ct)` signature + doc.
- `backend/ShelfGuard.Application/Features/Locations/LocationService.cs` — injected existing `IUserLocationRepository` (TASK-392, reused — no new repo); `StoreScopedRoles` set built from Domain `AppRoles` constants (not Infrastructure `AppPolicies`, keeping layer direction clean); filter + fail-open comment.
- `backend/ShelfGuard.Api/Controllers/LocationsController.cs` — `GetAll` passes tenant_id / `ClaimTypes.NameIdentifier` / `ClaimTypes.Role` claims (thin controller, same helper patterns as ServiceDeskController).
- `backend/ShelfGuard.Tests/Locations/LocationServiceTests.cs` — constructor updated for new dependency.
- `backend/ShelfGuard.Tests/Locations/LocationServiceGetAllScopeTests.cs` — new: all 3 branches (admin sees all + no lookup; each scoped role filtered; zero-assignment fail-open) + missing-claim defensive case.

Untouched by design: `GetById`, zones, floor-plan endpoints; Stage 3 migration/RLS branch; frontend.

## Verification

- `dotnet build` — 0 errors (1 pre-existing unrelated warning in Marketplace tests).
- `dotnet test` — 918/918 green (was 907 + 11 new).
