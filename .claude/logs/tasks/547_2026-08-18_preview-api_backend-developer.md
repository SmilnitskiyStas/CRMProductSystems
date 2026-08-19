# TASK-547 — Preview API

**Status:** done
**Agent:** backend-developer
**Date:** 2026-08-18

## What was built

`GET /api/v1/mobile/config/preview` — staff-only (`AtLeastEnterpriseAdmin`), `ITenantContext`-
resolved tenant. Returns the tenant's current draft with `theme` composed live, in the same
document shape `GET /api/v1/mobile/config` returns for a published config
(`hasDraft`/`schemaVersion`/`configVersion`/`tenant`/`theme`/`features`/`navigation`/`pages`).
No draft yet → `hasDraft: false` + empty/default body, still `200` (never a bare `404`).

New files:
- `backend/ShelfGuard.Api/Controllers/MobileConfigPreviewController.cs` — separate controller
  (not folded into `MobileConfigController`/`MobileConfigDraftController`), same sibling-route
  shape as `MobileConfigPublishController` (`api/v1/mobile/config/preview`). Deliberately its own
  controller rather than an action on `MobileConfigController`: that controller carries a
  controller-level `[AllowAnonymous]`, and ASP.NET Core skips the authorize check for an endpoint
  whenever ANY `AllowAnonymousAttribute` is present in its metadata — an action-level
  `[Authorize]` there would NOT reliably override it.
- `backend/ShelfGuard.Application/Features/MobileConfig/IMobileConfigPreviewService.cs` /
  `MobileConfigPreviewService.cs` — loads `MobileConfiguration` (+`DraftVersion`+`Theme`) via the
  existing `IMobileConfigurationRepository.GetByTenantIdAsync` (already runs under the caller's
  own tenant RLS, no `ITenantSessionOverride`), composes `theme` live via
  `MobileThemeJson.ToJsonObject` (reused, not re-implemented — the same helper
  `MobileConfigPublishService.ComposeTheme` uses at real publish time). Never calls
  `SaveChangesAsync` or any entity mutation method.
- Tests: `MobileConfigPreviewServiceTests.cs`, `MobileConfigPreviewControllerTests.cs`,
  `MobileConfigPreviewAuthorizationTests.cs` (all under `ShelfGuard.Tests/MobileConfig/`).

Modified:
- `backend/ShelfGuard.Application/DependencyInjection.cs` — one new `AddScoped` line registering
  `IMobileConfigPreviewService`.

No changes to `MobileConfigController.cs`, `MobileConfigPublishService.cs`, or
`MobileConfigDraftService.cs`.

## Authorization test approach

This repo has no `WebApplicationFactory` HTTP harness (confirmed — see
`RlsRoleGuardTests` remarks and grep of the test tree). `MobileConfigPreviewAuthorizationTests`
instead builds a real `IAuthorizationService` from `AppPolicies.Configure` (the same registration
`Program.cs` uses) and calls `AuthorizeAsync` against `AppPolicies.AtLeastEnterpriseAdmin` with a
`ClaimsPrincipal` shaped like a real consumer-session JWT (`AppRoles.Consumer` role claim, no
`tenant_id` claim) — asserts rejection. Also asserts rejection for an unauthenticated principal and
every staff role below `enterprise_admin`, and a positive control for `enterprise_admin`. A
reflection check confirms `MobileConfigPreviewController` carries
`[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]` and no `[AllowAnonymous]` anywhere in
its metadata. This exercises the real named policy end-to-end, not a bespoke role-string check.

## Verification actually performed this run

- `dotnet build ShelfGuard.sln -c Debug` — succeeded, 0 errors (1 pre-existing unrelated warning
  in `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln -c Debug --no-restore` — **1654 passed, 0 failed, 0 skipped**
  (includes the existing RLS/Postgres integration suites — DB was reachable in this environment).
- `dotnet test ... --filter "FullyQualifiedName~MobileConfig"` — **236 passed, 0 failed**,
  confirming `MobileConfigPublishedReadService`/RLS tests (the "never leaks draft content" guard
  for `GET /api/v1/mobile/config`) are unaffected by this change.
- No browser/manual/live verification was performed — build + test output only.
