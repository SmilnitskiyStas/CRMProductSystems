# TASK-542 — Navigation Builder (frontend half: UI)

**Agent:** frontend-developer
**Date:** 2026-08-18
**Status:** done (verification pending live authenticated device/backend acceptance — same gap
TASK-539/540/541 already flagged; no backend/database was available in this session)

## Context loaded

`.claude/agents/frontend-developer.md`, `CLAUDE.md`, `.claude/tasks/mobile-roadmap.md` TASK-542
entry (Stage 6, Stage C), `backend/ShelfGuard.Application/Features/MobileConfig/
MobileConfigWhitelists.cs` and `MobileConfigValidator.cs` (to get the exact current whitelist
constants and validation semantics, not guessed), `useMobileConfigDraft.ts` (TASK-538b),
`AppBuilderCanvas.tsx` (TASK-539/541) and `ThemeEditorSection.tsx` (TASK-537) for the established
read-modify-write / explicit-save / structured-error-display conventions, `types.ts`, and
`mobile/features/mobile-config/{validation.ts,mock.ts}` to confirm no icon-key→lucide mapping
already existed anywhere in the codebase.

## Backend/frontend whitelist discrepancy found (documented, not blocking)

The brief stated backend enforces "label 1–30 chars." Reading `MobileConfigValidator.cs` directly
showed this is not accurate today: `ValidateNavigation`'s `RequireString(item, "label", ...)` only
checks `label` is present and is a string — no length cap, even empty strings pass server-side
(MASTER SPEC §8: "label stays a free string"). `MobileConfigWhitelists.cs` has no navigation-label
length constant (the only `30` in that area is `BlockRegistry.cs`'s unrelated `ctaLabel` block
prop). Proceeded with the 1–30 char client-side guard as specified anyway, since it's strictly
*more* conservative than the server (never rejects anything the server would accept) and is
good-practice UX (no truly-empty nav labels). Documented this clearly in `types.ts` on the new
`MOBILE_CONFIG_NAVIGATION_LABEL_MAX_LENGTH` constant so it's not mistaken for a real backend mirror
like every other `MOBILE_CONFIG_*`/`THEME_*` constant in that file is.

## What was built

- **`frontend/features/consumer-app/components/NavigationBuilderSection.tsx`** (new) — the
  Navigation Builder. Loads/saves via the existing `useMobileConfigDraft`/`useSaveMobileConfigDraft`
  hooks only (TASK-538b) — no second loading/saving mechanism. Keeps the full
  `MobileConfigDocument` minus `navigation` in local state (`restOfDoc`), lets `react-hook-form` +
  `useFieldArray` own the `navigation` array, and merges them back into one document on submit —
  the same read-modify-write shape as `AppBuilderCanvas.tsx`'s `withPageBlocks`, narrowed to a
  single array field. A brand-new tenant (`hasDraft: false`) gets a local `buildSeedDocument()`
  deliberately kept byte-identical to `AppBuilderCanvas.tsx`'s own seed (all 8 feature flags false,
  all 4 pages scaffolded with empty blocks, same starting `home`/`profile` nav items) — duplicated
  rather than importing from `AppBuilderCanvas.tsx`, per the brief's file-scope constraint; a code
  comment cross-references the original so the two stay in sync if it ever changes.
  - **Reordering:** `@dnd-kit/sortable` (already a project dependency, already used by
    `AppBuilderCanvas.tsx` for block drag-reorder) via `useFieldArray`'s `move()`. Chose this over
    up/down buttons for consistency with the established Retailer Admin builder interaction model,
    even though the list is short — no concrete downside found for a 2–5 item list, and it avoids a
    second reordering pattern in the same feature area.
  - **Add/remove:** "Add item" disabled at 5 items with an inline hint; each row's remove button is
    `disabled` (not just rejected on save) once only 2 items remain, with a `title` tooltip and
    matching hint text — satisfies the DoD's "block removal below the minimum with clear UI
    feedback, not just a rejected save."
  - **Icon preview:** `NAVIGATION_ICON_COMPONENTS`, a small local `Record<icon-key, LucideIcon>` map
    (`home→Home`, `tag→Tag`, `grid→Grid3x3`, `qr→QrCode`, `ticket→Ticket`, `map→Map`,
    `news→Newspaper`, `user→User`) — verified no such mapping existed anywhere in the frontend or
    `mobile/` before this task (the mobile client resolves these same semantic keys through its own
    native icon registry, not lucide). Kept local to this file rather than a new shared module,
    matching `AppBuilderCanvas.tsx`'s own `BLOCK_ICONS` precedent (also file-local) — nothing else
    in the frontend needs it today. Each row shows a live icon swatch (via `useWatch`) next to its
    icon `<select>`.
  - **Validation:** a `zod` schema mirroring the backend whitelist exactly — `navigation` array
    `.min(2).max(5)`, `type: z.enum(MOBILE_CONFIG_NAVIGATION_TYPES)`,
    `icon: z.enum(MOBILE_CONFIG_NAVIGATION_ICONS)`, `label` 1–30 chars (see discrepancy note above).
    Backend 400 errors (`navigation[N].field` format) are regex-mapped to react-hook-form's
    `navigation.N.field` `setError` paths for per-row/per-field display; anything that doesn't match
    (the array-level count error, or an unrelated field) falls back to the same generic banner
    convention `ThemeEditorSection.tsx`'s `mappedAny` fallback established.
- **`frontend/features/consumer-app/types.ts`** (extended) — added
  `MOBILE_CONFIG_NAVIGATION_ICONS`/`MobileConfigNavigationIcon` (mirrors
  `MobileConfigWhitelists.NavigationIcons`) and `MOBILE_CONFIG_NAVIGATION_LABEL_MAX_LENGTH`
  (client-only guard, explicitly documented as not a backend mirror — see discrepancy note above).
  No existing type/constant changed.
- **`frontend/app/(dashboard)/consumer-app/navigation/page.tsx`** — replaced TASK-535's
  `PlaceholderSection` body with `<NavigationBuilderSection />`; widened the page's `maxWidth` from
  720 to 800 so the component's 3-column row grid (type/label/icon) plus drag handle and remove
  button isn't cramped by the placeholder's narrower single-column width. Role gate/page-shell
  structure otherwise untouched (still mirrors every sibling `/consumer-app/*` route).
- **`frontend/messages/{en,uk}.json`** — new `Dashboard.consumerApp.navigationBuilder` key group
  (loading/error/labels/placeholders/8 `navTypes`/8 `navIcons`/count and field validation
  messages/save states), added to both locale files to keep them in lockstep, matching every other
  section in this file (`appBuilder`, `themeEditor`, etc., which are already fully bilingual).

## Verification

- `npx tsc --noEmit` (full project) — **0 errors**. One real type error hit and fixed along the way:
  `MobileConfigNavigationItem.icon` is a loose `string` (matches the raw JSON shape), while the
  zod-inferred form type narrows it to the icon enum — added a local `NavFormItem` type alias
  (`FormValues["navigation"][number]`) for `newItem()`'s return type, and a documented cast on the
  `reset()` call when hydrating from a previously-saved/seed document.
- `npx next lint` (full project) — **0 warnings, 0 errors**.
- `node -e "JSON.parse(...)"` on both `messages/en.json` and `messages/uk.json` — valid JSON.
- Live compile smoke check: started the `frontend-dev` preview server, navigated to
  `/consumer-app/navigation`. `app/(dashboard)/consumer-app/navigation/page.js` compiled and served
  `200 OK` with no console/server error tied to the new code (only the pre-existing, unrelated
  `next-intl` `ENVIRONMENT_FALLBACK` timeZone warning present on every page in this app already) —
  confirms `react-hook-form`/`@dnd-kit/sortable`/`zod`/`lucide-react` all resolve correctly at
  runtime. No backend was running in this session (`ERR_CONNECTION_REFUSED` on `:5000`), so the
  route's existing auth guard correctly redirected to `/login` before the component could fetch a
  real draft — authenticated round-trip (add/remove/reorder/edit/save/reload against a live backend
  + seeded enterprise-admin tenant) was **not run**, same gap TASK-539/540/541 already flagged and
  left for a combined live-acceptance pass.

## Scope discipline

`git status` after this task shows exactly the files the brief predicted: `types.ts` (extended,
`M`), `en.json`/`uk.json` (extended, `M`), plus `navigation/page.tsx` and
`NavigationBuilderSection.tsx` (both new — appear as `??` alongside the rest of Stage C's still-
uncommitted files from TASK-535–541, none of which this task touched). No shared component/hook
file (`AppBuilderCanvas.tsx`, `ThemeEditorSection.tsx`, `useMobileConfigDraft.ts`, etc.) was
modified — confirmed via `git status`, not assumed.
