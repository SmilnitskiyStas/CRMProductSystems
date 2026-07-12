# TASK-340: Notification filtering — UI (frontend) — DONE

**Agent:** frontend-developer · **Date:** 2026-07-12

## Scope
Per handoff `339-to-340_frontend-developer.md` and ADR-018 §4/§5: consume the new paginated/filtered
`GET /api/notifications/history`, add 6 new/newly-labelled `NotificationEventType` values, build a
hand-rolled filter drawer (search + category + employee + store + date range), and add pagination to
the history list.

## What was built

1. **Types** (`frontend/features/notifications/types.ts`) — `NotificationEventType` gained
   `receipt.created`, `order.replenishment_suggested`, `supplier.message`,
   `supplier_agreement.signed`, `iot.temp_alert`, `iot.offline` with Ukrainian labels in
   `EVENT_TYPE_LABELS`/`EVENT_TYPE_SOURCE` (values from the handoff table). `NotificationChannel`
   gained `webhook` (valid per backend `ValidChannels`, previously unmapped → would have rendered
   `undefined`). `NotificationHistoryItem` gained `title`, `storeId`, `userId` (all `string | null`,
   nullable on pre-TASK-338 rows) and a client-side-only `storeName?` enrichment field. Added
   `NotificationHistoryFilters` (search/eventType/userId/storeId/dateFrom/dateTo/page/pageSize).

2. **API/hooks** — `fetchNotificationHistory` now takes a `NotificationHistoryFilters` object, builds
   a query string, and returns `PagedResult<NotificationHistoryItem>` (added `totalPages?` to the
   shared `PagedResult<T>` in `frontend/lib/api-types.ts`). `useNotificationHistory(filters)`'s query
   key now carries the filters object; `MOCK_HISTORY` fallback removed (backend fully wired).
   `useMarkAsRead`/`useMarkAllAsRead`/`useMarkAsUnread` switched from single-key `setQueryData` to
   prefix-based `invalidateQueries` — the old approach silently stopped updating anything once the
   query key started varying by filters.

3. **`NotificationFilterDrawer.tsx`** (new) — hand-rolled fixed-panel + backdrop overlay, same pattern
   as `NotificationDetailDrawer.tsx` (no shadcn Sheet in this repo). Search input debounces 300ms;
   category/employee/store/date fields apply immediately and reset `page` to 1. Employee options from
   `useUsers()` (`frontend/features/users`), store options from `useLocations()`
   (`frontend/features/locations` — `frontend/features/stores` is just a re-export alias of it). Plain
   `<input type="date">` fields (the existing `DateRangePicker` is analytics-specific with a
   non-optional compare-period toggle that doesn't fit this use case). "Скинути фільтри" clears
   everything but keeps `pageSize`.

4. **`NotificationHistoryList.tsx`** — now takes `filters`/`onFiltersChange` props, reads
   `result.items`/`totalCount`/`page`/`totalPages` from the paged response, resolves `storeName` from
   `useLocations()` client-side, prefers `item.title` over the old payload-parsing heuristic (falls
   back to it for pre-TASK-338 rows where `title` is null), and adds a simple Назад/Далі pagination
   footer ("Сторінка X з Y · усього N") — chosen over "load more" as the simpler variant per the brief.

5. **`app/(dashboard)/notifications/page.tsx`** — owns `filters` state (single source of truth, passed
   to both the list and the drawer), a Filter-icon trigger button in the header showing an active-filter
   count badge, and renders `NotificationFilterDrawer`.

## Build / verification
- `npx tsc --noEmit` — clean.
- `npm run build` — succeeds, `/notifications` route compiles (9.5 kB).
- Verified live in browser (dev server + local `dotnet run` API + existing Docker Postgres/Redis):
  seeded 31 temporary `notification_queue` rows (mix of old title-less rows and new rows across all 11
  event types, incl. `webhook` channel) via `docker exec psql`, confirmed labels/icons/store-name
  render correctly, pagination (`Сторінка 1 з 2 → 2 з 2`) works, search debounce fires
  `?search=молоко&page=1` ~300ms after typing stops, category select combines with search via AND
  (verified empty-result and single-result cases), trigger badge shows correct active count, and reset
  restores the full list. Deleted all seeded rows afterward (tenant had 0 rows before this session) and
  stopped the locally-started backend process; dev frontend preview server also stopped.

## Files changed
- `frontend/features/notifications/types.ts`
- `frontend/features/notifications/api/notifications.ts`
- `frontend/features/notifications/hooks/useNotifications.ts`
- `frontend/features/notifications/components/NotificationHistoryList.tsx`
- `frontend/features/notifications/components/NotificationFilterDrawer.tsx` (new)
- `frontend/app/(dashboard)/notifications/page.tsx`
- `frontend/lib/api-types.ts` (added optional `totalPages` to shared `PagedResult<T>`)

## Next
None — this closes out the ADR-018 notification-filtering feature (TASK-338/339/340). No further
handoff needed; `notification_settings` UI toggle for the 4 new outbox-driven event types remains
explicitly out of scope per the TASK-339 handoff.
