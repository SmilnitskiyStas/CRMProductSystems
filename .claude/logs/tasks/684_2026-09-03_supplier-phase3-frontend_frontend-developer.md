# TASK-684 — Supplier Phase 3 FRONTEND: batch-consuming ship modal + batch display (D4)

**Agent:** frontend-developer · **Plan:** `1-partitioned-book.md` Phase 3 · **Status:** review, NOT committed
Backend: TASK-683 (`.claude/logs/tasks/683_2026-09-03_supplier-phase3-shipping_backend-developer.md`), also uncommitted.

## What changed

### Types
- `frontend/features/marketplace/types.ts`
  - `MarketplaceOrderItemBatchDto` (new): `{ id, expiryDate, batchNumber|null, qty, supplierStockId|null }`.
  - `MarketplaceOrderItemDto` += `batches: MarketplaceOrderItemBatchDto[]` (always present, nearest-expiry-first).
  - `MarketplaceOrderDto` += `sourceWarehouseId: string|null`, `expectedDeliveryDate: string|null`.
  - `MarketplaceOrderReceiptItemDto` += `sourceOrderItemBatchId: string|null`.
- `frontend/features/supplier-cabinet/types.ts` += `ShipAllocation`, `ShipLine`, `ShipOrderRequest`,
  `ShipSuggestionAllocation`, `ShipSuggestionLine`, `ShipSuggestion`, `ShipOrderResult` (+ import of `MarketplaceOrderDto`).

### API + hooks
- `api/supplier-cabinet-api.ts` += `getShipSuggestion(orderId, warehouseId?)` (GET `.../ship-suggestion?warehouseId=`),
  `shipOrder(orderId, body)` (POST `.../ship`).
- `hooks/useCabinetCooperation.ts` += `useShipSuggestion(orderId, warehouseId)` (query, `enabled` only with both ids —
  i.e. modal open + warehouse picked), `useShipOrder()` (mutation, invalidates `CABINET_COOP_KEYS.orders` +
  `SUPPLIER_INVENTORY_KEYS.stockRoot`).

### Ship modal
- **`EstimateDeliveryModal.tsx` → renamed (`git mv`) to `ShipOrderModal.tsx`.** Only consumer was
  `CabinetOrdersTab.tsx`; low-risk. Component is now `ShipOrderModal({ order, onClose })` and owns its own
  mutations + toasts (previously the parent owned them).
  - Module state via `useModules()` → `modules?.modules?.includes("supplier_inventory")`. (Brief said
    `modules?.includes(...)`; the hook actually returns `{ businessType, modules }`, so `.modules.includes`.)
  - **Module OFF** — unchanged behaviour: single positive-integer days input → `useUpdateCabinetOrderStatus`
    `{ status:"shipped", estimatedDeliveryDays }`.
  - **Module ON** — warehouse `<select>` (`useSupplierWarehouses`, default first active) → `useShipSuggestion`
    → per-line card: item name + ordered qty + editable allocation grid (expiry / batch / available / **qty input**,
    prefilled from the suggestion) + a live shortfall chip when `covered < qty`. Expected-delivery-date input OR
    days input (≥1 required; days→date derived and shown as a hint). Submit → `useShipOrder` with
    `{ sourceWarehouseId, expectedDeliveryDate?, estimatedDeliveryDays?, lines:[{orderItemId, allocations:[{supplierStockId, qty}]}] }`
    (only qty>0 allocations; only lines with ≥1 such allocation). On success: success toast + one
    `toast.warning` per `result.warnings` entry (shortfall is not an error).
  - **Module ON but no active warehouse** — fallback: submits `/ship` with only the ETA (no `sourceWarehouseId`,
    no `lines`), which the backend routes through its legacy no-consume branch. Keeps ship working.

### Batch display (read-only)
- `CabinetOrdersTab.tsx` + `app/(dashboard)/marketplace/orders/page.tsx` expanded rows: under each order line,
  when `item.batches.length > 0`, a small "Партії/Batches" sub-list (`expiry · batchNumber · qty`).
- Both `ShippingDetail` components now prefer `order.expectedDeliveryDate` over the client-derived ETA date,
  reusing the existing `estimatedDeliveryLabel` key (no new label added — it already fits).
- Buyer receipt view (`ReceiptItemsTable` in `marketplace/orders/page.tsx`): iterates `receipt.items`, so the
  1→N batch sub-rows render as N rows with no structural change. Verified by reading; columns still align.

### i18n — `messages/{uk,en}.json`
+23 keys per language: `Dashboard.supplierCabinet.ordersTab` gets the ship-modal strings (`shipModalShipButton`,
`shipModalWarehouseLabel`, `shipModalNoWarehouseHint`, `shipModalSuggestionLoading`, `shipModalSuggestionError`,
`shipModalNoBatches`, `shipModalLineOrdered`, `shipModalAlloc{Expiry,Batch,Available,Qty}`, `shipModalShortfallChip`,
`shipModalExpectedDateLabel`, `shipModalEtaHint`, `shipModalDerivedDateHint`, `shipModalEtaRequired`) + `batchesLabel`;
`Dashboard.marketplace.ordersPage.ordersTab` gets `batchesLabel`. Parity verified: 5513 == 5513 key-paths, 0 diff.

## Verification
- `npx tsc --noEmit` — clean.
- `npx next lint --dir features/supplier-cabinet --dir app/(dashboard)/marketplace` — no warnings/errors.
- `npx next build` — exit 0, `✓ Compiled successfully`, all routes present (`/supplier/orders`, `/marketplace/orders`).
  `ENVIRONMENT_FALLBACK` lines during static gen are pre-existing noise.
- No preview run — another chat's dev server holds the port; changes are inside an authed dashboard flow that
  needs a supplier tenant with the module toggled on (data not available in this session).

## Decisions / deviations
- **Modal renamed** to `ShipOrderModal` (file + component). Single consumer updated.
- Modal now self-contained (owns mutations/toasts) rather than presentational — cleaner than threading the
  suggestion query + two mutations through `CabinetOrdersTab`.
- `expectedDeliveryDate` reuses `estimatedDeliveryLabel` rather than adding a near-duplicate key.
- If the user zeroes every allocation for a line, that line is omitted from the request and the backend
  auto-FEFOs it. Acceptable for v1 (shortfall is allowed anyway); no "ship nothing for this line" signal exists
  in `ShipLineDto`.
- Backend `warnings` strings are built server-side in Ukrainian regardless of UI locale (documented in the
  backend log) — surfaced as-is.

## Known limitation (not in scope)
- A **custom** supplier role WITHOUT `warehouse_management` but able to view orders, on a tenant with
  `supplier_inventory` ON: `POST /ship` returns 403, shown as an inline form error. The default `supplier_admin`
  role has the permission, so this only bites bespoke restricted roles. The ship button is not permission-gated
  in `CabinetOrdersTab`. Left as-is per scope.

## Follow-ups (shared debt, not this task)
- `backend/openapi.json` regen (TASK-670..674 + Phase 1/2/3).
- Mobile receiving screen for N batch sub-rows — handoff `.claude/logs/handoffs/phase3-mobile-receipt-batches.md`.
