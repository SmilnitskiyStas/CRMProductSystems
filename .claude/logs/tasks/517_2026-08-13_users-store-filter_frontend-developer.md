# TASK-517: Users list — filter by global header store selector (frontend)

**Status:** done · **Agent:** frontend-developer

Frontend half of TASK-517 (see `.claude/logs/tasks/517_2026-08-13_users-store-filter_backend-developer.md`
for the backend half — `GET /api/users` repeated `storeIds` param, done in parallel against
a fixed contract). Brief originally suggested TASK-508 for this log; renumbered to match the
backend log, which had already landed as TASK-517 (current.md's actual max at that point).

## What changed

1. `frontend/features/users/api/users.ts` — `usersApi.getAll(storeIds?: string[])` now
   appends repeated `?storeIds=` params (same `URLSearchParams.append` style as
   `priceSegmentsApi`'s `buildStoreQs`), omitted entirely when empty/undefined.
2. `frontend/features/users/hooks/useUsers.ts` — `useUsers()` reads
   `useStoreContext((s) => s.selectedStoreIds)` and includes it in the query key
   (`[...USERS_KEY, selectedStoreIds]`), passing it to `usersApi.getAll`.

Known limitation (per brief, not fixed): `useInviteUser`/`useUpdateUser`/`useDeactivateUser`'s
`qc.setQueryData(USERS_KEY, ...)` optimistic updates only patch the `[]` (all-stores) cache
entry now that the query key includes `selectedStoreIds`. Acceptable — switching stores
triggers a fresh fetch anyway.

No other files touched. Found 8 total `useUsers()` consumers (brief mentioned 2 — Users
page + UsersList): also `TenantRolesTab`, `TicketDetail`, `NotificationFilterDrawer`,
`WeekGrid`, `CreateWorkOrderModal`. All pick up the store filter transparently, consistent
with how the app already scopes similar pickers (e.g. `NotificationFilterDrawer` already
filters `useLocations()` the same way). Not in scope to change any of them.

## Verification

- `npx tsc --noEmit` in `/frontend` — clean, no errors.
- Live check via local dev servers (frontend :3001, backend :5000 — both started fresh for
  this check): logged in as `ea@demo.local` (enterprise_admin seed user), opened `/users`,
  confirmed `GET /api/users?storeIds=<id>` when a single store is selected (200 OK) and
  `GET /api/users` (no query string) when switched to "All stores" (200 OK) — against the
  already-merged backend implementation.

## Files touched

- `frontend/features/users/api/users.ts`
- `frontend/features/users/hooks/useUsers.ts`
