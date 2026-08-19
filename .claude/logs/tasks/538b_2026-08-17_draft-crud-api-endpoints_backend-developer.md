# TASK-538b — Draft CRUD API endpoints

**Status:** done
**Agent:** backend-developer
**Date:** 2026-08-17

## What was done

- `MobileConfigDraftController.cs` (`backend/ShelfGuard.Api/Controllers/`) — new controller, thin
  HTTP wrapper around TASK-532's already-shipped `IMobileConfigDraftService`
  (`SaveDraftAsync`/`GetDraftAsync`); no draft orchestration logic added or touched.
  - `GET /api/v1/mobile/config/draft` — returns the tenant's current draft, or
    `MobileConfigDraftResponse.Empty()` (`HasDraft: false`) when none exists yet. Always `200`,
    never `404`.
  - `PUT /api/v1/mobile/config/draft` — accepts `{ configurationJson }`, calls `SaveDraftAsync`,
    returns the saved draft on success or `{ errors: [{ field, message }] }` (400) on validation
    failure — the exact shape `MobileThemeController` (TASK-536) already established, so
    `frontend/lib/api.ts`'s existing `ApiError.body` handling covers this endpoint for free.
  - `[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]`, tenant resolved via
    `ITenantContext` (never trusted from the request body) — same posture as
    `MobileThemeController`, deliberately a separate controller from the anonymous
    `MobileConfigController` (TASK-534's consumer-facing published-config read).
  - `actingUserId` is resolved from `ClaimTypes.NameIdentifier`/`"sub"`, same `GetUserId()` pattern
    `LoyaltyController`/`ChatController`/etc. already use in this codebase.
- `Dtos/MobileConfigDraftDtos.cs` — added two records (no changes to the existing
  `MobileConfigDraftDto`):
  - `SaveMobileConfigDraftRequest(string ConfigurationJson)` — the PUT body.
  - `MobileConfigDraftResponse` — the GET/PUT response envelope, with `FromDraft(...)` and
    `Empty()` factory methods. See Decisions below for why this is a new type rather than reusing
    `MobileConfigDraftDto` directly for the "no draft yet" case.
- `MobileConfigDraftControllerTests.cs` (`backend/ShelfGuard.Tests/MobileConfig/`) — 9 new
  controller-level unit tests (mocked `IMobileConfigDraftService`/`ITenantContext`, same
  `ControllerContext`/`ClaimsPrincipal` convention `MobileAuthControllerTests` already uses):
  403-when-no-tenant (GET and PUT), empty-vs-populated GET shape, structured-error PUT response,
  `actingUserId` threading from the authenticated claim (present and absent), and tenant isolation
  (a save for tenant A never targets tenant B, tenant id always comes from `ITenantContext`, never
  from the request). Draft orchestration itself (get-or-create, in-place mutation, validation
  rules) is already covered by the existing `MobileConfigDraftServiceTests` (mocked repo) and
  `MobileConfigDraftServiceRlsIntegrationTests` (real Postgres) — neither was touched or
  re-derived here, per the task's explicit constraint not to touch the service's internal logic.
- No DI registration needed: `IMobileConfigDraftService` was already registered
  (`ShelfGuard.Application/DependencyInjection.cs:153`, from TASK-532).

## Decisions

**Separate controller, not added to `MobileConfigController`.** The brief called this out
explicitly and investigating confirmed it: `MobileConfigController` is `[AllowAnonymous]` (TASK-534,
consumer/public reads of the *published* document); this endpoint is an authenticated staff/admin
write of the *draft*. Same reasoning `MobileThemeController`'s own remarks already document for
its own split from `MobileConfigController`. Route chosen as `/api/v1/mobile/config/draft` —
sibling to `MobileThemeController`'s `/api/v1/mobile/theme`, both nested under the same
`/api/v1/mobile` admin surface family.

**New `MobileConfigDraftResponse` type instead of reusing `MobileConfigDraftDto` for GET.**
`MobileConfigDraftDto`'s fields are non-nullable (`Guid MobileConfigurationId`, `DateTime
CreatedAt`, `string ConfigurationJson`) — there is no honest non-fabricated value for those when no
draft has ever been saved. The "no draft yet" case could not reuse `MobileThemeService`'s
"propose `CreateDefault()`'s built-in defaults" convention either: `MobileTheme.CreateDefault`
proposes plain color/radius defaults, but a valid `MobileConfigurationVersion.ConfigurationJson`
requires 2-5 real `navigation` items (`MobileConfigWhitelists.NavigationMinItems`) — fabricating
two fake navigation entries would be inventing retailer-facing product content, not a neutral
default. `MobileConfigDraftResponse` instead adds an explicit `HasDraft: false` flag with nullable
fields and no fabricated `ConfigurationJson`, so the future App Builder UI (TASK-539) can render an
empty canvas from a clear signal rather than parsing a half-fake document. `SchemaVersion` in the
empty response is still populated (from `MobileConfigWhitelists.CurrentSchemaVersion`) since that
one value has an unambiguous, non-content correct default.

**GET always returns 200, never 404**, per the task's explicit DoD line — matches the "propose
defaults on GET" convention already used by `MobileThemeService.GetThemeAsync` and
`LoyaltyService.GetSettingsAsync`.

**No controller-level (HTTP/WebApplicationFactory) test added.** Checked the existing test suite
for a precedent first: neither `MobileThemeController` nor `MobileConfigController` (this
controller's closest siblings, both from Stage C) has an HTTP-level integration test — the one
controller test file that exists in the whole repo, `MobileAuthControllerTests.cs`, uses the same
mocked-service-plus-`ControllerContext` unit-test style this task's new test file follows. Kept
consistent with that convention rather than introducing a new (WebApplicationFactory-based) test
style unilaterally for one controller.

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing, unrelated warning in
  `MarketplaceServiceTests.cs`, untouched by this task).
- `dotnet test ShelfGuard.sln` — 1563/1563 passed (1554 pre-existing + 9 new
  `MobileConfigDraftControllerTests`), re-run after the final edit.
- `git status` reviewed: this task's own changes are the new `MobileConfigDraftController.cs`, the
  two new records appended to the existing (already-uncommitted, TASK-532) `Dtos/
  MobileConfigDraftDtos.cs`, and the new `MobileConfigDraftControllerTests.cs`. No DI registration
  change was needed. `MobileConfigDraftService.cs` was not touched, per the task's constraint. The
  repo carries a large amount of pre-existing uncommitted state from earlier Stage 6 tasks
  (TASK-526-538) and older mobile-workstream files — none of it was touched by this task. Did not
  update `.claude/tasks/mobile-roadmap.md` (orchestrator's responsibility per the brief).

## Files

- `backend/ShelfGuard.Api/Controllers/MobileConfigDraftController.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/Dtos/MobileConfigDraftDtos.cs`
  (+ `SaveMobileConfigDraftRequest`, `MobileConfigDraftResponse`)
- `backend/ShelfGuard.Tests/MobileConfig/MobileConfigDraftControllerTests.cs` (new)

## Next

TASK-539 (`frontend-developer` — App Builder foundation, drag & drop canvas) can now call
`GET`/`PUT /api/v1/mobile/config/draft` to load and persist canvas state, using `HasDraft: false`
to detect the empty-canvas first-visit case and the `{ errors: [{ field, message }] }` shape for
inline per-field validation errors, matching TASK-537's frontend `ApiError.body` handling.
