# TASK-221 — Supplier Marketplace API

**Agent:** backend-developer
**Date:** 2026-06-17
**Status:** done

---

## Summary

Implemented the full Supplier Marketplace API backend for ShelfGuard v4 Phase 3.
Depends on TASK-220 (migration `20260617183005_V4SupplierMarketplace` — tables `supplier_profiles`, `supplier_items`, `supplier_metrics`, `supplier_reviews` with RLS).

---

## Files Created

### Domain
- `ShelfGuard.Domain/Interfaces/IMarketplaceRepository.cs` — repository interface

### Application
- `ShelfGuard.Application/Features/Marketplace/Dtos/MarketplaceDtos.cs` — all DTOs
- `ShelfGuard.Application/Features/Marketplace/IMarketplaceService.cs` — service interface
- `ShelfGuard.Application/Features/Marketplace/MarketplaceService.cs` — service implementation

### Infrastructure
- `ShelfGuard.Infrastructure/Data/Repositories/MarketplaceRepository.cs` — EF Core + RLS bypass pattern

### API
- `ShelfGuard.Api/Controllers/MarketplaceController.cs` — 5 marketplace endpoints
- `ShelfGuard.Api/Controllers/SupplierProfileSettingsController.cs` — 2 self-management endpoints

### Tests
- `ShelfGuard.Tests/Marketplace/MarketplaceServiceTests.cs` — 15 unit tests

### DI wiring
- `ShelfGuard.Application/DependencyInjection.cs` — `IMarketplaceService`
- `ShelfGuard.Infrastructure/DependencyInjection.cs` — `IMarketplaceRepository`

---

## Endpoints Implemented

| Method | Route | Auth | Module gate |
|---|---|---|---|
| GET | /api/marketplace/suppliers | [AllowAnonymous] | [RequireModule("marketplace")] |
| GET | /api/marketplace/suppliers/{id} | [AllowAnonymous] | [RequireModule("marketplace")] |
| GET | /api/marketplace/suppliers/{id}/items | [AllowAnonymous] | [RequireModule("marketplace")] |
| POST | /api/marketplace/search | [AllowAnonymous] | [RequireModule("marketplace")] |
| POST | /api/marketplace/suppliers/{id}/reviews | [Authorize] | [RequireModule("marketplace")] |
| GET | /api/settings/supplier-profile | [Authorize] | — |
| PUT | /api/settings/supplier-profile | [Authorize] | — |

---

## Key Decisions

### RLS bypass for public listing
The `supplier_profiles` table has two RLS policies:
- `tenant_isolation` — standard tenant filtering by `app.tenant_id`
- `provider_bypass` — bypasses RLS when `app.role = 'provider'`

For public endpoints (no auth), `TenantConnectionInterceptor` resets `app.tenant_id` and `app.role`. This means the standard policy would see 0 rows. `MarketplaceRepository` solves this by explicitly executing `SET app.role = 'provider'` before public queries in `SetProviderRoleAsync()`. This matches the pattern referenced in the migration comment.

### Premium field gating
- `GetSupplierProfileAsync` accepts `callerIsAuthenticated` parameter
- Premium fields (website, deliveryRegions, workingHours, paymentTerms) are hidden when `plan == "free"` AND caller is unauthenticated
- If authenticated OR plan is "premium" → all fields shown

### Patch semantics for profile update
`UpdateOwnProfileAsync` only mutates fields where the request value is non-null.

---

## Acceptance Criteria

- [x] `dotnet build` — 0 errors, 0 warnings
- [x] `dotnet test` — 435/435 green (420 existing + 15 new marketplace tests)
- [x] All 7 endpoints implemented with correct status codes
- [x] `[RequireModule("marketplace")]` applied to marketplace endpoints
- [x] Public listing — `[AllowAnonymous]`; no `[Authorize]` on listing/search/items
- [x] `POST .../reviews` validates rating 1-5; 409 on duplicate
- [x] `PUT /api/settings/supplier-profile` validates plan ("free"|"premium")
- [x] Task log created

---

## Unblocked Tasks
- TASK-222 — Frontend: Supplier Marketplace UI
- TASK-223 — Backend: AI Supplier Recommendation
