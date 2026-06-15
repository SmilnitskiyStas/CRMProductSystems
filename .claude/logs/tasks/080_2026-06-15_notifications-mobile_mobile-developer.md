# TASK-080 — Mobile: Notifications screen
**Agent:** mobile-developer
**Date:** 2026-06-15
**Status:** done

## What was built

### New files
- `mobile/features/notifications/types.ts` — `Notification` interface + `categorize()` helper (expiry/stock/system)
- `mobile/features/notifications/api/notificationApi.ts` — `GET /api/notifications/history`
- `mobile/features/notifications/hooks/useNotifications.ts` — `useNotificationHistory()` with 60s auto-refetch
- `mobile/features/notifications/store.ts` — Zustand store for in-session read/unread tracking (`readIds: Set<string>`)
- `mobile/features/notifications/components/NotificationItem.tsx` — List item with type icon (expiry=amber clock, stock=blue layers, system=gray bell), read/unread visual styles (green-50 bg + left border strip when unread)
- `mobile/features/notifications/components/NotificationBell.tsx` — Header bell button with red badge showing unread count; navigates to `/(app)/notifications`
- `mobile/app/(app)/notifications.tsx` — FlatList screen with pull-to-refresh, "Всі прочитані" button, empty state

### Modified files
- `mobile/app/(app)/_layout.tsx` — Dashboard tab: `headerShown: true` + `headerRight: NotificationBell`; registered `notifications` as hidden screen
- `mobile/app/(app)/index.tsx` — Removed duplicate static bell icon, removed inline "Дашборд" title (header now owns it), changed `SafeAreaView` to `edges={['bottom','left','right']}` to avoid double top inset

## Read/unread tracking
Backend `NotificationHistoryDto` has no `isRead` field (delivery `status` only). Tracked client-side in Zustand (`Set<string>` of readIds). Resets on app restart — acceptable for current scope; persisted read state would require a backend endpoint (out of scope).

## Acceptance criteria
- [x] `tsc --noEmit` green (no output = no errors)
- [x] List loads from `GET /api/notifications/history`
- [x] Bell badge shows unread count in Dashboard header
- [x] Tap notification → marks as read (style changes, badge decrements)
- [x] "Всі прочитані" button marks all as read
- [x] Pull-to-refresh works
