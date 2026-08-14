# TASK-521: Banner admin API + consumer content API (Consumer App plan)

**Agent:** backend-developer
**Date:** 2026-08-14
**Status:** done — built, tested, live-verified against a real (non-superuser) dev DB. No blocker.

## Context

Backend slice of the Consumer App plan (`C:\Users\stass\.claude\plans\quirky-questing-hoare.md`),
blocked on TASK-520 (database-engineer, schema — done). Two deliverables: admin CRUD for
promotional banners, and a new public consumer-facing read API (banners/promotions/catalog) for
the mobile "споживчий" app. TASK-522 (frontend-developer, admin UI) is blocked on this task; a
separate mobile-developer task (not yet numbered) is blocked on the handoff doc below.

## Done

**Admin banner management** (`[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]`, same
gate as `LoyaltySettingsController`):
- `backend/ShelfGuard.Domain/Interfaces/IBannerRepository.cs` + `Infrastructure/Data/Repositories/BannerRepository.cs`
  — CRUD over `Banner`, batched (banner-id → list) lookups for `BannerLocation`/`BannerProduct`
  ids and `BannerEvent` view/click counts (same N+1-avoidance batching style as
  `UserLocationRepository`). `ReplaceLocationsAsync`/`ReplaceProductsAsync` are full delete+insert,
  don't call `SaveChanges` themselves — same transaction-boundary convention as
  `UserLocationRepository.ReplaceForUserAsync`.
- `backend/ShelfGuard.Application/Features/Banners/` — `IBannerService`/`BannerService`,
  `Dtos/BannerDtos.cs`. List/detail DTO carries resolved `locationIds`/`productIds` plus
  `viewCount`/`clickCount` (batched, not per-row queries) for TASK-522's list rows. Admin
  Body/Terms stay the entity's raw "\n"-joined string (a plain textarea round-trips this
  directly) — only the consumer-facing DTO splits into arrays, see below.
- `backend/ShelfGuard.Api/Controllers/BannersController.cs` — `GET /api/banners` (list,
  `?locationId`/`?activeOnly`), `GET/{id}`, `POST`, `PUT/{id}` (full replace incl.
  locationIds/productIds), `DELETE/{id}` (→ `SetActive(false)`, never a hard delete),
  `POST/{id}/image` (`IFormFile`, byte-for-byte copy of `ItemsController.UploadImage`'s 5MB cap +
  jpg/jpeg/png/webp/gif allowlist + `wwwroot/uploads/{feature}/{id}.{ext}` disk pattern, just
  `banners` instead of `items`), `GET/{id}/analytics` (`{ viewCount, clickCount, ctr }`, ctr=0
  when viewCount=0).
- No FK-existence pre-validation on `locationIds`/`productIds` beyond distinctness — deliberately
  mirrors `DiscountService.CreateAsync`'s style (no ProductId/StoreId existence check either);
  the FK constraints TASK-520 already added are the real backstop, and only enterprise_admin+ can
  reach this endpoint in the first place. Same reasoning as `UserLocationRepository`.

**Consumer-facing read API** (`ShelfGuard.Api/Controllers/ConsumerContentController.cs`,
`[Route("api/consumer")]`, `[AllowAnonymous]` — works with no `Authorization` header at all, and
still attaches `ConsumerAccountId` when a consumer JWT is present):
- `GET /api/consumer/{tenantId}/banners?storeId=` — active banners for the store, body/terms
  split into `string[]` server-side (`.Split('\n', RemoveEmptyEntries|TrimEntries)`), with
  attached products resolved via `BannerProduct` → `Item`.
- `POST /api/consumer/{tenantId}/banners/{id}/view` / `.../click` — inserts a `BannerEvent`,
  `ConsumerAccountId` nullable (anonymous allowed). 204 on success, 404 if the banner doesn't
  exist for that tenant.
- `GET /api/consumer/{tenantId}/promotions?storeId=` — pure read projection over active
  `Discount` rows (Status=active, within ValidFrom/ValidUntil) joined with `Item`. Zero changes
  to `Discount`/`DiscountService`/`DiscountsController`/`IDiscountRepository`.
- `GET /api/consumer/{tenantId}/catalog?storeId=&search=&categoryId=&page=&pageSize=` —
  paginated active `Item`s for the tenant, `isAvailableAtStore` derived from a `ProductStock`
  sum at that store (restricted to the current page's item ids, not the whole catalog, since
  this is a browse endpoint not a live-inventory guarantee).
- New `Application/Features/ConsumerContent/` — `IConsumerContentRepository` returns Application
  DTOs directly (same precedent as `Features.Analytics.IAnalyticsRepository`: a read-only
  cross-feature query repository that doesn't belong to any single domain entity, since it joins
  `Banner`/`BannerLocation`/`BannerProduct`/`Discount`/`Item`/`ProductStock`).
  `ConsumerContentRepository` (Infrastructure) implements it directly against `AppDbContext`,
  same style as `AnalyticsRepository`.
- **Tenant context:** every repository call runs inside `ITenantSessionOverride.ExecuteAsync`
  (`ConsumerContentService`) — reused exactly, no new mechanism. tenantId comes from the route,
  validated by a `Tenants.GetByIdAsync` existence check first (Tenants carries no RLS at all —
  confirmed by grep, same precedent `LoyaltyService.JoinAsync` already relies on) before the
  override opens its transaction. The `view`/`click` existence-check + insert share one
  `ExecuteAsync` call (one transaction) rather than two.

## DI registrations

- `ShelfGuard.Infrastructure/DependencyInjection.cs`: `IBannerRepository→BannerRepository`,
  `IConsumerContentRepository→ConsumerContentRepository`.
- `ShelfGuard.Application/DependencyInjection.cs`: `IBannerService→BannerService`,
  `IConsumerContentService→ConsumerContentService`.

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning, same as TASK-520's
  baseline).
- `dotnet test` (full suite) — **1411/1411 green**, no new test file needed (no behavior change
  to any existing dynamic RLS audit's enumerated table set — the new tables were already
  confirmed by TASK-520; this task added no new tables).
- **Live sanity check** against the real dev Postgres (`crmproductsystems-postgres-1`,
  `shelfguard_app_dev` non-superuser role) via `dotnet run` + curl:
  1. `GET /api/consumer/{tenantId}/banners|promotions|catalog` all return `200` with **no
     `Authorization` header at all** — confirmed anonymous browsing works.
  2. Logged in as a real `enterprise_admin` (dev seed `ea@demo.local`/`password`), created a
     banner via `POST /api/banners` with `locationIds`/`productIds`, got it back correctly
     shaped (incl. `isCurrentlyActive: true`).
  3. `GET /api/consumer/{tenantId}/banners?storeId=` (anonymous) showed the new banner, with
     `body`/`terms` correctly split into arrays and the attached product resolved.
  4. `POST .../view` and `.../click` (anonymous, no token) both returned `204`; `GET
     /api/banners/{id}/analytics` (admin) then showed `{ viewCount: 1, clickCount: 1, ctr: 1 }`.
  5. **Cross-tenant isolation**: same `storeId`, a *different* tenantId in the route →
     `GET .../banners` returned `[]` (not the other tenant's banner), and `POST .../view` against
     the wrong tenant returned `404 Banner not found` — confirms `ITenantSessionOverride` +
     RLS correctly scope every read/write to the route's tenantId, not just "whichever tenant
     happens to own the id."
  6. `DELETE /api/banners/{id}` (admin) → `204`, and the banner immediately disappeared from the
     anonymous consumer feed (soft-hide, not hard delete — confirmed via a DB row still existing
     with `IsActive=false` before final cleanup).
  7. `GET /api/banners` with no token → `401` (policy gate enforced).
  8. Test banner + its join/event rows were deleted directly via psql afterward (cascade —
     confirmed `banners`/`banner_locations`/`banner_products`/`banner_events` all back to 0 rows)
     to leave the dev DB clean, no residue.

## Deviations from the plan / judgment calls

- `UpdateBannerRequest` deliberately has no `IsActive` field — `Banner.Update()` (TASK-520) never
  accepted one; the entity's own API surface already decided IsActive is mutated only via
  `SetActive` (DELETE / a future "resume" endpoint if ever added), not through the general PUT.
  Left as-is rather than changing the entity.
- Added `viewCount`/`clickCount` to the list/detail `BannerDto`, not just the dedicated
  `/analytics` endpoint — the plan's own TASK-522 section describes the list row as showing
  "views/clicks" inline, so the list endpoint needed them anyway; kept it to one batched query
  rather than per-row N+1.
- No `[RequireModule(...)]` gate on either controller — nothing in the brief named a module key
  for banners/consumer-content, and the plan explicitly frames this as public marketing content
  that should work even for anonymous/unregistered users, which module gating (keyed off a JWT
  tenant claim) can't apply to anyway for the consumer side.

## Not in scope (per brief)

- No frontend changes (`/consumer-app` admin UI) — TASK-522, frontend-developer.
- No `mobile/` changes — separate mobile-developer task, blocked on the handoff doc below.
- No changes to `Discount`/`DiscountService`/`DiscountsController`/`IDiscountRepository` — the
  consumer promotions endpoint is a pure additive read projection.
- `.claude/docs/` not updated — consistent with TASK-419/471/520 precedent (deferred to a
  documentation-writer pass once the full 3-task feature ships).

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `backend/ShelfGuard.Domain/Interfaces/IBannerRepository.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/BannerRepository.cs` (new)
- `backend/ShelfGuard.Application/Features/Banners/Dtos/BannerDtos.cs` (new)
- `backend/ShelfGuard.Application/Features/Banners/IBannerService.cs` (new)
- `backend/ShelfGuard.Application/Features/Banners/BannerService.cs` (new)
- `backend/ShelfGuard.Api/Controllers/BannersController.cs` (new)
- `backend/ShelfGuard.Application/Features/ConsumerContent/Dtos/ConsumerContentDtos.cs` (new)
- `backend/ShelfGuard.Application/Features/ConsumerContent/IConsumerContentRepository.cs` (new)
- `backend/ShelfGuard.Application/Features/ConsumerContent/IConsumerContentService.cs` (new)
- `backend/ShelfGuard.Application/Features/ConsumerContent/ConsumerContentService.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/ConsumerContentRepository.cs` (new)
- `backend/ShelfGuard.Api/Controllers/ConsumerContentController.cs` (new)
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` (2 registrations added)
- `backend/ShelfGuard.Application/DependencyInjection.cs` (2 registrations added)
- `.claude/logs/handoffs/521-to-mobile-developer_consumer-content-api.md` (new — API contract handoff)
