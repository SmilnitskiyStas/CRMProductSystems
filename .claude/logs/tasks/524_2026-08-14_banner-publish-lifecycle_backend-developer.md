# TASK-524: Banner publish endpoint + lifecycle status + draft-leak fix

**Agent:** backend-developer
**Date:** 2026-08-14
**Status:** done — built, tested, live-verified against the real dev DB.

## Context

Follow-up to TASK-523 (database-engineer, `Banner.PublishedAt`/`Create(publishImmediately)`/
`Publish(utcNow)` — done). Builds the API surface for TASK-525's admin banners history view
(running / past / draft tabs).

## Done

- `backend/ShelfGuard.Application/Features/Banners/Dtos/BannerDtos.cs`:
  - `CreateBannerRequest` gained `bool PublishImmediately = true`.
  - `BannerDto` gained `DateTime? PublishedAt` and `string LifecycleStatus`.
  - Added `BannerLifecycleStatus` constants class (`Draft`/`Running`/`Past`).
- `backend/ShelfGuard.Application/Features/Banners/BannerService.cs`:
  - `CreateAsync` passes `publishImmediately: request.PublishImmediately` into `Banner.Create`.
  - `ToDtosAsync` (the single mapping helper all read paths funnel through) computes
    `LifecycleStatus` from `PublishedAt`/`IsCurrentlyActive` — `"draft"` when
    `PublishedAt == null`, `"running"` when published and currently active, `"past"` otherwise.
    Derived, not stored — same pattern as `IsCurrentlyActive`.
  - New `PublishAsync(tenantId, id, ct)` — loads the banner, calls `banner.Publish(DateTime.UtcNow)`
    (idempotent per TASK-523), saves, returns the updated `BannerDto`. 404-shaped via
    `(null, "Banner not found.")` same as the other id-scoped methods.
- `backend/ShelfGuard.Application/Features/Banners/IBannerService.cs` — added `PublishAsync` to
  the interface.
- `backend/ShelfGuard.Api/Controllers/BannersController.cs` — new
  `POST /api/banners/{id}/publish`, same `AtLeastEnterpriseAdmin` class-level gate as the rest of
  the controller, same tenant-resolution/404 pattern as `GetById`/`GetAnalytics`.
- **Critical correctness fix** —
  `backend/ShelfGuard.Infrastructure/Data/Repositories/ConsumerContentRepository.cs`,
  `GetActiveBannersAsync`: the query backing the public, anonymous
  `GET /api/consumer/{tenantId}/banners` filtered only on `IsActive` + `ValidFrom`/`ValidUntil`.
  A draft (`PublishedAt == null`) with `IsActive=true` and a currently-valid date window would
  have leaked to consumers. Added `b.PublishedAt != null` as a required `Where` condition
  alongside the existing checks.

## Not touched (per brief)

- `Discount`/`DiscountsController`/`DiscountService` — zero changes; its existing
  pending/active/expired/cancelled `Status` enum already covers draft/history for promotional
  products, entirely a frontend concern for TASK-525.
- `Banner.Update(...)` — unchanged, publishing only ever happens via `Publish()` (TASK-523's
  design, preserved).

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`, same baseline as TASK-520/521/523).
- `dotnet test` (full suite) — **1411/1411 green**, no regressions.
- **Live sanity check** against the real dev Postgres (`crmproductsystems-postgres-1`,
  `shelfguard_app_dev` non-superuser role) via `dotnet run` (port 5000) + curl, logged in as
  `ea@demo.local` (enterprise_admin, tenant "Свіжий Кут"):
  1. `POST /api/banners` with `publishImmediately: false`, `validFrom`/`validUntil` set to a
     currently-valid window, default `IsActive=true`, assigned to a real `locationId` → response
     showed `publishedAt: null`, `lifecycleStatus: "draft"`, `isCurrentlyActive: true` (proving
     the two are genuinely independent).
  2. `GET /api/consumer/{tenantId}/banners?storeId=` (anonymous, no auth header) → `[]`. Confirms
     the fix: without it this banner would have appeared (IsActive=true + valid window is exactly
     the leak condition described in the brief).
  3. `POST /api/banners/{id}/publish` → `publishedAt` set to current UTC timestamp,
     `lifecycleStatus` flipped to `"running"`.
  4. Called publish again → identical `publishedAt` timestamp (idempotent, confirmed via
     `Publish()`'s null-guard, no overwrite).
  5. `GET /api/consumer/{tenantId}/banners?storeId=` (anonymous) again → banner now present with
     correctly split `body`/`terms` arrays.
  6. Cleanup: `DELETE /api/banners/{id}` (soft, 204) then hard-purged the row via
     `docker exec crmproductsystems-postgres-1 psql -U crm -d crm -c "DELETE FROM banners WHERE
     \"Id\" = '...'"` — confirmed 0 rows remaining in `banners` and `banner_locations` for that id.
     Dev API process stopped afterward.

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `backend/ShelfGuard.Application/Features/Banners/Dtos/BannerDtos.cs` (modified —
  `PublishImmediately`, `PublishedAt`, `LifecycleStatus`, `BannerLifecycleStatus` constants)
- `backend/ShelfGuard.Application/Features/Banners/BannerService.cs` (modified — `CreateAsync`
  wiring, `PublishAsync`, `LifecycleStatusOf` helper)
- `backend/ShelfGuard.Application/Features/Banners/IBannerService.cs` (modified — `PublishAsync`)
- `backend/ShelfGuard.Api/Controllers/BannersController.cs` (modified — `POST {id}/publish`)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/ConsumerContentRepository.cs` (modified —
  `PublishedAt != null` filter added to `GetActiveBannersAsync`)
