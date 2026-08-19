# TASK-534 — GET /api/v1/mobile/config

**Status:** done
**Agent:** backend-developer
**Date:** 2026-08-17

## What was done

- `MobileConfigController` (`backend/ShelfGuard.Api/Controllers/MobileConfigController.cs`) — new
  `GET /api/v1/mobile/config` endpoint, the first route under the new `/api/v1/` prefix.
- `MobileConfigPublishedReadService` + `IMobileConfigPublishedReadService`
  (`backend/ShelfGuard.Application/Features/MobileConfig/`) — resolves the tenant's current
  `MobileConfigurationVersion` with `Status == Published` only, composes the full response document,
  computes a stable ETag.
- `MobileConfigReadDtos.cs` (same folder) — `MobileConfigReadErrorType` (`TenantNotFound` /
  `NoPublishedConfiguration`) + `MobileConfigReadError`.
- `IMobileConfigurationRepository.GetPublishedByTenantIdAsync` + its
  `MobileConfigurationRepository` implementation — untracked read (`.Include(PublishedVersion)`,
  `.Include(Theme)`), never includes `DraftVersion`.
- DI registration in `ShelfGuard.Application/DependencyInjection.cs`.
- Tests: `MobileConfigPublishedReadServiceTests.cs` (8 mocked-repo unit tests — document shape,
  tenant-not-found, no-published-config, draft-only-never-leaks, defensive non-Published-status
  pointer, theme fallback, ETag stability/change, `ITenantSessionOverride` call verification) +
  `MobileConfigPublishedReadRlsIntegrationTests.cs` (4 live-Postgres tests — published document
  under a real anonymous RLS session, draft-only never leaks, unknown tenant, two-tenant isolation).
- `.claude/docs/domain-model.md` — new "TASK-534" subsection under Mobile Configuration domain;
  corrected TASK-532's "theme composition timing" bullet, which had assumed TASK-534 would read
  `theme` straight from `ConfigurationJson` (that assumed TASK-544 already existed; it doesn't).

## Decisions

**Tenant transport** (the actual blocker this task exists to unblock — `docs/mobile/
MOBILE_CURRENT_STATE.md` §8/§15/§12 lists it explicitly): route stays literally
`GET /api/v1/mobile/config`, spec-compliant, no tenant path segment. `tenantId` travels as an
explicit `?tenantId=` query parameter, resolved through `ITenantSessionOverride` — same mechanism
`ConsumerContentController`'s `{tenantId}` route segment already uses, since a consumer/anonymous
session structurally never carries an `app.tenant_id` claim (`TenantConnectionInterceptor`: a fully
unauthenticated request RESETs every `app.*` session variable). Followed the brief's recommended
option as-is; no reason found to deviate.

**Auth:** `[AllowAnonymous]`, same posture as `ConsumerContentController`. MASTER SPEC §12's
"discover before joining" flow needs this to work with zero consumer JWT, and the document is
identical for every viewer of a given tenant — no reason to require one.

**Theme sourcing (supersedes TASK-532's forward-looking note, load-bearing for TASK-544):** TASK-532
predicted this endpoint would read `theme` straight off the published version's `ConfigurationJson`
once TASK-544's publish flow bakes it in there. TASK-544 doesn't exist yet, so this endpoint
composes `theme` **live** from the tenant's `MobileTheme` row on every call instead — completely
independent of `ConfigurationJson`. Falls back to `MobileTheme.CreateDefault`'s built-in defaults
when the tenant has no `MobileTheme` row yet (nothing auto-creates one alongside
`MobileConfiguration` today — that's meant to happen via the not-yet-shipped Theme Editor,
TASK-536). Documented explicitly in `domain-model.md` and in
`MobileConfigPublishedReadService`'s own class remarks so whoever builds TASK-544 makes one
deliberate choice (keep reading live vs. switch to trusting `ConfigurationJson.theme`) instead of
silently double-composing or breaking this endpoint.

**ETag:** strong ETag = SHA-256 hex of the exact served JSON string (not just `Id`+`Version`) —
deliberately covers the live-composed theme too, so a future independent theme edit that doesn't
mint a new version still changes the ETag instead of a false `304`.

**Response construction:** built server-side as a `System.Text.Json.Nodes.JsonObject` (parses the
stored `ConfigurationJson`, deep-clones `features`/`navigation`/`pages`, merges in `tenant`/`theme`/
`configVersion`), then served via `Content(json, "application/json")` verbatim — guarantees the ETag
hash and the served bytes are byte-identical (no risk of a second serialization pass producing
different output than what was hashed).

**Error disclosure:** matches `ConsumerContentController`'s existing level — distinguishes "tenant
not found" from "no published configuration yet" (not merged into one generic message), since that
controller already discloses the same distinction for the same public tenant space.

## Bug found, NOT fixed here (out of this task's scope)

While building the live-Postgres integration test, found that `MobileConfigDraftService.
SaveDraftAsync`'s (TASK-532) create-new-`MobileConfiguration` branch sets
`config.DraftVersionId = draftVersion.Id` and saves both brand-new rows in **one**
`SaveChangesAsync()` call. Against real Postgres, EF Core throws `circular dependency detected` —
`MobileConfigurationVersion.MobileConfigurationId` and `MobileConfiguration.DraftVersionId` each
require the other row inserted first, and EF can't resolve an order for two brand-new rows in one
batch. TASK-532's own tests never caught this because `MobileConfigDraftServiceTests.cs` mocks
`IMobileConfigurationRepository` (never touches real EF `SaveChanges`). Practical effect: the first
draft save for any tenant with no existing `MobileConfiguration` row would 500 in production today.

Hit the identical shape in this task's own test seeding (for `PublishedVersionId`/`DraftVersionId`
respectively) and fixed it there by splitting into two `SaveChangesAsync` calls — insert both rows
with the pointer null, then set the pointer and save again. Left `MobileConfigDraftService.cs`
itself untouched (outside this task's file scope) and flagged it as a separate background task
(`spawn_task`, title "Fix circular-dependency crash on first mobile-config draft save") instead of
silently fixing it inline.

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln` — 1462/1462 passed (1450 pre-existing + 12 new: 8 unit +
  4 live-Postgres integration). Postgres was reachable in this environment, so the 4 RLS
  integration tests actually executed against real dev Postgres (docker-compose, port 5435), not
  skipped.
- `git status` reviewed: this task's own changes are the new `MobileConfigController.cs`, the new
  `Features/MobileConfig/{Dtos/MobileConfigReadDtos.cs, IMobileConfigPublishedReadService.cs,
  MobileConfigPublishedReadService.cs}` files, the `GetPublishedByTenantIdAsync` addition to
  `IMobileConfigurationRepository.cs`/`MobileConfigurationRepository.cs`, the DI registration lines
  in `Application/DependencyInjection.cs`, the two new test files, and the `domain-model.md` note.
  The repo carries a large amount of pre-existing uncommitted state from the same day's earlier
  Stage 6 tasks (TASK-527/528/531/532/533) plus older mobile-workstream files — none of it was
  touched by this task.

## Files

- `backend/ShelfGuard.Api/Controllers/MobileConfigController.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/IMobileConfigPublishedReadService.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigPublishedReadService.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/Dtos/MobileConfigReadDtos.cs` (new)
- `backend/ShelfGuard.Application/DependencyInjection.cs` (+ registration)
- `backend/ShelfGuard.Domain/Interfaces/IMobileConfigurationRepository.cs` (+ method)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/MobileConfigurationRepository.cs` (+ method)
- `backend/ShelfGuard.Tests/MobileConfig/MobileConfigPublishedReadServiceTests.cs` (new)
- `backend/ShelfGuard.Tests/Infrastructure/MobileConfigPublishedReadRlsIntegrationTests.cs` (new)
- `.claude/docs/domain-model.md` (new TASK-534 subsection + corrected TASK-532 bullet)

## Next

Stage B (Stage 6 of the mobile roadmap) is complete. Orchestrator marks the roadmap entry `done`
and summarizes Stage B. TASK-535 (`frontend-developer` — expand `/consumer-app` into full Retailer
Admin shell) can proceed; it only depends on this task. TASK-544 (generalized publish flow) must
read this task's "Theme sourcing" decision before implementation. A separate follow-up task is
pending for the `MobileConfigDraftService` circular-dependency bug found above.
