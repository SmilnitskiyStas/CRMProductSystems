# TASK-566 — QA: App Builder live preview regression pass

**Status:** done
**Agent:** qa-tester

## Update 2026-08-19 — targeted re-check after TASK-565b's fix

TASK-565b (`.claude/logs/tasks/565b_2026-08-19_fix-onlivechange-infinite-loop_frontend-developer.md`)
fixed the bug below by switching `BlockPropertyEditor.tsx`'s effect to `watch()`'s subscription/
callback form instead of depending on its unstable render-time return value. Re-ran the dev servers
fresh, logged in as `ea@demo.local`, and did a targeted (not full) re-check per the coordinator's
request:

1. **Console-quiet check.** Opened Hero Banner's Property Editor drawer, hooked `console.error`
   with a counter, left it idle 9s: **0** errors (was 37/~6s before the fix). Cross-checked with
   `read_console_messages` — only one unrelated `401` from an unrelated background request, no
   "Maximum update depth exceeded" anywhere.
2. **Three behaviors that route through the same code path, all intact:**
   - (a) Live-reflect before Apply: set the Title field via a native input-value-setter + `input`
     event (this Browser pane's synthetic click/type still doesn't reach React reliably — same
     limitation TASK-565b's own log notes) — preview updated to the new title instantly, before
     Apply.
   - (b) Apply persists: clicked Apply, then Save draft, then fetched
     `GET /api/v1/mobile/config/draft` directly — confirmed the new title
     (`"Apply Persist Recheck"`) was actually written server-side, not just reflected in the DOM.
   - (c) Close-without-apply reverts: typed an unapplied edit, clicked Cancel — preview reverted to
     the last-applied value, dirty state untouched.
3. **Resize commit-once-per-drag sanity check** (Hero Banner height handle, real `PointerEvent`
   pointerdown → pointermove → read-mid-state → pointerup): mid-drag the handle showed the live
   value (225→245px) while the Save button stayed disabled; only on `pointerup` did the value
   commit and the Save button flip to enabled — unchanged from the original pass.

All three targeted checks pass. Stopped the temporary dev servers afterward. TASK-566 flipped to
`done` in `.claude/tasks/current.md`. No further re-testing needed — everything else in this task's
original scope (see below) already passed and this fix touched only the one file.

---

Environment: started backend (`dotnet run`, port 5000) and frontend (`next dev -p 3001`, port
3000 was occupied by an unrelated project) locally against the existing dev Postgres/Redis
containers. Logged in as `ea@demo.local` (session already cached in the browser profile). Browser
pane screenshots/`left_click_drag` were unavailable in this environment (pane never composited) —
all interaction was via `read_page`/`get_page_text` for clicks and via `javascript_tool`
dispatching real `PointerEvent`s (pointerdown/pointermove/pointerup with `pointerId`) for drag
gestures, which exercises the same native listeners `useResizeDrag`/`@dnd-kit` attach. Draft/
publish state was cross-checked against the raw API (`GET/PUT /api/v1/mobile/config/draft`,
`GET /api/v1/mobile/config?tenantId=`) alongside the UI.

## Bug found

### Bug: Property Editor drawer causes a sustained React "Maximum update depth exceeded" loop
Severity: high
Task: TASK-565
File: `frontend/features/consumer-app/components/BlockPropertyEditor.tsx` (~line 482-486)

Steps:
1. `/consumer-app/pages`, click "edit" on any block to open the Property Editor drawer.
2. Watch the browser console.

Expected: no console errors while the drawer is open.
Actual: repeated `Warning: Maximum update depth exceeded...` from `BlockPropertyEditor`, recurring
for as long as the drawer stays mounted, stopping immediately (confirmed 0 further occurrences in
1.5s) the instant the drawer closes (Apply or Cancel). Instrumented via a `console.error` counter:
37 occurrences accumulated over ~6s with the drawer open, 0 afterward. Fully reproducible from a
fresh page load with a single click — not intermittent.

Root cause: `const values = watch();` (react-hook-form) returns a new object reference on every
render (no memoization). The new effect added by TASK-565:
```
useEffect(() => {
  onLiveChange?.(values);
}, [values, onLiveChange]);
```
fires on every render because `values` never has stable identity, calling `onLiveChange` →
`AppBuilderCanvas`'s `setLiveProps` → parent re-render → child re-render → new `values` reference →
effect fires again. Self-sustaining for as long as the drawer is mounted; each unrelated top-level
re-render (React Query background refetch, etc.) restarts a burst.

Note on the task log's framing: TASK-565's log/JSDoc says this "matches the existing `watch()`-based
live pattern `ThemeEditorSection.tsx` already uses for its own preview panel." Checked —
`ThemeEditorSection.tsx:206` does call `const values = watch();`, but it only reads `values`
directly during render (feeds its own local JSX), it never pipes `values` through a
dependency-array `useEffect` into a parent callback. The loop-inducing part is new to
`BlockPropertyEditor.tsx`, not inherited from the pattern it cites.

Impact: does not corrupt data — Apply/Save/Publish all persisted correct values end-to-end (see
below), and the described live-reflect/revert-on-cancel/persist-on-apply UX all function correctly
despite the loop. But it burns CPU continuously while any drawer is open and will flood the
console / any error-tracking integration (Sentry etc.) in production. One `javascript_tool`
automation call timed out immediately after opening a drawer ("Promise was collected"), consistent
with real main-thread contention, though not proof of a hard freeze.

Suggested fix direction (not applied — QA doesn't fix code): subscribe via react-hook-form's
`watch` callback form in a mount-only effect instead of depending on its unstable return value,
e.g. `useEffect(() => { const sub = watchFn((v) => onLiveChange?.(v)); return () =>
sub.unsubscribe(); }, [])`, rather than calling `watch()` in the render body and keying an effect
off its result.

## Everything else: clean pass

1. **Add/remove/reorder across Home/Promotions/Catalog/News** — preview updates in the same
   render for add, remove, and (verified via synthetic `@dnd-kit` pointer-drag) reorder. Switching
   page tabs shows the correct per-page block list every time with zero leakage from another page
   (checked all 4, back-and-forth); Home's edits survived being untouched while blocks were added
   to the other 3 pages.

2. **Property edits** — typing in the drawer reflects in the preview before Apply; closing via
   Cancel reverts the preview to the last-applied value and leaves `dirty` untouched; Apply commits
   to `configDoc`, Save draft persists it, and a full page reload confirmed the value survived
   (`Applied Title Persist Test` on Hero Banner). (Runs under the render-loop bug above, but the
   observable behavior itself is correct.)

3. **Resize drag, all 4 types** — bounds and defaults match ADR-031's table exactly, verified live
   and post-reload:
   | Block type | Prop | Default confirmed | Min clamp | Max clamp |
   |---|---|---|---|---|
   | heroBanner | heightPx | 190 | 120 ✓ | 260 ✓ |
   | bannerCarousel | cardWidthPx | 280 | 200 ✓ | 360 ✓ |
   | promotionCarousel | cardWidthPx | 210 | 150 ✓ | 270 ✓ |
   | productCarousel | cardWidthPx | 170 | 120 ✓ | 220 ✓ |

   Commit-once-per-drag verified directly (not just trusted from the task log): Save button stayed
   disabled through pointerdown+pointermove, flipped to enabled only on pointerup, for both
   heroBanner (clean-state test) and productCarousel. All 4 resized values survived Save + full
   page reload.

4. **Old saved config regression** — PUT a hand-edited draft via the API with `heightPx`/
   `cardWidthPx` deleted from all 4 block types' `props` (simulating a pre-TASK-561 config),
   reloaded `/consumer-app/pages`: web preview rendered exactly 190/280/210/170 on all 4, confirming
   the `num(props.x, default)` fallback in `blockPreviews.tsx` works for a genuinely *absent* key,
   not just a registry-supplied default on a freshly-added block. Mobile suite spot-checked by
   reading and then running it: `coreBlocks.test.tsx` has an explicit
   `falls back to today's exact default pixel values when heightPx/cardWidthPx are absent
   (regression guard)` test asserting 190/280/210/170, and `resolveBlocks.test.ts` has parametrized
   cases confirming `cardWidthPx` forwards when present and resolves to `undefined` (not dropped/
   crashed) when absent, for all 3 carousel types. Ran `npx jest server-driven-ui`: 3 suites / 31
   tests passed.

5. **Full draft → publish loop** — set distinguishing sizes via real resize drags (heroBanner=225,
   bannerCarousel=310, promotionCarousel=195, productCarousel=205), Save draft, confirmed via
   `GET .../config/draft` those exact values were stored, published from `/consumer-app/versions`
   (draft became version 4/CURRENT), then fetched the real consumer-facing
   `GET /api/v1/mobile/config?tenantId=...` and confirmed byte-for-byte the same 4 values came back
   — the "not a lie" claim holds end-to-end from web preview to published API response.

6. **loyaltyCard / loyaltyBalance sample-data labeling** — both render a clearly visible "ПРИКЛАД
   ДАНИХ" (sample data) badge in the preview; confirmed for both block types (loyaltyBalance
   wasn't in the frontend-developer's own spot-check, added here).

7. **Pre-existing flow regression (TASK-539/540/541/546)** — Save button disabled/enabled state
   tracked dirty correctly throughout every test above; the in-app navigation guard
   (`useUnsavedChangesGuard`) still fires — clicking a sidebar link while dirty triggered the
   native `confirm()` gate and blocked navigation (page stayed on `/consumer-app/pages`) when the
   dialog was dismissed; drag-and-drop reorder via `@dnd-kit` still works and the new preview
   column stays in sync with it. Nothing broken by the third column or `onLiveChange` wiring beyond
   the bug above.

## Next step
Route the bug above back through backend/frontend-developer's normal fix cycle (frontend-only,
`BlockPropertyEditor.tsx`), then re-verify just that file's console-quietness before flipping
TASK-566 to `done`. Everything else in this task's scope is a clean pass and doesn't need
re-testing after the fix.
