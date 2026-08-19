# TASK-565b — Fix: BlockPropertyEditor infinite update-depth loop

**Status:** done · **Agent:** frontend-developer
**Fixes:** TASK-566's blocking bug (regression from TASK-565)

## Root cause
`frontend/features/consumer-app/components/BlockPropertyEditor.tsx` called
`const values = watch();` during render and keyed a `useEffect` off `values`:
```ts
useEffect(() => { onLiveChange?.(values); }, [values, onLiveChange]);
```
react-hook-form's argumentless `watch()` returns a new object reference every render (not
memoized). The effect fired every render → called parent's `setLiveProps` → parent/child
re-render → new `values` reference → effect fires again. Self-sustaining "Maximum update depth
exceeded" loop for as long as the drawer stayed mounted.

## Fix
Replaced the render-time-`values`-dependent effect with `watch`'s subscription/callback form,
which has a stable function identity and only invokes the callback on actual field changes:
```ts
useEffect(() => {
  const subscription = watch((formValues) => {
    onLiveChange?.(formValues as Record<string, unknown>);
  });
  return () => subscription.unsubscribe();
}, [watch, onLiveChange]);
```
Kept `const values = watch();` as-is — it's still needed at render time to drive the form's own
controlled inputs (`StringArrayField`'s `value={values[def.name]}`). That call was never the bug;
only the effect keyed off its result was.

File touched: `frontend/features/consumer-app/components/BlockPropertyEditor.tsx` (single
localized change, ~10 lines). No change to `onLiveChange`'s signature or to `AppBuilderCanvas.tsx`.

## Verification
- `npx tsc --noEmit` — clean.
- Ran frontend (port 3001, matching backend CORS allowlist — 3002 is CORS-blocked, only
  `http://localhost:3000,3001` are allowed in `appsettings.Development.json`) + backend (port
  5000) locally, browser session reused the same cached `ea@demo.local`-equivalent session QA used
  (tenant "Свіжий Кут Оболонь", role Network admin).
- Opened Hero Banner's Property Editor drawer, left it idle 8s: **zero** console errors (previously
  37 "Maximum update depth exceeded" in ~6s). Confirmed via `read_console_messages` before/after.
- Typing in Title field (dispatched as a real `input` event via the native value setter) updated
  the live preview column before Apply — unaffected by the fix.
- Apply persisted the value; `Save draft` PUT to `/api/v1/mobile/config/draft` returned 200 with
  the new title in the response body (`"title":"Live Loop Fix Check"`, `heightPx:225` also
  preserved) — confirmed via network inspection, not just UI state.
- Re-opened the drawer, typed an unapplied edit into Title (live preview reflected it), clicked
  Cancel: drawer closed, preview reverted to the last-applied value, unapplied text discarded.
- Resize-drag sanity check on the Hero Banner height handle (real `PointerEvent`
  pointerdown/pointermove×2/pointerup, `pointerId` set, small waits between events so React flushes
  each discrete event before the next dispatch): live value updated per pointermove (225→235→245px),
  `Save draft` button stayed **disabled** throughout the drag and flipped to enabled only on
  pointerup — confirms commit-once-per-drag is intact, unaffected by this fix (separate code path,
  `useResizeDrag.ts`/`blockPreviews.tsx`, untouched).

Note on environment: this Browser pane's `computer` click/type actions did not reach React's event
handlers reliably (same root cause QA hit — pane never fully composited for trusted-event
delivery); verification instead used direct React-prop invocation and native
`dispatchEvent`/`PointerEvent` calls, same technique QA's own log describes using.

## Next step
TASK-566 flipped back to `planned` in `.claude/tasks/current.md` (not `done`) — qa-tester should
do a **targeted re-check of only this bug** (open a drawer, watch console ~10s idle) rather than
re-running the full regression pass, since everything else in TASK-566's scope already passed.
