# TASK-629 — Fix store-scope flash on login + add pagination (receipts/transfers/write-offs/stock)

**Status:** done · **Agent:** frontend-developer

## What changed

**Part A — flash fix (store-scope-ready gating):**
- `frontend/lib/useStoreContext.ts` — added `hasHydrated`/`setHasHydrated` (set via zustand
  persist's `onRehydrateStorage`) and exported `useStoreScopeReady()` = `hasHydrated && initialized`.
- `frontend/components/layout/StoreSelector.tsx` — default-resolution effect now bails until
  `hasHydrated` is true, so a hard reload can't clobber an explicit "all stores" choice.
- Gated on `useStoreScopeReady()` (query `enabled` + `isLoading = !ready || query.isLoading`):
  `useReceipts`, `useTransfers`, `useWriteOffs`, `useStock`, `useUsers`, `useAiOrders`,
  `useEvents`, `useDailySales`, and all 5 hooks in `useDashboard.ts`.
- Page-level `enabled` composition (`&& ready`): `analytics`, `analytics/pos`,
  `marketing-analytics` (root, post-campaign, audience-builder, price-segments).
- `locations/page.tsx`: loading branch now checks `showLoading = isLoading || !ready`.
- Floor-plan zone counts (`useZoneStatusCounts` in `useFloorPlan.ts`): switched from
  `locationsApi.getStock()` (whole-tenant fetch) to `stockApi.getAll({ store_id: locationId,
  pageSize: 200 })` — scoped server-side to one location, no dependency on the global selector.
  Also fixed a pre-existing bug: the old code compared `b.locationId` against `locationId`, but
  `ProductStockDto` has no `locationId` field (it's `storeId`) — counts were silently always
  empty. Removed the now-unused `locationsApi.getStock`/`StockBatchSlim` and their re-export in
  `features/stores/api/stores.ts` (confirmed zero other consumers).

**Part B — real pagination (receipts/transfers/write-offs/stock):**
- API layer (`receipts.ts`, `transfers.ts`, `writeOffs.ts`, `stock.ts`): added `page`/`pageSize`
  query params, return the full `PagedResult<T>` envelope instead of discarding it via
  `.then(r => r.items)`.
- Hooks (`useReceipts`, `useTransfers`, `useWriteOffs`, `useStock`): accept `page`/`pageSize`
  (default `pageSize=50`), include them in the query key, `placeholderData: (prev) => prev`.
  Combined with the Part A ready-gating in the same pass.
- New `frontend/components/ui/Pagination.tsx` — generic prev/next + "page X/Y" + total-count
  footer, extracted from `TableControls.tsx`'s `TablePaginationFooter` but using generic
  `Common.prev`/`next`/`pageOf`/`totalLabel` i18n keys (added to `messages/{uk,en}.json`)
  instead of the price-segments-specific namespace. The 3 existing duplicated footers were left
  untouched.
- Wired into `receipts/page.tsx`, `transfers/page.tsx`, `write-offs/page.tsx`, `stock/page.tsx`:
  local `page` state, reset to 1 on filter change, footer rendered only in the "has data" branch.
- Downstream fallout from `useStock`'s return shape changing to the full envelope: updated the
  3 other consumers to unwrap `.items` — `CreateWriteOffForm.tsx`, `CreateTransferForm.tsx`
  (both via the hook), and `ProductTrendPanel.tsx` (calls `stockApi.getAll` directly).

## Verification

- `npx tsc --noEmit` — 0 errors.
- `npm run lint` — 0 warnings/errors (fixed 2 new `react-hooks/exhaustive-deps` warnings by
  wrapping `data?.items ?? []` in `useMemo` on the stock/write-offs pages).
- Manual smoke test against the real backend (dotnet run + local Postgres) via browser:
  dashboard, receipts, transfers, write-offs, stock, locations, and one floor-plan page all
  load cleanly, no console errors beyond the routine access-token-refresh 401. Confirmed via
  network trace that store-scoped requests only ever fire with the resolved `storeIds` (no
  empty-filter tenant-wide request before resolution — the flash is gone). Confirmed stock
  pagination end-to-end: page 1 showed "Batches: 50 / Total: 645 / 1 of 13", clicking Next
  fired `GET /api/stock?...&page=2&pageSize=50` and rendered different rows. Confirmed the
  floor-plan zone-counts fix: `GET /api/stock?storeIds={locationId}&pageSize=200` fires with
  response items carrying `storeId` matching the URL's locationId and populated `zoneId`s.

No known issues.
