# TASK-539 — App Builder foundation (drag & drop canvas)

**Agent:** frontend-developer
**Date:** 2026-08-18
**Status:** done (verification pending live device/backend acceptance — see Verification)

## Context loaded

`CLAUDE.md`, `.claude/tasks/mobile-roadmap.md` Stage 6 (TASK-535/536/537/538/538b completed
entries, TASK-539 nominal entry), `docs/architecture/TARGET_ARCHITECTURE.md` §2 ЕТАП 7 (implied
by roadmap context), backend `MobileBlocksController.cs`, `MobileConfigDraftController.cs`,
`BlockRegistry.cs`/`BlockDefinition.cs`/`BlockPropDefinition.cs`/`BlockCategories.cs`/
`BlockPropTypes.cs`, `MobileConfigWhitelists.cs`, `MobileConfigValidator.cs`,
`MobileConfigDraftService.cs`, `MobileConfigDraftDtos.cs`/`BlockRegistryDtos.cs`, frontend
`FloorPlanCanvas.tsx` (dnd-kit precedent), `ThemeEditorSection.tsx` (form/save/error-handling
precedent), `features/consumer-app/types.ts`, `lib/api.ts`.

## What was built

- **`frontend/features/consumer-app/types.ts`** (extended) — `MobileConfigDocument` and its
  parts (`MobileConfigFeatures`, `MobileConfigNavigationItem`, `MobileConfigPage`,
  `MobileConfigBlockInstance`), client-side mirrors of `MobileConfigWhitelists.cs`
  (`MOBILE_CONFIG_BLOCK_TYPES`, `MOBILE_CONFIG_FEATURE_KEYS`, `MOBILE_CONFIG_NAVIGATION_TYPES`,
  `MOBILE_CONFIG_PAGE_NAMES`, nav min/max, current schema version — same "mirror the
  dependency-free C# constants class by hand" convention `THEME_*` already established), Block
  Registry DTOs (`BlockDefinitionDto`, `BlockPropDefinitionDto`), and Draft CRUD DTOs
  (`MobileConfigDraftResponse`, `SaveMobileConfigDraftRequest`,
  `MobileConfigDraftValidationError`).
- **`frontend/features/consumer-app/api/blockRegistry.ts`** — `fetchBlockRegistry()`,
  `GET /api/v1/mobile/blocks`.
- **`frontend/features/consumer-app/api/mobileConfigDraft.ts`** —
  `fetchMobileConfigDraft()`/`saveMobileConfigDraft()` (`GET`/`PUT /api/v1/mobile/config/draft`)
  and `extractDraftValidationErrors()`, mirroring `mobileTheme.ts`'s
  `extractThemeValidationErrors()` verbatim against the same `{ errors: [{field,message}] }`
  shape `MobileConfigDraftController` reuses from `MobileThemeController`.
- **`frontend/features/consumer-app/hooks/useBlockRegistry.ts`** — `useBlockRegistry()`
  (React Query, 5 min `staleTime` — static, non-tenant-scoped catalog).
- **`frontend/features/consumer-app/hooks/useMobileConfigDraft.ts`** —
  `useMobileConfigDraft()` / `useSaveMobileConfigDraft()`, same cache-seed-on-success convention
  as `useMobileTheme.ts`.
- **`frontend/features/consumer-app/components/AppBuilderCanvas.tsx`** (new) — the canvas
  itself. Palette (left, grouped by `BlockCategories`, from the live registry) + Home-page block
  list (right, `@dnd-kit/sortable`). Drag from palette onto canvas (or click "+") inserts a new
  block instance (`crypto.randomUUID()` id, `defaultProps` copied from the registry) at the drop
  position; dragging a placed block reorders via `arrayMove`; a trash-icon button removes one.
  `DragOverlay` shows a floating ghost card during any drag. Explicit **"Save draft"** button
  (disabled unless dirty) — no autosave, matching `ThemeEditorSection`'s established explicit-save
  convention for this feature area, confirmed against the rest of the codebase (no debounced-save
  precedent exists outside search-box filters). No publish/preview control anywhere on this
  screen.
- **`frontend/app/(dashboard)/consumer-app/pages/page.tsx`** — replaced TASK-535's
  `PlaceholderSection` placeholder with `<AppBuilderCanvas />`, widened to `maxWidth: 1100`
  matching `/consumer-app/design`'s two-column precedent.
- **`frontend/messages/{uk,en}.json`** — new `Dashboard.consumerApp.appBuilder` key group
  (palette/canvas copy, category labels, save states, the "this is a draft, not published"
  notice).
- **`frontend/package.json`/`package-lock.json`** — added `@dnd-kit/sortable@^8.0.0` as a new
  direct dependency. `@dnd-kit/core`/`@dnd-kit/modifiers` were already present (used by
  `FloorPlanCanvas.tsx`); `@dnd-kit/utilities` was already a transitive dependency of
  `@dnd-kit/core`. `@dnd-kit/sortable` is the official first-party companion package for exactly
  this "reorderable list + drop from an external source" pattern — adding it, rather than
  reimplementing sortable insertion-index math by hand on top of raw `@dnd-kit/core`, is "follow
  the established dnd-kit pattern" in spirit, not "a different drag library."

## Read-modify-write implementation (the correctness trap flagged in the brief)

`AppBuilderCanvas` keeps the **entire** parsed `MobileConfigDocument` in one piece of state
(`configDoc`, named to avoid shadowing the global `window.document`), not just the block array.
Every add/remove/reorder goes through `withHomeBlocks(doc, updater)`, which only ever replaces
`doc.pages.home.blocks` (reassigning sequential `order` values) and spreads everything else
(`schemaVersion`, `features`, `navigation`, any other `pages.*` entry) through unchanged. `Save`
sends `JSON.stringify(configDoc)` — the full document — to `PUT`, never a `pages.home`-only
fragment. Verified by inspecting `MobileConfigDraftService.SaveDraftAsync`: it calls
`_validator.Validate(configurationJson)` against the whole string and stores it verbatim, so a
partial document would either fail `MobileConfigValidator`'s required-`features`/required-
`navigation` checks or silently drop whatever the client omitted — this component structurally
cannot produce that fragment because it never constructs the PUT body from anything but the
full `configDoc`.

## Default-seeding decision for first-time tenants (documented per the brief's request — TASK-542 needs this)

When `GET /api/v1/mobile/config/draft` returns `hasDraft: false` (brand-new tenant), the canvas
does **not** treat the missing draft as an error or leave the document `null` — it seeds a
minimal, valid starting document in `buildSeedDocument()`:

```json
{
  "schemaVersion": 1,
  "features": { "loyalty": false, "promotions": false, "catalog": false, "coupons": false,
                "news": false, "receipts": false, "delivery": false, "personalOffers": false },
  "navigation": [
    { "type": "home", "label": "Головна", "icon": "home" },
    { "type": "profile", "label": "Профіль", "icon": "user" }
  ],
  "pages": { "home": { "blocks": [] } }
}
```

- **All 8 `features` keys present, defaulted `false`** — per the brief's explicit instruction,
  even though `MobileConfigValidator.ValidateFeatures` doesn't itself require every key present
  (it only rejects unknown keys / non-boolean values on whatever keys *are* given). Defaulting
  every key to `false` is honest (no feature silently implied "on") and gives Stage D's Feature
  Flags UI (not built yet) a complete object to toggle instead of a sparse one.
- **`navigation` has exactly the 2-item required minimum** (`home` + `profile`) — the exact
  label/icon values (`"Головна"`/`"home"`, `"Профіль"`/`"user"`) are **not invented**: they match
  `mobile/features/mobile-config/mock.ts`'s existing `home`/`profile` entries and the backend's
  own test fixtures (`MobileConfigValidatorTests.cs`,
  `MobileConfigPublishedReadServiceTests.cs`) verbatim, so a first-time tenant's seed document
  looks identical in shape/content to what every other layer of this system already treats as the
  canonical default nav, not a client-invented value that could drift.
- **`pages` only populates `home`** — this task's DoD is Home-page blocks only; `promotions`/
  `catalog`/`news` stay entirely absent from the document until TASK-541 (Page Builder) adds them,
  which is valid per `MobileConfigWhitelists.PageNames`/`MobileConfigValidator.ValidatePages`
  (iterates whatever keys are present, doesn't require all four).
- The seed is used **client-side only** until the user's first `Save` — nothing is written to the
  backend just by opening the page with an empty draft. **TASK-542 (Navigation Builder) should
  build on/edit this existing 2-item default**, not assume no navigation exists for a tenant that
  has only ever touched the App Builder canvas so far.

## Verification

- `npx tsc --noEmit` — **0 errors** (full project).
- `npm run lint` (`next lint`, full project) — **0 warnings, 0 errors**. (A file-scoped
  `next lint --file features/consumer-app ...` run separately showed two **pre-existing**
  `no-img-element` warnings in `BannerForm.tsx`/`ThemeEditorSection.tsx`, already noted in
  TASK-537's own log — not introduced here, and absent from the full clean run.)
- `npx vitest run` (full project) — **48/48 passed**, no regressions (same count as TASK-537's
  baseline; this task added no new automated tests of its own — see Known gaps below).
- Live compile smoke check: started the `frontend-dev` preview server, navigated to
  `/consumer-app/pages` unauthenticated. Next.js compiled the route cleanly (953 modules, `200`
  response, correctly redirects to `/login` under the existing `useMe()`-based client guard — same
  behavior as every sibling `/consumer-app/*` route). Confirms the new `@dnd-kit/sortable`
  dependency and every new module resolve and execute through Next's dev webpack with no runtime
  import/module error. The only console error was `net::ERR_CONNECTION_REFUSED` (backend not
  running — expected, unrelated) and a pre-existing, unrelated `next-intl` `ENVIRONMENT_FALLBACK`
  timeZone warning already present on every page in this app.
- **Not run:** authenticated end-to-end interaction (actual drag/reorder/remove/save against a
  live backend + seeded enterprise-admin tenant). No backend instance, database, or credentials
  were available in this session to exercise that path. This mirrors how TASK-435's device-QA
  gate defers live acceptance elsewhere in this roadmap — flagged here rather than silently
  assumed passing.

## Known gaps / follow-up (not silently left unaddressed)

- No automated tests were added for `AppBuilderCanvas.tsx` itself (unlike some other
  `frontend-developer` tasks in this codebase, this feature area — `ThemeEditorSection.tsx`,
  `BannerForm.tsx` — has no existing component-level test precedent either; `vitest`'s current 48
  tests are all `lib/*` unit tests). If component test coverage is wanted for the App Builder
  canvas, that is unscoped follow-up work, not a gap introduced relative to this directory's
  existing convention.
- Drag-from-palette insertion position is index-based on whatever `over.id` resolves to at drop
  time (a specific card, or the empty dropzone) — reasonably intuitive, but not pixel-precise
  "insert exactly above/below the hovered half" the way some polished builders do. Acceptable for
  a "foundation" canvas per the task's own framing; can be refined later without a data-shape
  change.
- `BLOCK_ICONS` is a small hand-maintained map from the registry's free-text `icon` string to a
  `lucide-react` component (12 Core Blocks V1 entries), with a generic `Blocks` fallback for any
  future/unrecognized value — documented inline in `AppBuilderCanvas.tsx` remarks. This is
  decorative-only; it does not affect validation or persistence.

## Scope discipline

No block-instance **props editing** UI was built (that's TASK-540, explicitly out of scope per
the brief) — a freshly-dropped block always gets the registry's `defaultProps` verbatim, with no
per-field controls. No Promotions/Catalog/News page editing (TASK-541) and no
Draft/Preview/Publish workflow (TASK-544) — confirmed no publish-shaped control exists anywhere
in `AppBuilderCanvas.tsx`.
