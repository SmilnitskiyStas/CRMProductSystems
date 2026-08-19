# TASK-533 — Canonical /contracts/mobile-config.schema.json

**Status:** done
**Agent:** backend-developer
**Date:** 2026-08-17

## What was done

- Authored `/contracts/mobile-config.schema.json` at the repo root: canonical JSON Schema for the
  full document `GET /api/v1/mobile/config` (TASK-534) will serve — `schemaVersion`, `configVersion`,
  `tenant` (id/slug/name/logoUrl), `theme`, `features`, `navigation`, `pages`.
  `additionalProperties: false` at every whitelisted level.
- Added `backend/ShelfGuard.Tests/MobileConfig/MobileConfigSchemaContractTests.cs` (7 tests):
  loads the schema file at runtime (walks up from `AppContext.BaseDirectory` to find the repo
  root, no hardcoded relative path) and asserts, via plain `JsonDocument` navigation (no new NuGet
  dependency), that its `schemaVersion`/`features`/`navigation.type`/`navigation` min-max/
  `pages`/`block.type` values are set-equal to `MobileConfigWhitelists.cs`'s constants.

## Key decision: Draft 07, not 2020-12

Deviated from the task's "Draft 2020-12 unless the mobile audit says otherwise" default, per that
same clause's escape hatch — a real incompatibility was found:
`mobile/features/mobile-config/validation.ts` instantiates `new Ajv(...)` (the package's *default*
export). AJV v8's default `Ajv` class only ships Draft 07's meta-schema; Draft 2019-09/2020-12
require importing `ajv/dist/2019` / `ajv/dist/2020` instead (`Ajv2019`/`Ajv2020`), which mobile does
not do anywhere, and `ajv-formats` isn't installed either. A 2020-12-authored schema handed to that
default instance would fail at compile time (unrecognized `$schema`, or unknown keywords like
`$defs`/`prefixItems`). So this file targets Draft 07 (`"$schema": "http://json-schema.org/draft-07/schema#"`,
`definitions`+`$ref` not `$defs`, no `format` keyword) — actually loadable by mobile's current AJV
setup with zero mobile-side changes. `docs/mobile/MOBILE_CURRENT_STATE.md` §12 only says "AJV is
installed, JSON Schema can be the canonical boundary" — it doesn't call out draft version, so this
wasn't contradicting an explicit finding there, just filling a gap the audit hadn't drilled into.
Full reasoning is also inlined in the schema file's own top-level `description`.

## Theme sourcing

`theme` was built from `ShelfGuard.Domain/Entities/MobileTheme.cs`'s typed fields (`LogoUrl`,
`PrimaryColor`/`SecondaryColor`/`BackgroundColor`/`SurfaceColor`/`TextPrimaryColor`/
`TextSecondaryColor`, `ButtonRadius`, `CardRadius`, `SpacingPreset`) plus CLAUDE CODE SPEC §10
(ЕТАП 6 Theme Editor field list: Logo/Primary/Secondary/Background/Surface/Text/Button
radius/Card radius/Spacing preset) and MASTER SPEC §7's `colors`/`buttons`/`cards`/`spacing`
response shape — **not** copied from `MobileConfigWhitelists.cs` (no theme concept there by
design, confirmed against its own doc comment) nor from mobile's `validation.ts` placeholder.

One open gap, flagged rather than guessed: `MobileTheme.SpacingPreset` has no authoritative
whitelist yet in backend code — TASK-536 ("Theme domain validation") owns that and hasn't shipped.
Left `theme.spacing` as a free non-empty string (no `enum`) rather than inventing a value set;
noted in the schema's `description` that TASK-536 should tighten this and reconcile it with
mobile's own current placeholder guess (`validation.ts` guesses `['compact','comfortable']`).
`theme.spacing`/radii/colors are therefore not covered by the whitelist-agreement test — there is
no backend whitelist for them to agree with yet.

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln` — 1450/1450 passed (1443 pre-existing + 7 new).
- `git status` reviewed: only `contracts/mobile-config.schema.json` (new) and
  `backend/ShelfGuard.Tests/MobileConfig/MobileConfigSchemaContractTests.cs` (new) were added by
  this task. The repo carries a large amount of pre-existing untracked/modified state from earlier
  sessions (mobile Stage 1-4 work, TASK-526/527/528/531/532 files, docs, etc.) — none of it was
  touched here.

## Files

- `contracts/mobile-config.schema.json` (new)
- `backend/ShelfGuard.Tests/MobileConfig/MobileConfigSchemaContractTests.cs` (new)

## Next

TASK-534 (`backend-developer` — `GET /api/v1/mobile/config`) can consume this schema/validator
pairing. No `.claude/tasks/mobile-roadmap.md` update performed here per instruction — orchestrator
handles that.
