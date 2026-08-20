# TASK-581 — Transfers: hide "Confirm receipt" for users outside the destination store

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-20

## Context

Parallel to TASK-580 (security-reviewer, backend fail-closed 403 on `PUT /api/transfers/{id}/confirm`
for users not assigned to the transfer's `toStoreId`). This task is the frontend UX side only —
hide the row action so users don't see a button that will now fail, and confirm no silent failure
if it does.

## Change

`frontend/app/(dashboard)/transfers/page.tsx`:
- Added `useLocations()` (`@/features/locations/hooks/useLocations`) and
  `myLocationIds = useMemo(() => new Set(locations.map(l => l.id)), [locations])`.
- Gated the "Confirm receipt" `ActionMenu` item on `tr.status === "in_transit" &&
  myLocationIds.has(tr.toStoreId)` (was status-only before).

Verified the assumption behind this by reading `LocationService.GetAllAsync`
(`backend/ShelfGuard.Application/Features/Locations/LocationService.cs`): scoped roles
(`network_manager`/`store_manager`/`merchandiser`/`storekeeper`/`cashier`/`staff`) get their
`user_locations` list filtered; `provider`/`enterprise_admin`/etc. get the full tenant list
unconditionally — so `myLocationIds.has(tr.toStoreId)` is correct for both scoped users (real
assignment check) and bypass roles (always true, matching the backend's unconditional bypass).
Known fail-open edge case (zero-assignment scoped user sees the full list) is pre-existing,
documented in that file, and out of scope here — not touched.

## Error handling (403 from a stale/race confirm attempt)

Checked the established pattern for one-click `ActionMenu` row actions elsewhere in the app
(`write-offs/page.tsx` approve/reject via `useApproveWriteOff`/`useRejectWriteOff`, `transfers`
cancel): **none of them wire `onError`** — `onClick: () => x.mutate(id)` with no error handling
is the existing, consistent convention for this action shape across the app, even though `sonner`
toast (`toast.error(err.message)`) *is* used elsewhere for form-submit mutations (events, orders,
sales pages). Adding a toast to just this one button would be inconsistent with its sibling
actions (cancel, approve, reject) that got no equivalent treatment. Left `confirm.mutate(tr.id)`
as-is, per the brief's own guidance not to invent a new pattern for a single button.

**Flagging as a pre-existing gap, not fixed here:** one-click `ActionMenu` row actions (confirm,
cancel, approve, reject) have no error surfacing anywhere in the app — a failed mutation (403,
network error, etc.) is currently silent everywhere this pattern is used, not just on this button.

## Verification

- `npx tsc --noEmit` — clean.
- `npm run lint` — clean, no new warnings.
- Live browser verification (backend TASK-580's 403 enforcement was already live in this same DB,
  confirmed via its task log):
  - Started `frontend-dev`/`backend-dev` on non-default ports (3010/5000) since the repo's default
    3000/3001 were both held by unrelated local processes; temporarily passed `Cors__Origins` as an
    env var to the `dotnet run` process (not a file edit under `backend/`) to allow the 3010 origin.
  - As `ea@demo.local` (enterprise_admin, bypass role): the in-transit transfer's row menu showed
    View / **Confirm receipt** / Cancel — button correctly shown (bypass roles always pass).
  - As `manager@demo.local` (store_manager, assigned to Центральний + Подільський only):
    temporarily changed the in-transit transfer's `ToLocationId` in the dev DB to Троєщина (a store
    they're not assigned to), confirmed the row menu now showed only View / Cancel — **Confirm
    receipt correctly hidden**. Reverted `ToLocationId` back to its original value immediately
    after.
  - Cleanup: DB value reverted (verified via SELECT), `.claude/launch.json` reverted to its
    committed 3001/5000 config (`git diff` on the file is empty), temporary dev servers stopped.

## Deviations

None from the brief. No backend files touched.
