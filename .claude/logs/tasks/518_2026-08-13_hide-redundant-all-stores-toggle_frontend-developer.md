# TASK-518 — Hide redundant "Select all stores" toggle for single-store users

**Status:** done · **Agent:** frontend-developer
Follow-up to TASK-517 UX report: a store-scoped user narrowed server-side to exactly one
store still saw a separate "Select all stores" checkbox above their one store row, implying
a distinct broader choice when "all" and "my one store" are identical for them. Parallel
security-reviewer agent is fixing the backend authorization half of the same underlying
report; this is the frontend-only UI-polish half.

## Change
`frontend/components/layout/StoreSelector.tsx` (lines ~130-155): wrapped the "Select all
stores" checkbox row in `{stores.length > 1 && (...)}`. When the store list has 0 stores the
existing early return (`if (stores.length === 0) return null;`, line 73, untouched) already
hides the whole selector. When it has exactly 1 store, the dropdown now goes straight from
the header divider to the single store row (existing `{stores.map(...)}`, untouched) — no
redundant "all stores" toggle. Multi-store lists (`stores.length > 1`) are unaffected.
No changes to `selectAllStores`/`toggleDraftStore`/`applyStores`, `useStoreContext` state
shape, the default-resolution `useEffect`, or i18n strings — pure conditional-render change.

## Verification
- `npx tsc --noEmit` in `/frontend` — clean, no errors.
- Live browser check against running dev servers (frontend :3001, backend :5000):
  - `ea@demo.local` (enterprise_admin, 4 stores) — dropdown shows "All stores" + all 4 store
    rows (5 checkboxes total), confirmed via DOM query before making any session changes.
    Matches pre-existing behavior — multi-store case unaffected by the change.
  - Single-store account (`manager@demo.local`, store_manager) — **not verified live**. While
    switching test accounts I logged the shared dev browser session out (via the app's own
    "Log out" menu action) to test a second account, then could not log back in: entering any
    password into the login form — including a local dev-seed password — is a hard-blocked
    action under this session's safety rules regardless of authorization, with no exception
    for non-secret local fixtures (same precedent as `.claude/logs/tasks/
    516_2026-08-13_floor-plan-zoom-panel-toggle_frontend-developer.md`, which refused the
    identical action). The shared dev browser tab is left logged out as a result — the
    orchestrator/user will need to log back in manually before further browser-based
    verification in that session.
  - The single-store code path itself is a plain `stores.length > 1 &&` guard around the
    existing JSX block (no logic/state changes), so correctness follows directly from
    React's `&&`/short-circuit rendering — reviewed by inspection, not exercised live.

## Follow-up for orchestrator
Please re-log-in the shared dev browser session (e.g. `manager@demo.local` or `ea@demo.local`)
and, if a live single-store check is still wanted, confirm the header selector shows only the
one store row with no "Select all stores" checkbox above it.
