# TASK-538 — Block Registry

**Status:** done
**Agent:** backend-developer
**Date:** 2026-08-17

## What was done

- `BlockRegistry/` (`backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/`), a new
  subfolder of the existing MobileConfig feature:
  - `BlockPropTypes.cs` — plain string constants (`String`/`Int`/`Bool`/`Enum`/`Url`/`StringArray`)
    for a prop's declared type. Same "kind travels as a string, not a C# enum" convention
    `MobileConfigurationVersionStatus` already uses — avoids System.Text.Json's default
    enum-as-number serialization, which would be useless to a future property-editor UI with no
    extra `JsonStringEnumConverter` setup this repo doesn't otherwise carry.
  - `BlockCategories.cs` — string constants for the App Builder's block-palette groupings
    (`banner`/`loyalty`/`promotions`/`products`/`news`/`stores`/`layout`).
  - `BlockPropDefinition.cs` — one entry in a block's `validationSchema`: `name`/`type`/`required`/
    `default`/bounds (`minLength`/`maxLength`/`min`/`max`/`minItems`/`maxItems`)/`allowedValues`. A
    flat per-field descriptor, not a full JSON-Schema document.
  - `BlockDefinition.cs` — `type`/`displayName`/`icon`/`category`/`props`/`supportedDataSource`.
    `DefaultProps` is a computed property *derived* from each prop's `Default`, not a second,
    independently-authored dictionary, so the two can never drift apart.
  - `BlockRegistry.cs` — the static catalog: all 12 Core Blocks V1 types (CODEX SPEC ЕТАП 6) —
    `heroBanner`, `bannerCarousel`, `loyaltyCard`, `loyaltyBalance`, `promotionCarousel`,
    `promotionGrid`, `productCarousel`, `productGrid`, `sectionHeader`, `quickActions`, `newsList`,
    `storeList`. Each has a real `displayName`/icon/category, 1-5 typed+bounded props, and an
    evidence-based `supportedDataSource` (see Decisions).
  - `IBlockRegistryProvider`/`BlockRegistryProvider.cs` — DI-registered **singleton** wrapping the
    static list, with an O(1) `TryGet(type)` lookup dictionary built once.
- `Dtos/BlockRegistryDtos.cs` — `BlockDefinitionDto`/`BlockPropDefinitionDto`, each with a `From(...)`
  mapper that walks every field generically (loops over `Props`) — no per-block-type branching.
- `MobileBlocksController.cs` (`backend/ShelfGuard.Api/Controllers/`) — new controller:
  - `GET /api/v1/mobile/blocks` — every definition.
  - `GET /api/v1/mobile/blocks/{type}` — one definition, 404 if unknown.
  - `[Authorize(Policy = AppPolicies.AtLeastEnterpriseAdmin)]`, versioned under `/api/v1/` (decision
    2, TASK-556) — same admin-surface posture as `MobileThemeController`, deliberately separate from
    the anonymous consumer-facing `MobileConfigController`. Not tenant-scoped (no `ITenantContext`
    dependency) — the catalog is identical for every tenant.
- DI registration (`services.AddSingleton<IBlockRegistryProvider, BlockRegistryProvider>()`) in
  `ShelfGuard.Application/DependencyInjection.cs`.
- `.claude/docs/domain-model.md` — new "Block Registry (TASK-538)" subsection under the existing
  MobileConfig domain block, documenting the shape, the DI/route decisions, and the props-validation
  scope decision below.
- Tests:
  - `BlockRegistryTests.cs` (backend/ShelfGuard.Tests/MobileConfig/) — 12-types-defined,
    **agreement test against `MobileConfigWhitelists.BlockTypes`** (the load-bearing one — same
    pattern `MobileConfigSchemaContractTests` uses for TASK-533's contract), no-duplicate-types,
    per-definition non-empty display metadata (theory over all 12), every prop has a known type
    (theory), every enum/stringArray prop declares non-empty `AllowedValues` (theory), `DefaultProps`
    is correctly derived from `Props` for every definition, and `quickActions.actions` reuses
    `MobileConfigWhitelists.NavigationTypes` verbatim.
  - `BlockRegistryProviderTests.cs` — `GetAll`/`TryGet` (known/unknown/null/empty type), and
    `BlockDefinitionDto.From` field-by-field mapping correctness (one targeted case plus a
    no-throw pass over all 12 definitions).

## Decisions

**Static in-code registry, no DB table/migration.** Block *types* are compile-time-known metadata —
no retailer ever creates a new block type, only arranges instances of the fixed types on their pages
(TASK-539/541's job). A `BlockRegistry` static class + `IBlockRegistryProvider` singleton, per the
brief's recommended design; no schema work needed or done.

**Route: `GET /api/v1/mobile/blocks`, authenticated, `AtLeastEnterpriseAdmin`, not tenant-scoped.**
Chose the admin-surface posture (matching `MobileThemeController`) over the anonymous
`MobileConfigController` posture, because this catalog exists to serve TASK-539/540's Retailer Admin
UI, not the consumer mobile client (which has its own baked-in Component Registry and never calls
this endpoint). Since the catalog is identical for every tenant, no `ITenantContext` dependency was
added — simpler than `MobileThemeController`'s per-tenant GET/PUT shape.

**`validationSchema` fidelity: flat typed field list, not full JSON Schema.** Per the brief's
explicit permission — `{propName, type, required, allowedValues?, bounds}` is enough for TASK-540 to
generate the right input control per field (text/number/switch/select) without a general-purpose
JSON-Schema engine. `DefaultProps` is *computed* from each prop's `Default`, not duplicated, to rule
out the two ever silently disagreeing.

**`supportedDataSource` fidelity: real endpoint references where they exist, honest gaps where they
don't.** Checked what's actually shipped before writing each one: `bannerCarousel`/`promotionCarousel`
/`promotionGrid`/`productCarousel`/`productGrid` point at `ConsumerContentController`'s real
`GET /api/consumer/{tenantId}/{banners|promotions|catalog}` endpoints; `loyaltyCard`/`loyaltyBalance`
point at `ConsumerLoyaltyController`'s real `code`/`history` endpoints. `heroBanner`/`sectionHeader`/
`quickActions` are static, admin-authored content with no backend read (verified there's no "featured
banner" concept in `Banner`/`ConsumerContentController` — a rotating carousel exists, a single
pinned/featured slot doesn't). `newsList` and `storeList` **honestly flag real, current gaps** rather
than inventing an endpoint: grepped the Domain layer and found no `News` entity at all (only a
reserved, unimplemented `"news"` key in `MobileConfigWhitelists.FeatureKeys`/`NavigationTypes`); for
stores, `ConsumerLoyaltyController` has a preferred-store *selection* endpoint (`PUT
preferred-store`, takes a `storeId` the client already knows) but no GET-list-of-stores endpoint a
`storeList` block could actually bind to. Both are registered anyway (Core Blocks V1 requires all 12
types to exist), with the gap stated in `supportedDataSource` and in code comments, not silently
glossed over.

**`quickActions.actions` reuses `MobileConfigWhitelists.NavigationTypes` verbatim as its
`allowedValues`**, instead of inventing a second, parallel vocabulary of shortcut-target names — a
quick action is structurally just a shortcut to one of the app's already-whitelisted navigation
destinations, so reusing that existing whitelist is both less code and closes off a place a second,
independently-drifting list of "valid app destinations" could have appeared.

**Props-validation wiring into `MobileConfigValidator`: NOT done, flagged as follow-up (per the
brief's explicit "your call, document either way" permission).** Read the task brief's framing
carefully: wiring is warranted only if it's a *small, natural extension* on top of what's already
built. Concretely investigated whether it was, and found it is not, for two independent reasons:

1. TASK-532's already-shipped, already-passing `MobileConfigValidatorTests.cs` (23 tests) encodes an
   explicit, tested contract that a block's `props` is free-form JSON at this stage.
   `Validate_accepts_a_well_formed_document` uses `"props": {}` on a `heroBanner` block and asserts
   `Assert.Empty(result.Errors)` — but this task's own (first-ever, independently-authored)
   `heroBanner` schema marks `imageUrl` `required: true`. `Validate_accepts_multiple_whitelisted_
   block_types_on_multiple_pages` uses an arbitrary `"showQr": true` prop on a `loyaltyBalance`
   block and asserts the whole document is valid — this registry's own `loyaltyBalance` schema has
   no `showQr` field at all (it has `showPointsLabel`/`ctaLabel`). Wiring in strict
   presence/unknown-key enforcement now would break both already-shipped tests, and "fixing" them
   would mean rewriting TASK-532's tests around prop shapes *I* invented for *this* task, with no
   real UI producer to confirm them against — not a correction of a bug, an unauthoritative
   overwrite of already-agreed test behavior.
2. TASK-539 (App Builder canvas) and TASK-540 (Property Editor) — the actual UIs that will ever
   *produce* a block's `props` payload — don't exist yet. Locking save-time enforcement to this
   registry's invented shapes before those exist risks the registry diverging from what they
   actually need once built, forcing a second breaking change later. TASK-536 hit and avoided this
   exact risk class by checking the real mobile client's enum instead of guessing past a mismatch;
   the same caution applies here, except there is no "real client" to check yet — the producer is
   still two tasks away.

This is precisely the "much larger validation engine vs. keep scoped" fork the brief anticipated: the
per-field checks themselves are simple (that part of the brief's reasoning is correct), but safely
retrofitting them onto an already-shipped, already-tested, intentionally-free-form field — without
breaking existing coverage or guessing at a not-yet-built UI's real payload shape — is not small.
Documented in three places so a future reader lands on it: `BlockRegistry.cs`'s class remarks,
`BlockPropDefinition.cs`'s `Required` remarks, and `.claude/docs/domain-model.md`'s new Block
Registry section. **Recommended next step, not done here:** revisit this once TASK-539/540 ship and
real draft payloads exist to validate the registry's invented shapes against, rather than doing it
speculatively now.

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing, unrelated warning in
  `MarketplaceServiceTests.cs`, untouched by this task).
- `dotnet test ShelfGuard.sln` — 1554/1554 passed (1506 pre-existing + 48 new: `BlockRegistryTests`
  + `BlockRegistryProviderTests`, both pure in-memory unit tests, no Postgres dependency since this
  task added no persisted data).
- `git status` reviewed: this task's own changes are the new `BlockRegistry/` subfolder (7 files),
  `Dtos/BlockRegistryDtos.cs`, `MobileBlocksController.cs`, the DI registration line in
  `DependencyInjection.cs`, the two new test files, and the `domain-model.md` addition.
  `MobileConfigValidator.cs` was deliberately NOT touched (see Decisions). The repo carries a large
  amount of pre-existing uncommitted state from earlier Stage 6 tasks (TASK-526-537) and older
  mobile-workstream files — none of it was touched by this task. Did not update
  `.claude/tasks/mobile-roadmap.md` (orchestrator's responsibility per the brief).

## Files

- `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockPropTypes.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockCategories.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockPropDefinition.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockDefinition.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockRegistry.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/IBlockRegistryProvider.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/BlockRegistry/BlockRegistryProvider.cs` (new)
- `backend/ShelfGuard.Application/Features/MobileConfig/Dtos/BlockRegistryDtos.cs` (new)
- `backend/ShelfGuard.Api/Controllers/MobileBlocksController.cs` (new)
- `backend/ShelfGuard.Application/DependencyInjection.cs` (+ singleton registration)
- `backend/ShelfGuard.Tests/MobileConfig/BlockRegistryTests.cs` (new)
- `backend/ShelfGuard.Tests/MobileConfig/BlockRegistryProviderTests.cs` (new)
- `.claude/docs/domain-model.md` (+ "Block Registry (TASK-538)" section)

## Next

TASK-539 (`frontend-developer` — App Builder drag & drop canvas) can fetch `GET /api/v1/mobile/blocks`
for its block palette. TASK-540 (`frontend-developer` — Block Property Editor) can fetch
`GET /api/v1/mobile/blocks/{type}` and generate its form from `validationSchema`/`defaultProps`.
Whoever eventually revisits the props-validation gap (see Decisions) should do so only after TASK-539/
540 exist and real draft payloads can be checked against this registry's invented prop shapes —
same "verify against the real producer before locking behavior" caution TASK-536 already established
for this feature.
