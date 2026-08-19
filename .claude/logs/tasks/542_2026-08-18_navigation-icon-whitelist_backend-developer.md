# TASK-542 — Navigation Builder (backend half: icon whitelist)

**Agent:** backend-developer
**Date:** 2026-08-18
**Status:** done (backend portion only — frontend Navigation Builder UI is a separate agent run)

## Context loaded

`.claude/agents/backend-developer.md`, `CLAUDE.md`, `.claude/tasks/mobile-roadmap.md` TASK-542
entry (Stage 6, Stage C), `MobileConfigWhitelists.cs` and `MobileConfigValidator.cs` (TASK-532),
`mobile/features/mobile-config/validation.ts` (authoritative icon enum source), the already-shipped
`contracts/mobile-config.schema.json` + `MobileConfigSchemaContractTests.cs` lockstep pattern from
TASK-533/536.

## What was built

TASK-532 already enforced navigation item count (2–5) and `type` whitelisting, but left `icon` as
"any non-empty string" by explicit design note ("icon whitelisting is TASK-542's concern"). This
task closes that gap using the exact icon set already live on the mobile client — no new list was
invented.

- **`backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigWhitelists.cs`** — added
  `NavigationIcons` (`home`, `tag`, `grid`, `qr`, `ticket`, `map`, `news`, `user`), matching
  `mobile/features/mobile-config/validation.ts` line ~110 exactly. Updated `NavigationTypes`'s doc
  comment (removed the now-stale "icon whitelisting is TASK-542's concern" note).
- **`backend/ShelfGuard.Application/Features/MobileConfig/MobileConfigValidator.cs`** —
  `ValidateNavigation` now checks `icon` against `MobileConfigWhitelists.NavigationIcons` the same
  way it already checks `type` against `NavigationTypes`, reporting `navigation[N].icon` with a
  precise message (`"Unknown navigation icon '{value}'."` or a required/type error) instead of the
  old bare `RequireString` type-only check. Class-level doc comment updated to list
  `navigation[].icon` among the whitelist-enforced fields.
- **`contracts/mobile-config.schema.json`** — `navigation.items.properties.icon` changed from
  `{"type": "string", "minLength": 1}` to `{"enum": ["home","tag","grid","qr","ticket","map","news","user"]}`,
  with a description pointing at `MobileConfigWhitelists.NavigationIcons`. Updated the `navigation`
  property description and the top-level file description (same "was free string pending TASK-XXX,
  now resolved" pattern already used for `theme.spacing`/TASK-536).
- **`backend/ShelfGuard.Tests/MobileConfig/MobileConfigSchemaContractTests.cs`** — added
  `Navigation_icons_match_whitelist`, asserting the schema's `navigation.items.properties.icon.enum`
  equals `MobileConfigWhitelists.NavigationIcons` exactly, same shape as the existing
  `Navigation_types_match_whitelist`/`Theme_spacing_enum_matches_theme_whitelist` tests. Updated the
  class summary to list `navigation[].icon` among the compared fields.
- **`backend/ShelfGuard.Tests/MobileConfig/MobileConfigValidatorTests.cs`** — added:
  - `Validate_accepts_every_whitelisted_navigation_icon` (`[Theory]`, all 8 icons individually).
  - `Validate_rejects_unknown_navigation_icon` — asserts failure and a `navigation[0].icon` error
    containing the offending value.
  - `Validate_rejects_navigation_item_missing_icon` — asserts a `navigation[0].icon` error when the
    field is absent.

No entities, endpoints, or UI changed — validator/whitelist/contract/tests only, per scope.

## Verification

- `dotnet build ShelfGuard.sln` — **0 errors**, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, nullable dereference, not touched by this task).
- `dotnet test ShelfGuard.sln --no-build` (full suite, re-run after all edits) —
  **1574/1574 passed**, 0 failed, 0 skipped. Includes the 3 new validator tests (8 theory cases +
  2 facts) and the 1 new schema-contract test.
- Confirmed no other backend test fixture used a non-whitelisted icon value (grepped every
  `"icon":` literal across `backend/ShelfGuard.Tests/MobileConfig/**` and
  `backend/ShelfGuard.Tests/Infrastructure/MobileConfig*RlsIntegrationTests.cs`) — all pre-existing
  fixtures already use `"home"`/`"user"`, so nothing needed updating and nothing regressed.
- `git status` after all edits shows exactly the 5 files above changed (all report `??` because the
  whole `MobileConfig` feature tree, `MobileConfigController.cs`/`MobileConfigDraftController.cs`,
  the `MobileConfiguration*` domain/migration files, etc. are pre-existing uncommitted work from
  TASK-527–541, not something this task touched or introduced).

## Publish round-trip note

Per the brief's constraint, "Publish" round-trip (TASK-544, Draft→Preview→Validate→Publish
generalized beyond `Banner`) doesn't exist yet, so it isn't testable now. Only the Draft CRUD
(TASK-538b) path was in scope and is covered by the existing
`MobileConfigDraftServiceTests`/`MobileConfigDraftControllerTests`/RLS integration tests, none of
which needed changes since their icon fixtures were already whitelisted values. Once TASK-544 lands
on the same `MobileConfigurationVersion.ConfigurationJson` document shape, the icon whitelist
enforced here applies automatically — no extra work anticipated.

## Scope discipline

Exactly the 5 files predicted by the brief: `MobileConfigWhitelists.cs`, `MobileConfigValidator.cs`,
`contracts/mobile-config.schema.json`, `MobileConfigSchemaContractTests.cs`,
`MobileConfigValidatorTests.cs`. No new entities, endpoints, or frontend/mobile files touched — the
Navigation Builder UI is left for the frontend-developer half of TASK-542.
