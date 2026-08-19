# TASK-427: Fix — mobile crash on notifications (`PagedResult` envelope not handled)

**Agent:** mobile-developer
**Date:** 2026-07-27
**Status:** done — implemented, `tsc --noEmit` clean. No emulator/Expo Go available in this
environment — on-device verification still needed by the user.

## Context

Since TASK-339 (2026-07-12), `GET /api/notifications/history` returns a paginated envelope
`{ items, totalCount, page, pageSize, totalPages }` instead of a bare array. Mobile's API client
still typed/returned it as `Notification[]`, so `.filter`/`.map`/`.length` calls on the envelope
object threw `TypeError: undefined is not a function` right after staff login, crashing the app.
Root cause was pre-investigated and handed off in the brief — no re-investigation done here.

## Fix (mobile only, 4 files)

- `mobile/features/notifications/types.ts` — added `isRead`, `readAt`, `title`, `storeId`,
  `userId` to `Notification`; added local `PagedResult<T>` interface (mobile convention: no
  shared `mobile/lib` type module, each feature keeps its own copy).
- `mobile/features/notifications/api/notificationApi.ts` — `getNotificationHistory()` now
  returns `PagedResult<Notification>` instead of `Notification[]`. No pagination params added;
  backend already defaults to page=1/pageSize=50, so behavior (first page only) is unchanged.
- `mobile/features/notifications/components/NotificationBell.tsx` — `notifications.filter(...)`
  → `notifications.items.filter(...)`.
- `mobile/app/(app)/notifications.tsx` — 4 call sites updated to go through `.items`
  (`unreadCount` filter, `handleMarkAll` map, `FlatList` `data`, empty-state length check).

Backend untouched (contract already correct). Auth/session/role-gate files untouched, per brief.

## Verification

- `npm run type-check` (`tsc --noEmit`) in `mobile/` — clean, no errors.
- Grepped both consumer files for leftover bare `notifications.filter(`/`.map(`/`.length` outside
  `.items` — none found. Confirmed no other file references `useNotificationHistory` /
  `getNotificationHistory` besides the 4 files touched plus the untouched hook/item files named
  in the brief.
- `npm run lint` fails repo-wide with "ESLint couldn't find an eslint.config.js" (ESLint 9 flat
  config migration not done) — pre-existing, unrelated to this change, not fixed here (out of
  scope for TASK-427).
- **Not verified:** actual on-device/Expo Go run. This environment cannot launch a mobile
  emulator or Expo Go. User should smoke-test staff login → bell icon → notifications screen
  before considering this closed.

## Not in scope (per brief, unchanged)

No pagination UI added to mobile (still shows first page only, matching prior behavior).
`useNotifications.ts` and `NotificationItem.tsx` needed no changes. No handoff file — self-contained.
