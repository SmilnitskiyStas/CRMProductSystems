# Handoff → mobile (Codex agent): marketplace receiving is now 1→N per order line

**From:** backend-developer (TASK-683, supplier-portal expansion Phase 3, plan `1-partitioned-book.md` D4)
**Date:** 2026-09-03
**Status:** backend on `main` working tree, **not yet deployed**. Nothing in `mobile/` was touched.

## What changed on the server

A supplier with the `supplier_inventory` module on now ships an order by allocating specific
warehouse batches (expiry + batch number). Those allocations are handed to the buyer, and the
buyer's receiving draft is **prefilled from them**.

Concretely: `POST /api/marketplace/orders/{orderId}/receipt` used to return exactly one
`items[]` entry per order line. It now returns **one entry per shipped batch** — so a single
ordered position of 120 units can come back as two rows (100 expiring 2026-12-01, 20 expiring
2027-02-01). Several rows now share the same `marketplaceOrderItemId`.

## API deltas (that is all of them)

`MarketplaceOrderReceiptItemDto` gains one field:

```ts
sourceOrderItemBatchId?: string | null
```

- **non-null** → this row was prefilled from a supplier batch: `expiryDate`, `batchNumber` and
  `quantityOrdered` already carry the supplier's values.
- **null** → legacy shape (an order shipped before Phase 3, or by a supplier without the
  module): one blank row per line, `expiryDate`/`batchNumber` null, exactly as today.

`MarketplaceOrderDto` gains `sourceWarehouseId?`, `expectedDeliveryDate?`, and per item
`batches: { id, expiryDate, batchNumber?, qty, supplierStockId? }[]` (always present, possibly
empty, nearest-expiry-first). The receiving screen does not need `batches` — it is the same data
already flattened into the receipt items — but the order-detail screen can show "shipped as N
batches".

**`PUT /api/marketplace/orders/{orderId}/receipt/items/{itemId}` is unchanged**: same route,
same body, same merge semantics. Scan-one-commit-one still works exactly as it does now.

**`POST .../receipt/finalize` is unchanged**, including its gate: every item still needs
`productId` + `quantityReceived` + `expiryDate`. Prefilling only pre-answers `expiryDate`; the
employee still scans the product and enters the count on **every** sub-row.

## What the mobile receiving screen must change

1. **Group by `marketplaceOrderItemId`.** Render one header per ordered position
   (`itemNameSnapshot`, total ordered = sum of the group's `quantityOrdered`) and the group's
   items as sub-rows. Do not assume one item per position any more — a screen that renders a
   flat list will show "Молоко 2.5%" twice with no explanation.
2. **Label each sub-row by its batch**: `expiryDate` (and `batchNumber` when present) is the
   thing that distinguishes them. Suggested sub-row title: `до {expiryDate}` + `партія
   {batchNumber}`.
3. **Scan-one-commit-one per sub-row**, not per position. Each sub-row gets its own
   `PUT .../receipt/items/{itemId}` with its own `quantityReceived`. The barcode scan resolves
   the same `productId` for every sub-row of a position (same physical product) — a reasonable
   UX is: scan once at the position level, apply the resolved `productId` to each sub-row as the
   employee confirms its count.
4. **Keep `expiryDate` editable.** It is prefilled, not locked — if the pallet that arrived does
   not match what the supplier recorded, the employee corrects it and the discrepancy shows up
   through `discrepancyNotes` as usual.
5. **Per-position progress** = all sub-rows `isResolved`. `isResolved` is still per item and
   still means productId + quantityReceived + expiryDate all set.
6. **Legacy orders keep the current UI verbatim** — one row per position, blank expiry. Branch on
   `sourceOrderItemBatchId != null` (or on the group having more than one row), never on a
   feature flag.

## Result of finalizing

One client `ProductStock` batch per sub-row, each with its own `expiryDate`/`batchNumber` — i.e.
the supplier's FEFO batches survive the tenant boundary intact instead of collapsing into one
hand-typed expiry. Order still moves to `delivered` on finalize, same as today.

## Reference

- Backend task log: `.claude/logs/tasks/683_2026-09-03_supplier-phase3-shipping_backend-developer.md`
- API contract: `.claude/docs/api-contracts.md` → "Supplier cabinet — batch-consuming shipment"
- ADR: `.claude/docs/decisions.md` → ADR-033, amendment 2026-09-03
