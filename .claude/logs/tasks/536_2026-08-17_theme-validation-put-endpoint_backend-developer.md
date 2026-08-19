# TASK-536 — Theme domain validation + PUT endpoints

**Status:** done
**Agent:** backend-developer
**Date:** 2026-08-17

## What was done

- `MobileThemeWhitelists.cs` (`backend/ShelfGuard.Application/Features/MobileConfig/`) — single
  source of truth for `MobileTheme`'s writable-field whitelist: `SpacingPresets` (`compact`/
  `comfortable`), button/card radius bounds (0-32 / 0-40), `MaxLogoUrlLength` (2048), hex-color
  pattern. Mirrors `MobileConfigWhitelists`'s role but for the theme domain (TASK-532/533
  deliberately gave that class no theme concept).
- `Dtos/MobileThemeDtos.cs` — `MobileThemeDto` (`UpdatedAt` nullable — null means "never saved,
  these are `MobileTheme.CreateDefault`'s built-in defaults") and `UpdateMobileThemeRequest`.
- `IMobileThemeValidator` / `MobileThemeValidator.cs` — validates `UpdateMobileThemeRequest`
  against `MobileThemeWhitelists`: 6 hex-color fields, button/card radius bounds, spacing-preset
  whitelist, optional `logoUrl` (null allowed; non-null must be absolute http/https, length-bounded).
  Reuses `MobileConfigValidationResult`/`MobileConfigValidationError` so callers get the exact same
  "which field, what was wrong" per-field error style `MobileConfigValidator` (TASK-532) established.
- `IMobileThemeService` / `MobileThemeService.cs` — `GetThemeAsync` (proposes
  `MobileTheme.CreateDefault`'s defaults, unsaved, when no row exists — same convention
  `LoyaltyService.GetSettingsAsync` uses) and `UpdateThemeAsync` (validate, then create-or-update the
  tenant's one `MobileTheme` row; lazily creates the `MobileConfiguration` root too if the tenant has
  never touched it).
- `IMobileConfigurationRepository` / `MobileConfigurationRepository.cs` — `GetByTenantIdAsync` now
  also `.Include(c => c.Theme)` (harmless one-to-one join, existing draft-only callers unaffected);
  added `AddThemeAsync`/`UpdateTheme`.
- `MobileThemeController.cs` (`backend/ShelfGuard.Api/Controllers/`) — new controller,
  `GET`/`PUT api/v1/mobile/theme`, `[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]`,
  `ITenantContext`-resolved tenant. Deliberately a separate controller from `MobileConfigController`
  (that one is `[AllowAnonymous]` for anonymous consumer reads — a very different security posture).
  Same GET-proposes-defaults / PUT-upserts shape as `LoyaltySettingsController`
  (`api/settings/loyalty`), the assigned structural template.
- DI registration in `ShelfGuard.Application/DependencyInjection.cs`.
- **`theme.spacing` gap closed:** `contracts/mobile-config.schema.json`'s `theme.spacing` changed
  from a free non-empty string to `"enum": ["compact", "comfortable"]` — see Decisions below.
  `MobileConfigSchemaContractTests.cs` gained a new `Theme_spacing_enum_matches_theme_whitelist`
  test extending the existing schema-agreement pattern to this second whitelist source.
- Tests: `MobileThemeValidatorTests.cs` (30 tests — every field's valid/boundary/invalid cases,
  hex-color format variants, radius bounds inclusive, every whitelisted spacing preset, logoUrl
  null/empty/malformed-scheme/oversized, multi-field simultaneous rejection), `MobileThemeServiceTests.cs`
  (9 mocked-repo tests — GET default-proposal with/without a `MobileConfiguration` row, GET of a
  persisted theme, PUT validation short-circuit, PUT create-both-rows, PUT create-theme-only,
  PUT update-in-place, tenant isolation), `MobileThemeServiceRlsIntegrationTests.cs` (2 live-Postgres
  tests — see Decisions below).

## Decisions

**Immediate-live-effect gap — explicitly documented, not papered over (per the task brief's mandate):**
`MobileTheme` is one row per `MobileConfiguration` (i.e. per tenant), not draft/published-versioned
the way `MobileConfigurationVersion` is (TASK-531's design — no separate "draft theme" row exists to
target). `MobileConfigPublishedReadService`'s already-shipped, already-public
`GET /api/v1/mobile/config` (TASK-534) reads `theme` **live** off this exact same row on every
request, not from any published snapshot, because TASK-544 (generalized Draft→Preview→Publish
reconciliation for theme) doesn't exist yet. Consequence: **every successful `PUT` through this
task's endpoint takes effect in production immediately**, for every consumer currently viewing that
tenant's app — no draft/publish protection at all, contradicting MASTER SPEC's "production is not
affected until Publish" principle for this one part of the document. This is the same behavior TASK-534
already flagged for reads; this task is the first to make it write-reachable. Did NOT invent a
parallel mini-versioning mechanism for theme alone to satisfy the roadmap's literal "PUT updates the
draft MobileTheme only" DoD line — per the task brief, that would duplicate work TASK-544 needs to do
properly (composing theme into `ConfigurationJson` at publish time) and risks conflicting with
whatever shape TASK-544 lands on. Documented in `MobileThemeService`'s class remarks,
`IMobileThemeService`'s interface remarks, and `MobileThemeController`'s class remarks — three
separate places a future reader (especially TASK-537's and TASK-544's authors) will land on.

**`theme.spacing` whitelist resolved as `['compact', 'comfortable']`, not the brief's illustrative
"e.g. compact/comfortable/spacious":** found a concrete, already-shipped precedent —
`mobile/features/mobile-config/types.ts`'s `RetailThemeConfig.spacing` union
(`'compact' | 'comfortable'`) and `mobile/features/mobile-config/validation.ts`'s AJV
`enum: ['compact', 'comfortable']` — both predate this task and are exactly what
`contracts/mobile-config.schema.json`'s own top-level description flagged as needing reconciliation
("mobile's own current placeholder guess ... uses ['compact','comfortable']"). Matched that existing
two-value set instead of inventing a third ("spacious") the mobile client doesn't know about and
would reject client-side even if the backend accepted it. `MASTER SPEC §7`'s example
(`"spacing": "comfortable"`) is consistent with either choice, so it didn't disambiguate on its own.

**Button/card radius bounds (0-32 / 0-40):** no spec document states a numeric bound (MASTER SPEC §7
and CLAUDE CODE SPEC §10 only name the fields, not limits). The one concrete precedent in the repo is
`mobile/features/mobile-config/validation.ts`'s own (separate, currently-unwired prototype) AJV
schema, which already uses exactly these bounds — reused rather than inventing different numbers.

**`logoUrl` validation:** absolute `http`/`https` URL only (rejects `javascript:`, relative paths,
other schemes) — same "declarative data only, never something that could resolve to a dangerous
scheme" posture the rest of this domain enforces (`MobileConfigValidator`'s whitelist-only fields).
Length-bounded at 2048, matching `mobile/features/mobile-config/validation.ts`'s
`tenant.logoUrl.maxLength`.

**Repository change (`GetByTenantIdAsync` now includes `Theme`):** rather than adding a parallel
`GetThemeByTenantIdAsync` method, extended the existing tracked `GetByTenantIdAsync` — it's a
one-to-one join, negligible cost, and keeps `MobileThemeService`'s get-or-create-config logic
identical in shape to `MobileConfigDraftService`'s (both need the same root row, tracked, ready to
mutate in place).

**No circular-dependency risk on first-ever theme write (unlike TASK-534b's draft-creation bug):**
`MobileTheme.MobileConfigurationId` points one-directionally at `MobileConfiguration.Id`;
`MobileConfiguration` holds no reverse FK/pointer column at `MobileTheme` (only a nullable navigation
property, unlike `DraftVersionId`/`PublishedVersionId`, which DO create a real mutual-FK cycle with
`MobileConfigurationVersion`). Both `Guid`s are client-generated (`Guid.NewGuid()` in each entity's
factory), so EF Core can insert a brand-new `MobileConfiguration` and a brand-new `MobileTheme` in a
single `SaveChangesAsync()` call with no ordering problem. Proved this against real Postgres (not
just reasoned about it) with `MobileThemeServiceRlsIntegrationTests` — this is the FIRST code path
that has ever written a row into `mobile_themes` (previously read-only, `MobileConfigPublishedReadService`
only ever composed an in-memory, never-persisted default), and TASK-534/534b already found two real
EF/RLS bugs in this exact domain that mocked-repository tests alone missed, so trusting mocks alone
here wasn't warranted. Both live-Postgres tests passed on the first run — no bug found this time.

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors, 0 warnings.
- `dotnet test ShelfGuard.sln` — 1506/1506 passed (1464 pre-existing + 42 new: 30 validator +
  9 service unit tests + 1 schema-agreement test + 2 live-Postgres integration tests). Postgres was
  reachable in this environment (docker-compose, port 5435), so the 2 new RLS integration tests
  actually executed against real dev Postgres, not soft-skipped.
- `git status` reviewed: this task's own changes are the new `MobileThemeWhitelists.cs`,
  `Dtos/MobileThemeDtos.cs`, `IMobileThemeValidator.cs`, `MobileThemeValidator.cs`,
  `IMobileThemeService.cs`, `MobileThemeService.cs` files under `Features/MobileConfig/`, the new
  `MobileThemeController.cs`, the `Theme` include + `AddThemeAsync`/`UpdateTheme` additions to
  `IMobileConfigurationRepository.cs`/`MobileConfigurationRepository.cs`, the DI registration lines,
  the three new test files, and the `contracts/mobile-config.schema.json` +
  `MobileConfigSchemaContractTests.cs` spacing-enum changes. The repo carries a large amount of
  pre-existing uncommitted state from the same day's earlier Stage 6 tasks (TASK-527/528/531-535)
  plus older mobile-workstream files — none of it was touched by this task. Did not update
  `.claude/tasks/mobile-roadmap.md` (orchestrator's responsibility per the brief).

## Files

- `backend/ShelfGuard.Application/Features/MobileConfig/MobileThemeWhitelists.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/Dtos/MobileThemeDtos.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/IMobileThemeValidator.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/MobileThemeValidator.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/IMobileThemeService.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/MobileThemeService.cs` (new)
- `backend/ShelfGuard.Api/Controllers/MobileThemeController.cs` (new)
- `backend/ShelfGuard.Application/DependencyInjection.cs` (+ registration)
- `backend/ShelfGuard.Domain/Interfaces/IMobileConfigurationRepository.cs` (+ `Theme` include note,
  `AddThemeAsync`/`UpdateTheme`)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/MobileConfigurationRepository.cs`
  (+ `.Include(Theme)`, `AddThemeAsync`/`UpdateTheme` implementations)
- `backend/ShelfGuard.Tests/MobileConfig/MobileThemeValidatorTests.cs` (new)
- `backend/ShelfGuard.Tests/MobileConfig/MobileThemeServiceTests.cs` (new)
- `backend/ShelfGuard.Tests/Infrastructure/MobileThemeServiceRlsIntegrationTests.cs` (new)
- `backend/ShelfGuard.Tests/MobileConfig/MobileConfigSchemaContractTests.cs` (+ spacing-enum test)
- `contracts/mobile-config.schema.json` (`theme.spacing` free string → enum; description updated)

## Next

TASK-537 (`frontend-developer` — Theme Editor UI) can build against `GET`/`PUT api/v1/mobile/theme`.
TASK-544 (generalized publish flow) MUST read this task's "Immediate-live-effect gap" decision before
implementation — it needs to make one deliberate choice about how `MobileTheme` reconciles with
Draft/Preview/Publish, not discover the gap by accident.
