# TASK-682 — Supplier-portal expansion Phase 2 (frontend: supplier inventory + batch receiving UI)

**Status:** review (not pushed) · **Agent:** frontend-developer · Plan `.claude/plans/1-partitioned-book.md` Phase 2
Backend contract: `.claude/logs/tasks/681_2026-09-03_supplier-phase2-inventory_backend-developer.md`

## What changed

### Nav — `frontend/components/layout/Sidebar.tsx`
- Import `Boxes` (lucide-react).
- `buildSupplierNavGroup` += `/supplier/inventory` after `/supplier/warehouses`
  (`roles: SUPPLIER_ONLY`, `permission: "warehouse_management"`, `moduleKey: "supplier_inventory"`).

### `frontend/features/supplier-cabinet/`
- **`types.ts`** += `SupplierStockStatus`, `SupplierStock`, `SupplierStockReceipt`,
  `SupplierStockReceiptItem`, `SupplierStockReceiptStatus`, and 5 request types
  (`AddSupplierBatchRequest`, `AdjustSupplierStockRequest`, `CreateSupplierReceiptRequest`,
  `UpdateSupplierReceiptRequest`, `AddSupplierReceiptLineRequest`) — field names verbatim from the
  backend DTOs.
- **`api/supplier-cabinet-api.ts`** += 10 methods on `supplierCabinetApi`: `getWarehouseStock`,
  `addStockBatch`, `adjustStockBatch`, `listReceipts`, `createReceipt`, `getReceipt`,
  `updateReceipt`, `addReceiptLine`, `removeReceiptLine`, `finalizeReceipt`.
- **`hooks/useSupplierInventory.ts`** (new) — `useWarehouseStock`, `useAddStockBatch`,
  `useAdjustStockBatch`, `useSupplierReceipts`, `useSupplierReceipt`, `useCreateReceipt`,
  `useUpdateReceipt`, `useAddReceiptLine`, `useRemoveReceiptLine`, `useFinalizeReceipt`.
  Query keys `["supplier","stock",…]` / `["supplier","receipts",…]`; mutations invalidate the
  relevant prefix; `useFinalizeReceipt` also invalidates the stock root.
- **`components/WarehouseStockTable.tsx`** (new) — warehouse `<select>` (own `useSupplierWarehouses`),
  paged FEFO `Table` (item / expiry+daysLeft hint / on-hand / initial / batch / status chip /
  source / "Коригувати"). Status chip reuses `STATUS_COLOR` from `@/features/shelf/types`.
  Adjust modal → `{ quantity, reason? }`; the backend concurrency string is swapped for the
  localized `adjustModal.concurrencyRetry` key, any other error shown as-is.
- **`components/SupplierReceiptForm.tsx`** (new) — modal. Lifecycle: create draft → add lines
  (one POST `/lines` per line, `quantity` required > 0 client-side, `expiryDate` optional) →
  finalize. Multiple lines may share a `supplierItemId` — no `isRowAdded` guard; a "+ ще партія"
  button on saved lines and pending rows appends another row for the same item. Finalize 400
  `{ error }` (names the count of lines missing an expiry) is surfaced verbatim. Non-draft
  receipts open read-only.
- **`components/SupplierReceiptsList.tsx`** (new) — warehouse `<select>` + status filter +
  "Новий прийом"; `Table` (status pill / warehouse / reference / date / line count); row click
  opens the form (resume for draft, read-only for finalized/cancelled).

### Page — `frontend/app/(dashboard)/supplier/inventory/page.tsx` (new)
Shell copied from `supplier/warehouses/page.tsx` (`SUPPLIER_ONLY` + `warehouse_management` guards,
`Dashboard.supplierCabinet.pages.inventory.*`). Two tabs "Залишки" / "Прийоми" (same inline
tab pattern as `marketplace/orders/page.tsx`).

### i18n — `frontend/messages/{uk,en}.json`
- `Dashboard.sidebar.groups.supplierCabinet.inventory` = "Склад" / "Inventory".
- `Dashboard.supplierCabinet.pages.inventory.*` + `stockTable.*` (+ `.status.*`, `.source.*`,
  `.adjustModal.*`) + `receiptForm.*` + `receiptsList.*` (+ `.status.*`).
- +104 keys per language; parity 5673 == 5673, no diff.

## Deviations / notes

- **PagedResult for stock** — used `@/lib/api-types` `PagedResult<T>` (`{ items, totalCount, page,
  pageSize, totalPages? }`), aliased `ApiPagedResult` to avoid the name clash with the
  supplier-cabinet-local `PagedResult` (which is the marketplace **reviews** shape `{ items, total,
  page, pageSize }`). The brief's "reuse the local one" note was based on the wrong shape — the
  supplier stock endpoint returns `ShelfGuard.Application.Common.PagedResult`.
- **Receipt line persistence** — the backend has no line-update endpoint (POST/DELETE `/lines`
  only). The form therefore persists each line via an explicit "Додати позицію" action (one POST
  per line) rather than inline-editing a persisted line; changing a saved line = remove + re-add.
  This matches the add/remove-only API and keeps every mutation atomic and independently
  error-surfaced. Documented in the component header.
- **Adjust concurrency** — controller returns the concurrency conflict as a 400 with the raw
  Ukrainian string (not a 409), so the component string-matches `"інша операція"` to substitute
  the localized message for EN users.
- Receipts-list status filter (draft/received/cancelled) added as a small extra — cheap, uses the
  `?status=` query the endpoint already accepts.

## Build / tests
- `cd frontend && npx tsc --noEmit` — clean.
- `npx next lint --dir features/supplier-cabinet …` — clean.
- `npx next build` — success (exit 0), `/supplier/inventory` prerendered static, 8.11 kB.
  (The `ENVIRONMENT_FALLBACK` lines in the build log are the pre-existing next-intl static-render
  noise seen on every client page, not from this change.)
- i18n parity script — 5673 == 5673, `[] []`.

## Pending debt
- `backend/openapi.json` regen (batched with TASK-670..681).
- Not committed.
