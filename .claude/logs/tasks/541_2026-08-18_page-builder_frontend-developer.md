# TASK-541 — Page Builder

**Agent:** frontend-developer
**Date:** 2026-08-18
**Status:** done (verification pending live device/backend acceptance — same gap TASK-539/540 flagged)

## Context loaded

`CLAUDE.md`, `.claude/tasks/mobile-roadmap.md` (TASK-541 nominal entry, TASK-538/538b/539/540
completed entries above it), `AppBuilderCanvas.tsx` (TASK-539), `BlockPropertyEditor.tsx`
(TASK-540), `types.ts`, `MobileConfigValidator.cs`/`MobileConfigWhitelists.cs` (backend — checked
directly to confirm no minimum-page-count or per-page-required rule exists, so the seed/normalize
changes below stay valid), the pre-existing `/consumer-app/pages/page.tsx` route shell.

Per the orchestrating session's brief, this was frontend-only: the roadmap's original
`backend-developer` co-assignment predates TASK-532/538, which already whitelist and validate all
four pages generically. No backend gap was found — see "Backend gap finding" below.

## What was built

Most of "Home page fully block-driven end to end" was already done by TASK-539/540. This task's
actual scope was generalizing `AppBuilderCanvas.tsx` from hardcoded-to-Home into page-aware, so
Promotions/Catalog/News reuse the identical mechanism.

- **`frontend/features/consumer-app/components/AppBuilderCanvas.tsx`** (extended):
  - `withHomeBlocks(doc, updater)` → `withPageBlocks(doc, page, updater)` — same read-modify-write
    shape, parameterized by which page is active. All four call sites (`addBlockAt`, `removeBlock`,
    `updateBlockProps`, drag-reorder in `handleDragEnd`) updated.
  - `normalizeDocument` now sorts/defaults `blocks` for **every** whitelisted page
    (`MOBILE_CONFIG_PAGE_NAMES`), not just `home` — a document saved before this task (Home-only)
    normalizes cleanly; the missing pages just get an empty scaffold.
  - `buildSeedDocument` now seeds all four pages with an empty `blocks: []` (previously only
    `home`) — a brand-new tenant's first save is valid regardless of which tab they touch first.
  - New `activePage` state (`useState<MobileConfigPageName>("home")`) and a `PageTabs` component
    — a local underline-tab strip (visually matching this feature area's existing
    `LifecycleTabs.tsx` convention, kept as a separate local component since `LifecycleTabs`'s
    type is hardcoded to its own `LifecycleTab` union and isn't shared infrastructure) rendered
    above the palette/canvas. Tabs are `MOBILE_CONFIG_PAGE_NAMES` in order — Home/Promotions/
    Catalog/News — which is already the exact Ukrainian-first order the task brief asked for.
  - `blocks` (feeding the canvas) now reads `configDoc.pages[activePage].blocks`. Switching tabs
    clears `selectedBlockId` (closes any open Property Editor drawer) — explicit for clarity, even
    though `selectedBlock` would already resolve to `null` on another page since block ids never
    collide across pages.
  - Canvas panel title (`canvasTitle`) now interpolates the active page's tab label
    (`t("canvasTitle", { page: t(\`pageTabs.${activePage}\`) })`) instead of a hardcoded "Головний
    екран"/"Home screen" — reads identically to the old text when Home is selected.
  - The whole `configDoc` (all four pages) stays in React state regardless of which tab is active,
    so switching pages never drops unsaved edits on another page — Save still `JSON.stringify`s
    the entire document.
- **`frontend/features/consumer-app/types.ts`** — two doc-comment updates only (no shape/type
  changes): the `MOBILE_CONFIG_PAGE_NAMES` comment no longer says "This task only edits `home`",
  and `MobileConfigDocument`'s comment now notes the seed populates every `pages` key.
- **`frontend/messages/{en,uk}.json`** — new `Dashboard.consumerApp.appBuilder.pageTabs` key
  group (`home`/`promotions`/`catalog`/`news` labels, matching existing `categories.promotions`/
  `categories.news` translations already in the same file for consistency), and `canvasTitle`
  changed from a static string to a `{page}` interpolation template in both locales.
- No backend files touched. `BlockPropertyEditor.tsx` **not touched** — confirmed page-agnostic
  by reading it: its props are `block`/`definition`/`onClose`/`onApply`, no page concept anywhere,
  so it needed zero changes to work identically on any of the four pages.

## Backend gap finding

**None.** Verified directly against `MobileConfigWhitelists.cs`/`MobileConfigValidator.cs`:
`PageNames` already whitelists exactly `home`/`promotions`/`catalog`/`news` (matching
`MOBILE_CONFIG_PAGE_NAMES` on the frontend 1:1), `ValidatePages` iterates whatever page keys are
present with no minimum-count or "must include X" rule, and each page's `blocks` array is
validated identically regardless of page name. Nothing about seeding all four pages with an empty
`blocks: []` array, or round-tripping a document that only has some of the four keys, requires any
backend change.

## Non-regression checks (per DoD)

- `git status` after this task: only `AppBuilderCanvas.tsx` (untracked — TASK-539's file, not yet
  committed from a prior session), `types.ts` (2 comment-only edits), `en.json`/`uk.json` (new
  `pageTabs` keys + `canvasTitle` value change, inside the pre-existing, still-uncommitted
  `appBuilder` block from TASK-539/540). No files under `/consumer-app/{promotions,catalog}`
  (the unrelated data-admin routes) or any Profile/Auth/Security screen appear anywhere in the
  diff — confirmed by `git status`/`git diff --stat`, not assumed.
- `MobileConfigWhitelists.PageNames` deliberately excludes Profile/Auth/Security (per its own doc
  comment: "System-controlled pages ... are deliberately absent: they can never appear in this
  document at all") — so there was never a code path in this component that could have touched
  them; the DoD's non-regression bullet is satisfied structurally, not just by omission.

## Verification

- `npx tsc --noEmit` (full project) — **0 errors**. (One real type error hit and fixed along the
  way: `Object.fromEntries(MOBILE_CONFIG_PAGE_NAMES.map(...))` inferred as `{ [k: string]: ... }`,
  which TS refused to cast to `Record<MobileConfigPageName, MobileConfigPage>` — switched to an
  explicit `for` loop building the record, matching `normalizeDocument`'s existing pattern.)
- `npx next lint` (full project) — **0 warnings, 0 errors**.
- `npx vitest run` (full project) — **48/48 passed**, same baseline as TASK-539/540 (no new
  component tests added — no existing component-test precedent in this feature area).
- `node -e "JSON.parse(...)"` on both `messages/en.json` and `messages/uk.json` — valid JSON.
- Live compile smoke check: started the `frontend-dev` preview server, navigated to
  `/consumer-app/pages`. Compiled cleanly (`GET /consumer-app/pages 200`, 989 modules, no error
  tied to the new code — the only console error is the pre-existing, unrelated `next-intl`
  `ENVIRONMENT_FALLBACK` timeZone warning present on every page in this app already). Client-side
  redirected through `/login` to the marketing landing page under the existing unauthenticated
  guard, same pattern TASK-539/540 saw.
- **Not run:** authenticated end-to-end (switch tabs, add/reorder/remove/edit blocks on
  Promotions/Catalog/News, Save, reload, confirm Home's existing content survived) against a live
  backend + seeded enterprise-admin tenant. No backend instance/database/credentials were
  available in this session — same gap TASK-539/540 already flagged and explicitly deferred to a
  combined live-acceptance pass once the App Builder surface (TASK-539/540/541) is complete, which
  it now is.

## Scope discipline

Exactly the files the brief predicted: `AppBuilderCanvas.tsx` (generalized), `types.ts`
(comment-only), `en.json`/`uk.json` (new `pageTabs` keys). `BlockPropertyEditor.tsx` needed no
change (verified, not assumed). No new files, no new hooks, no backend files.
