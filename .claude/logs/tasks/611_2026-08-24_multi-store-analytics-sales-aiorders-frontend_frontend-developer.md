# TASK-611: Multi-store frontend fix — Analytics, Sales, AI-Orders

**Agent:** frontend-developer
**Status:** done

## What changed

Follow-up to TASK-609 (Dashboard) and TASK-610 (backend widening). Converted the three
pages' read paths from `usePrimaryStoreId()` to the full `selectedStoreIds` array
(`useStoreContext`), matching `frontend/features/dashboard/hooks/useDashboard.ts`'s pattern
exactly — repeated `storeIds=<id>` query params, empty array = all stores.

**Analytics** (`frontend/app/(dashboard)/analytics/page.tsx`): all 8 endpoints via
`frontend/features/analytics/api/analytics.ts` + `hooks/useAnalytics.ts` widened from
`store_id?: string` to `storeIds?: string[]` (new `appendStoreIds` helper mirrors dashboard's
`withStores`): `expiry-summary`, `write-offs` (+compare), `losses` (+compare), `by-zone`,
`by-category`, `losses/trend`, `losses/by-product`, `by-category/products`. Page now reads
`selectedStoreIds` via `useStoreContext` for all 8 calls. `CategoryDetailPanel.tsx` and
`LossesProductBreakdownPanel.tsx` (drill-down components hitting 2 of the 8 endpoints
directly) had their `storeId?: string` prop renamed to `storeIds?: string[]` — page-wide
drill-downs (category, reason, day) now pass the full `selectedStoreIds`; the losses-by-store
drill-down (a specific clicked store row) passes a single-element array
`[selectedLossDimension.value]`, staying scoped to that one store regardless of header
selection. `primaryStoreId` kept only for `ProductTrendPanel` (single-store ADU/stock lookup,
out of scope). `getMovements` (`/movements`) untouched, per TASK-610.

**Sales** (`frontend/app/(dashboard)/sales/page.tsx`): read-list path only —
`SalesFilters.storeId` → `storeIds?: string[]`, `salesApi.getAll`'s `query()` builds repeated
params. `usePrimaryStoreId()` kept for the entry form / CSV import (single-store writes,
untouched).

**AI-Orders** (`frontend/app/(dashboard)/ai-orders/page.tsx`): read-list path only —
`aiOrdersApi.getList(storeIds?: string[])`, `useAiOrders(storeIds?: string[])`.
`usePrimaryStoreId()` kept for `generate` (single-store write, untouched).

**Orders page**: not touched, per brief (no GET/list view, mutation-only).

## Build / verification

- `npx tsc --noEmit`: clean, 0 errors.
- Ran `backend-dev` (port 5000) + `frontend-dev` (port 3001), logged in with persisted session
  (2 stores pre-selected from prior TASK-609 testing).
- Network requests confirmed for all in-scope endpoints with 2 stores selected: all 8 Analytics
  endpoints (including drill-downs `losses/by-product` and `by-category/products`, triggered by
  clicking a reason row and a category row) sent repeated `storeIds=<id>&storeIds=<id>` and
  returned 200 OK; Sales `daily-sales` and AI-Orders `ai-orders` list likewise.
- `by-category/products` response confirmed `daysOfStockRemaining: null` on every row with 2
  stores selected (documented backend behavior) — page already renders null as "—", no frontend
  change needed.
- Regression check: switched to 1 store via localStorage (mirrors the header selector's
  persisted state) — `ai-orders` correctly sent a single `storeIds=<id>` param, 200 OK.
- No console errors on Analytics, Sales, or AI-Orders after fresh navigations (a handful of
  401/RSC-prefetch console entries were confirmed stale, from earlier Fast-Refresh cycles during
  editing — the console tail after each final navigation was clean).

## Files changed

- `frontend/features/analytics/api/analytics.ts`
- `frontend/features/analytics/hooks/useAnalytics.ts`
- `frontend/features/analytics/components/CategoryDetailPanel.tsx`
- `frontend/features/analytics/components/LossesProductBreakdownPanel.tsx`
- `frontend/app/(dashboard)/analytics/page.tsx`
- `frontend/features/sales/types.ts`
- `frontend/features/sales/api/sales.ts`
- `frontend/app/(dashboard)/sales/page.tsx`
- `frontend/features/ai-orders/api/aiOrders.ts`
- `frontend/features/ai-orders/hooks/useAiOrders.ts`
- `frontend/app/(dashboard)/ai-orders/page.tsx`
