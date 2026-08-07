# TASK-481: Category/losses product drill-down endpoints

**Agent:** backend-developer
**Date:** 2026-08-07
**Status:** done

## Context

Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md` (interactive analytics + margin
initiative). Depends on TASK-480 (`AnalyticsAuthorization.CanViewMargin`, done) and TASK-479 (DB
index, done — no direct code dependency for this task). Blocks TASK-483 (frontend).

## Done

Two new GET endpoints on the existing `AnalyticsController.cs`, both behind the controller's
existing class-level `[Authorize(Policy = AppPolicies.AnalyticsViewOrCapability)]` — no per-action
attributes added, reused `ResolveTenantId()`/`IsProvider()`/`ResolveDateRange()` exactly as every
other action does.

**A. `GET /api/analytics/by-category/products`** — `category_id` (null = "uncategorized" bucket,
matching `GetByCategoryAsync`'s existing null-key convention, not "all categories"), `store_id`,
`from`/`to`. New repo method `GetCategoryProductBreakdownAsync` merges `GetByCategoryAsync`'s stock
rollup and `GetPosTopProductsAsync`'s sales rollup, both re-scoped to one category and grouped by
`ProductId` instead of by category. Controller resolves `includeMargin` via
`AnalyticsAuthorization.CanViewMargin(User)` and passes the bool down — service/repository stay
authorization-agnostic, matching the plan's brief. Margin (`SalesRevenue − UnitsSold ×
Item.PricePurchase`) is looked up only when `includeMargin` is true; kept `null` (not `0`)
separately for "not authorized" vs. "no `PricePurchase` on file" — two distinct cases per the DTO's
own doc comment.

**B. `GET /api/analytics/losses/by-product`** — `store_id`, `reason`, `from`/`to`, all independent
optional AND-filters serving both the by-store and by-reason drill-downs through one endpoint. New
repo method `GetLossesByProductAsync` filters `WriteOffs`, joins `WriteOffItems` (subquery-`Contains`
join, same shape as `GetPosTopProductsAsync`'s tx/item join), groups by `ProductId`. **No margin
gate** — `LossAmount` is already shown in aggregate to every store_manager+ today (ADR-027 §1), so a
per-product breakdown isn't a new sensitivity; deliberately did not add a `CanViewMargin` check here.
One implementation note beyond the brief: `reason == "other"` matches `Reason == null OR Reason ==
"other"`, mirroring `GetWriteOffAnalyticsAsync`'s own `w.Reason ?? "other"` display bucket in this
same file — without it, drilling into the "other" bucket shown elsewhere would silently return zero
rows for any write-off with no reason recorded.

New DTOs in `AnalyticsDtos.cs`: `CategoryProductBreakdownDto`/`CategoryProductRowDto`,
`LossesByProductDto`/`LossByProductRowDto` — exact shapes from the brief. Thin pass-through methods
added to `IAnalyticsService`/`AnalyticsService.cs` (no business logic beyond the repository call, same
as every existing method in that file).

## Tests

Added to `backend/ShelfGuard.Tests/Analytics/PosAnalyticsServiceTests.cs` (only existing test file
under `Analytics/`, `Substitute.For<IAnalyticsRepository>()`-based service-level tests — no
repository-level EF test infra exists for this feature, consistent with the file's own established
shape). 6 new facts:
- Endpoint A: delegation (normal shape), null `category_id` forwarded as the uncategorized bucket,
  and the key margin-authorization test — constructs `ClaimsPrincipal`s the same way
  `AnalyticsAuthorizationTests.MakeUser` does, resolves `AnalyticsAuthorization.CanViewMargin` for
  store_manager (false) and network_manager (true) exactly as the controller will, drives the
  service call with each resulting bool, and asserts `MarginAmount`/`MarginPercent` come back null
  for store_manager and populated for network_manager — plus that the repository was called with
  the correct bool each time.
- Endpoint B: delegation, store/reason filter forwarding, and a test proving there is no margin gate
  by construction (`LossByProductRowDto` has no margin fields at all, and the method signature has
  no `includeMargin`/`ClaimsPrincipal` parameter) — first confirms the two roles really do differ on
  `CanViewMargin` (so the claim isn't vacuous), then shows the endpoint call path never consults it.

Followed the file's own established convention of field-level assertions (not whole-DTO
`Assert.Equal`) for DTOs with list properties — `record` equality on an `IReadOnlyList<T>` property
falls back to reference equality, and the file's existing `GetPosTopProductsAsync` tests already
avoid it for the same reason.

## Build/test

- `dotnet build` — 0 errors (same 1 pre-existing unrelated warning as TASK-480,
  `MarketplaceServiceTests.cs`).
- `dotnet test` — 1329/1329 green (1323 baseline + 6 new), no regressions.

## Not in scope (per brief)

No changes to `PosAnalyticsDtos.cs`, no `pos/products/{productId}/trend` endpoint (TASK-482, a
separate future agent run on these same files), nothing under `frontend/`, and no changes to
`AnalyticsAuthorization.cs`/`TenantRoleCapabilities.cs` (TASK-480, only consumed `CanViewMargin` here).

## Files

- `backend/ShelfGuard.Application/Features/Analytics/Dtos/AnalyticsDtos.cs`
- `backend/ShelfGuard.Application/Features/Analytics/IAnalyticsRepository.cs`
- `backend/ShelfGuard.Application/Features/Analytics/IAnalyticsService.cs`
- `backend/ShelfGuard.Application/Features/Analytics/AnalyticsService.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs`
- `backend/ShelfGuard.Api/Controllers/AnalyticsController.cs`
- `backend/ShelfGuard.Tests/Analytics/PosAnalyticsServiceTests.cs`
