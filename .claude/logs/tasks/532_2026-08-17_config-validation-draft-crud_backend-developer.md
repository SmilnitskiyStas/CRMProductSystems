# TASK-532 — Config JSON validation service + Draft CRUD

**Agent:** backend-developer
**Date:** 2026-08-17
**Status:** done

## Scope

Whitelist-based validator for `MobileConfigurationVersion.ConfigurationJson` plus Application-layer
Draft CRUD on top of TASK-531's `MobileConfiguration`/`MobileConfigurationVersion` entities. No API
controller, no publish flow — those are TASK-534/544.

## What was built

- `backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigWhitelists.cs` — single source
  of truth for schemaVersion/feature-key/navigation-type/page-name/block-type whitelists, kept
  dependency-free so TASK-533's `/contracts/mobile-config.schema.json` can reuse the same constants.
- `IMobileConfigValidator` / `MobileConfigValidator` — walks the parsed JSON manually (not typed-DTO
  deserialization) so every rejection carries a precise field path (`features.unknownKey`,
  `navigation[2].type`, `pages.home.blocks[0].props`) and message. Enforces navigation's min-2/max-5
  item count at this layer (not deferred to publish time).
- `Dtos/MobileConfigValidationDtos.cs` — `MobileConfigValidationError(Field, Message)` +
  `MobileConfigValidationResult`.
- `IMobileConfigDraftService` / `MobileConfigDraftService` — `SaveDraftAsync` (get-or-create the
  tenant's `MobileConfiguration` row, validate, then create-or-mutate-in-place the draft
  `MobileConfigurationVersion`) and `GetDraftAsync`.
- `Dtos/MobileConfigDraftDtos.cs` — `MobileConfigDraftDto`.
- `ShelfGuard.Domain/Interfaces/IMobileConfigurationRepository.cs` +
  `ShelfGuard.Infrastructure/Data/Repositories/MobileConfigurationRepository.cs` — same shape as
  `IBannerRepository`/`BannerRepository`; `GetByTenantIdAsync` eager-loads `DraftVersion`.
- DI: `IMobileConfigValidator`/`IMobileConfigDraftService` registered in
  `ShelfGuard.Application/DependencyInjection.cs`; `IMobileConfigurationRepository` in
  `ShelfGuard.Infrastructure/DependencyInjection.cs` — both follow the existing `AddScoped`
  convention next to `IBannerService`/`IBannerRepository`.

## Design decisions (documented in `.claude/docs/domain-model.md`)

1. **Theme composition timing** — the validated `ConfigurationJson` has no `theme` key at draft
   time (rejected as an unknown field if present). A copy is composed into
   `ConfigurationJson.theme` only at publish time (TASK-544), matching what TASK-531's entity doc
   comments already described. Consequence for TASK-534: `GET /api/v1/mobile/config` reads `theme`
   straight off the published version's stored JSON — no `MobileTheme` join needed at read time.
2. **Draft update-in-place** — `SaveDraftAsync` mutates the existing draft version via
   `UpdateConfigurationJson()` on every save rather than minting a new version row per edit — same
   shape as `Banner.Update()` staying separate from `Banner.Publish()`. A new version number is only
   allocated on first-ever draft or after a real publish (TASK-544) starts a fresh draft.

## Tests

`backend/ShelfGuard.Tests/MobileConfig/`:
- `MobileConfigValidatorTests.cs` (23 tests) — valid payload accepted; malformed JSON / non-object
  root; every whitelist violation (unknown top-level/nav-item/block key, unsupported schemaVersion,
  unknown feature key, non-boolean feature value, nav count <2/>5, unknown nav type, missing
  label, unknown page name, missing blocks, unknown block type, empty id, non-integer order,
  non-object props); multi-page/multi-block acceptance case.
- `MobileConfigDraftServiceTests.cs` (9 tests) — validation-failure short-circuit persists nothing;
  create-when-absent; update-in-place (no new version, no `GetMaxVersionNumberAsync` call); get
  returns null pre-configuration and pre-draft; full create→update→read round trip; tenant
  isolation (two tenants' draft operations never cross).

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.Tests/ShelfGuard.Tests.csproj` — 1443/1443 passed (1411 pre-existing +
  32 new), including the full integration suite.
- `git status` reviewed: my changes are limited to the new `Features/MobileConfig/` directory, the
  new `Tests/MobileConfig/` directory, the two `IMobileConfigurationRepository`/
  `MobileConfigurationRepository` files, the two `DependencyInjection.cs` registration edits, and
  the `domain-model.md` note. All other working-tree changes present (TASK-527/528/531 and the
  large pre-existing mobile/doc backlog) predate this task and were left untouched.

## Next

TASK-533 (`/contracts/mobile-config.schema.json`, reusing `MobileConfigWhitelists`) and TASK-534
(`GET /api/v1/mobile/config`) can proceed. No API controller exists yet for Draft CRUD by design —
wire it up once an admin UI consumer exists (Stage C).
