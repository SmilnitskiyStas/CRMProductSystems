# TASK-551 — API versioning rollout for new endpoints (audit)

**Status:** done
**Agent:** backend-developer
**Outcome:** Audit only. No code changes — every checked area was already consistent.

## Scope audited

The 8 Stage B–E consumer-platform controllers: `MobileConfigController`,
`MobileConfigDraftController`, `MobileConfigPublishController`, `MobileConfigVersionsController`,
`MobileConfigPreviewController`, `MobileThemeController`, `MobileBlocksController`,
`RetailersController`.

## 1. Route prefix

Confirmed all 8 controllers use `[Route("api/v1/...")]`. Cross-checked the full controller list
(`grep [Route(` across `backend/ShelfGuard.Api/Controllers/`, 74 controllers) — these 8 are the
*only* ones using `api/v1/`; every other controller (including pre-Stage-6 consumer-facing ones
like `ConsumerContentController`, `ConsumerLoyaltyController`, `MobileAuthController`,
`BannersController`) stays on the unversioned `api/...` surface, matching decision 2's scope
(only new consumer-platform endpoints from Stage B onward are versioned). Cross-referenced against
Stage B–E task entries (TASK-534, 536, 538, 538b, 544, 545, 547, 548) — no controller from those
stages is missing the prefix, and no pre-existing endpoint was moved/aliased/renamed. No fix
needed.

## 2. Error shape

Every error response across the 8 controllers follows one of two shapes, and none deviate:
- Field-level validation failures → `{ errors: [{ field, message }] }`, backed by
  `MobileConfigValidationError(string Field, string Message)`
  (`MobileConfigDraftController.Save`, `MobileThemeController.Update`,
  `MobileConfigPublishController`/`MobileConfigVersionsController` for `ValidationFailed`).
- Everything else (not-found, forbidden, conflict, business errors) → `{ error: string }`
  (`MobileConfigController.GetConfig`, `MobileBlocksController.GetByType`,
  `RetailersController`'s four actions, `Publish`/`Rollback`'s non-validation branches).

No bare strings, no raw exception messages, no inconsistent key naming found. This matches
`BannersController`'s baseline `{ error }` convention for non-field errors. No fix needed.

## 3. Pagination

Checked the codebase's established convention: `PagedResult<T>`/`PagedQuery` in
`ShelfGuard.Application/Common/Pagination.cs` ("Paginated result envelope returned by all LIST
endpoints"), used by `ItemsController`, `CustomersController`, `SuppliersController`,
`ConsumerContentController`'s catalog browse, and `LoyaltyController`'s ledger history.

Three of the 8 controllers return unpaginated lists. Checked each against real growth bounds and
existing frontend consumers:

- **`GET /api/v1/mobile/blocks`** (`MobileBlocksController.GetAll`) — serves
  `BlockRegistry`, a compile-time-fixed catalog of block *types* (not tenant data). Genuinely
  small and bounded; growth requires a code change, not user action. No pagination needed.
- **`GET /api/v1/mobile/config/versions`** (`MobileConfigVersionsController.GetHistory`) —
  one tenant's own publish/rollback history. Bounded by how many times *that tenant* has
  published — realistically low hundreds even over years, and not multiplied across tenants
  (RLS-scoped). `frontend/features/consumer-app/api/mobileConfigVersions.ts` is already built and
  typed against a flat array (`Promise<MobileConfigVersionSummary[]>`). No pagination needed now;
  low-priority future candidate if a tenant's history ever grows unusually large.
- **`GET /api/v1/retailers`** (`RetailersController.GetRetailers`, via
  `LoyaltyService.GetAvailableNetworksAsync`) — this one is genuinely different: it loops over
  *every active tenant on the platform* with `HasModule("loyalty")`, not a bounded per-consumer
  or per-tenant set, plus does 2 extra queries per qualifying tenant (N+1-shaped). This is a real
  forward-looking scalability concern as the SaaS platform grows. However:
  - It is deliberately kept in lockstep with the **pre-existing** (TASK-405, non-Stage-6)
    `ConsumerLoyaltyController.GetNetworks`, which returns the exact same unpaginated list — the
    docstring is explicit this pairing must "never drift apart." Paginating one without the other
    breaks that invariant, and touching `ConsumerLoyaltyController` is out of this task's
    constraints (pre-existing, non-Stage-6 controller).
  - No frontends currently consume the plain list endpoint yet (checked `frontend/` and
    `mobile/` — only the separate `{slug}/public` lookup has a client so far).
  - Introducing `PagedResult<T>` here would be a wire-format-breaking API contract change,
    which is feature work coordinated with a frontend/mobile change, not an "audit" fix.
  - **Not fixed inline.** Flagged as a follow-up via `spawn_task` (see below) rather than
    silently ignored or force-fixed out of scope.

## 4. UTC dates

Traced every `DateTime`/`DateTimeOffset` field returned by the 8 controllers back to its entity
source:
- `MobileConfiguration.CreatedAt/UpdatedAt`, `MobileConfigurationVersion.CreatedAt/PublishedAt`,
  `MobileTheme.UpdatedAt` — all set from `DateTime.UtcNow` at creation/mutation time
  (`MobileConfiguration.cs`, `MobileConfigurationVersion.cs`, `MobileTheme.cs`). DTOs
  (`MobileConfigDraftDtos.cs`, `MobileConfigPublishDtos.cs`, `MobileConfigVersionHistoryDtos.cs`,
  `MobileThemeDtos.cs`) copy these values through unchanged — no `.ToLocalTime()` or similar
  anywhere in the mapping path.
- `LoyaltyMembership.JoinedAt` (returned by `RetailersController.Join`) — `DateTimeOffset.UtcNow`
  at the entity level (`LoyaltyMembership.cs:44`).
- `RetailerPublicInfoDto`/`LoyaltyNetworkSummaryDto` carry no date fields at all.

All consistent with `Tenant`/existing entity conventions. No fix needed.

Noted but out of scope: this codebase has no global Npgsql/System.Text.Json configuration that
forces `DateTime.Kind = Utc` on read from `timestamp without time zone` columns — a pre-existing,
codebase-wide characteristic (not introduced by Stage 6), so the 8 audited controllers are
consistent *with the existing convention*, which is what this task asked to verify.

## Fixes made

None. All 4 areas were already consistent; this is a valid "confirmed consistent" audit outcome
per the task's own Constraints section.

## Build/test

Not run — no code was changed, so build/test would be pointless (per task's own "When done"
instructions).

## Follow-up flagged (not actioned here)

Spawned a background task suggestion: revisit `GET /api/v1/retailers`'s unbounded
platform-wide tenant loop (and its pre-existing sibling `ConsumerLoyaltyController.GetNetworks`)
for pagination once either list has a real chance of growing large, or once a frontend/mobile
consumer for the plain list endpoint exists to coordinate the contract change with.
