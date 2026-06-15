# TASK-081 — Mobile: Dashboard з реальними даними
**Agent:** mobile-developer
**Date:** 2026-06-15
**Status:** done

## Changes

### `mobile/features/dashboard/types.ts`
- Extended `DashboardStats` to include `needsVerification` + `total` (matches `StockSummaryDto`)
- Added `AiOrderListItem` type (matches `GET /api/ai-orders` response)
- Added `RecentMovement` + `RecentMovementsPage` types (matches `GET /api/movements`)
- Added `MOVEMENT_LABELS` map (receipt/transfer/write_off/adjustment/pos_sale → UA strings)
- Added `AT_LEAST_STORE_MANAGER_ROLES` constant for client-side role gating

### `mobile/features/dashboard/api/dashboardApi.ts`
- Switched from `/analytics/expiry-summary` → `/stock/summary` for status cards
  - Lower privilege: `CanViewStock` instead of `CanViewAnalytics` — works for storekeeper/merchandiser too
- Added `getAiOrders()` → `GET /api/ai-orders`
- Added `getRecentMovements(limit)` → `GET /api/movements?page=1&page_size=5`

### `mobile/features/dashboard/hooks/useDashboard.ts`
- Added `useAiOrders()` hook
- Added `useRecentMovements()` hook

### `mobile/app/(app)/index.tsx`
- Replaced placeholder stats with real data from `useDashboardStats()` (now hitting `/stock/summary`)
- Added AI Orders banner (indigo card): visible only for `AT_LEAST_STORE_MANAGER_ROLES`, shows count of `status === 'pending'` orders
- Added Recent Events section: last 5 movements from `/api/movements` with type icon, store name, quantity, formatted date
- Added `RefreshControl` on `ScrollView` — all three queries refetch on pull

## Notes
- `MovementDto.ProductName` is null in list responses (backend sets it to null in GetAsync). Event rows show movement type + store name + qty instead.
- AI orders query is always fetched (React Query handles it regardless), but the banner is conditionally rendered client-side by role.
- `tsc --noEmit` green (no output).

## Acceptance criteria
- [x] `tsc --noEmit` green
- [x] Status cards use real numbers from `/api/stock/summary`
- [x] AI orders section shows pending count (manager+ only)
- [x] Recent events section shows last 5 movements
- [x] Pull-to-refresh works (all three queries)
