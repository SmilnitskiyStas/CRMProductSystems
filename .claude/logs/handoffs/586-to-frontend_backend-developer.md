# Handoff: TASK-586 → frontend-developer (and future mobile-facing contract doc)

**From:** backend-developer (stage 3/4)
**Full task log:** `.claude/logs/tasks/586_2026-08-21_marketplace-order-receiving-logic_backend-developer.md`
**Spec:** ADR-033 in `.claude/docs/decisions.md` (read Decision 4 and Decision 5 in full — this
handoff summarizes but the ADR is the rationale source of truth)

This document is written to stand alone — everything a caller needs to build against these
endpoints, including auth and error handling, without reading the backend diff. It's also the
primary source the orchestrator will draw from when writing the eventual mobile/Codex-facing
handoff, so routes/shapes below are final, not provisional.

## What changed on existing endpoints (frontend must account for)

1. **`POST /api/marketplace/suppliers/{supplierId}/orders`** (order creation) now requires a
   `destinationStoreId` (`Guid`) in the request body. Omitting it (or sending `null`) → `400` with
   `{ "error": "Оберіть магазин-призначення для замовлення." }`. **You need to add a required
   store picker to the order/cart creation form** (per ADR-033 Decision 2 — an explicit choice,
   not inferred from any "current store" context, since an order is a future delivery to one
   specific store). Request shape now:
   ```json
   { "items": [{ "supplierItemId": "guid", "qty": 1 }], "comment": "string|null", "destinationStoreId": "guid" }
   ```

2. **`MarketplaceOrderDto`** (returned by order create/list/cancel — unchanged shape otherwise)
   gained one field: `destinationStoreId: "guid" | null`. Null on any order placed before this
   migration shipped — historical orders can never be received through the new flow, so treat
   `null` there as expected/permanent, not a loading state.

3. **The supplier cabinet's "Deliver" button is now dead.** `POST
   /api/supplier-cabinet/orders/{id}/status` with `{ "status": "delivered" }` on a `shipped` order
   now **always** returns `400` with
   `{ "error": "Перехід зі статусу 'shipped' у 'delivered' неможливий." }` — this is a plain
   backend behavior change, independent of whether you remove the button in the same deploy. Per
   the original plan (`CabinetOrdersTab.tsx`, `case "shipped":`), you should still remove the
   button and replace it with a status hint like "очікує підтвердження клієнтом" — but the API
   contract itself was already unambiguous the moment this stage merged.

## New endpoints — marketplace order receiving

All under the existing `MarketplaceCooperationController` (`/api/marketplace/...`), same auth as
every other endpoint in that controller: JWT bearer, `[Authorize]` + module `marketplace`. Reads
(a, c) need nothing beyond that. Mutations (b, d, e) additionally require the `CanReceiveStock`
policy (storekeeper role or above — same floor `ReceiptsController` uses for its equivalent
write actions).

Routes are **order-centric throughout** — every path takes `orderId`, never a separately surfaced
receipt id. The receipt is 1:1 with its order, so a caller who has an order id from `GET
/api/marketplace/my-orders` or `.../awaiting-receipt` never needs to learn or persist a second id.

### a. `GET /api/marketplace/orders/awaiting-receipt`
Shipped orders of the calling tenant that still need to be received. This is what a polling
screen (web read-only block, or the mobile "orders to receive" list) should call.
- **Response `200`:** `MarketplaceOrderDto[]` — the same existing DTO used everywhere else
  (carries `items`, `shippedAt`, `estimatedDeliveryDays`, `destinationStoreId`, etc.).
- No error cases — always `200`, possibly `[]`.

### b. `POST /api/marketplace/orders/{orderId}/receipt`
Starts a receiving session, or resumes one already in progress (**idempotent create-or-get** —
safe to call every time a user opens the "receive this order" screen, never errors on a repeat
call for the same order).
- **Response `200`:** `MarketplaceOrderReceiptDto` (see shape below).
- **`404`** `{ "error": "Замовлення не знайдено." }` — order doesn't exist or doesn't belong to
  the caller's tenant.
- **`400`** `{ "error": "Прийом можливий лише для відправлених замовлень." }` — order isn't
  `shipped` yet, or is already fully processed through a previous receiving session (its status
  will have moved to `delivered` — receiving is one-shot, no re-open).
- **`400`** `{ "error": "У замовлення не вказано магазин-призначення. Зверніться до підтримки." }`
  — the historical-gap case: an order shipped before `destinationStoreId` existed. Show this as a
  dead-end with a support contact, not a retryable error.

### c. `GET /api/marketplace/orders/{orderId}/receipt`
Read-only fetch — no side effects. Use this for the web's read-only "what was actually received"
block after `delivered`, and for the supplier cabinet's future read-only view (not built in this
stage — RLS already grants the supplier tenant `SELECT`-only access at the DB level via
`supplier_read`, but no supplier-facing endpoint exists yet; that's follow-up work, not blocking
for you).
- **Response `200`:** `MarketplaceOrderReceiptDto`.
- **`404`** `{ "error": "Документ прийому не знайдено." }` — no receiving session started yet for
  this order (call endpoint b first), or (for a foreign tenant) `{ "error": "Замовлення не
  знайдено." }` if the order itself isn't the caller's.

### d. `PUT /api/marketplace/orders/{orderId}/receipt/items/{itemId}`
Records a scan/count for one physical item — **one item per call, not bulk** (deliberate
deviation from the plain-Receipts feature's bulk `PUT /{id}/items`, to match a scan-one-commit-one
mobile flow). `itemId` is the receipt item's own id (`MarketplaceOrderReceiptItemDto.id`, from the
`items` array of the DTO returned by endpoint b/c) — not the order line's id.

**Request body** — all fields optional, only send what changed:
```json
{
  "productId": "guid|null",
  "quantityReceived": 4.5,
  "expiryDate": "2026-12-31",
  "batchNumber": "string|null",
  "discrepancyNotes": "string|null"
}
```
`productId` must already be resolved client-side — call the existing tenant-scoped
`GET /api/items/by-barcode/{code}` first (same endpoint POS/Stock/write-offs already use) and
pass the resulting id here. This endpoint does **not** resolve barcodes itself.

**Field semantics — important, matches the plain-Receipts feature's own convention exactly:**
- `quantityReceived` and `discrepancyNotes` **overwrite directly** — omitting the field (or
  sending `null`) clears it. If you want to keep a previously-set value, you must resend it every
  call.
- `productId`, `expiryDate`, `batchNumber` **merge** — omitting them (sending `null` or leaving
  them out) keeps whatever was already set; there's no way to explicitly clear them back to null
  once set (matches the plain-Receipts convention — not a new limitation introduced here).

- **Response `200`:** `MarketplaceOrderReceiptDto` (full receipt, so you can re-render the whole
  item list's progress after one item's update).
- **`404`** `{ "error": "Документ прийому не знайдено." }` (wrong tenant / no receipt) or
  `{ "error": "Позицію документа прийому не знайдено." }` (bad `itemId`).
- **`400`** `{ "error": "Документ прийому вже підтверджено." }` — receipt already finalized,
  no more edits possible.
- **`400`** `{ "error": "Отримана кількість не може бути від'ємною." }` — negative
  `quantityReceived`.
- **`400`** `{ "error": "Товар не знайдено у вашому каталозі." }` — `productId` doesn't resolve
  in the caller's own tenant catalog (shouldn't happen if you got it from the barcode-lookup
  endpoint, but guard for it — e.g. a stale/cached id).

### e. `POST /api/marketplace/orders/{orderId}/receipt/finalize`
The confirm/submit action. Gate: **every** item on the receipt must have `productId`,
`quantityReceived`, and `expiryDate` all set — not just the items the user touched. Use
`MarketplaceOrderReceiptItemDto.isResolved` (see below) to show per-item completion state and
disable the finalize button client-side until every item is resolved, rather than relying only on
the server error.

- **Response `200`:** `MarketplaceOrderReceiptDto` with `status: "received"`. As a side effect,
  the underlying `MarketplaceOrder.status` becomes `"delivered"` — if you're polling/caching the
  order list separately, invalidate it after a successful finalize.
- **`404`** — same two shapes as endpoint b/c (order not found, or receipt not found).
- **`400`** `{ "error": "Документ прийому вже підтверджено." }` — already finalized (repeat call).
- **`400`** `{ "error": "Усі позиції мають бути відскановані з кількістю та терміном придатності
  перед підтвердженням." }` — the gate: at least one item is still missing productId/quantity/
  expiry.

Discrepancies (`quantityReceived != quantityOrdered`) are **never blocking** — `discrepancyNotes`
is purely informational, same posture as the plain-Receipts feature.

## DTO shapes (exact field names, camelCase over the wire)

```ts
type MarketplaceOrderReceiptDto = {
  id: string;                    // guid
  marketplaceOrderId: string;
  clientTenantId: string;
  supplierTenantId: string;
  destinationStoreId: string;
  destinationStoreName: string;  // "—" if somehow unresolved, shouldn't happen
  status: "draft" | "received";
  createdByUserId: string | null;
  receivedByUserId: string | null;
  receivedAt: string | null;     // ISO 8601
  createdAt: string;
  updatedAt: string;
  items: MarketplaceOrderReceiptItemDto[];
};

type MarketplaceOrderReceiptItemDto = {
  id: string;                    // use this as {itemId} in endpoint d
  marketplaceOrderItemId: string; // the order line this closes — not usually needed client-side
  productId: string | null;      // set once scanned
  itemNameSnapshot: string;      // what the employee is supposed to be scanning — always present
  productName: string | null;    // resolved product name, null until productId is set
  quantityOrdered: number;
  quantityReceived: number | null;
  expiryDate: string | null;     // "YYYY-MM-DD"
  batchNumber: string | null;
  discrepancyNotes: string | null;
  isResolved: boolean;           // productId && quantityReceived && expiryDate all set —
                                  // the exact per-item finalize-gate condition, precomputed
};
```

## Web-side scope for this stage (per the original plan, section 4 — read-only only)

- `frontend/features/supplier-cabinet/components/CabinetOrdersTab.tsx` — remove the "Deliver"
  button on `shipped` orders (dead API call as of this stage, see item 3 above), replace with a
  status hint.
- `frontend/app/(dashboard)/marketplace/orders/page.tsx` + `CabinetOrdersTab.tsx` — after
  `delivered`, show a read-only block of what was actually received (call endpoint c) — quantity/
  batch/expiry per item, discrepancies if any. Same pattern already used for `ShippedAt`/
  `EstimatedDeliveryDays`/`DelayReason` display (TASK-584/585).
- Order creation form — add the required `destinationStoreId` store picker (item 1 above).
- **The web does not need to build the scan/count UI itself** — that's the mobile team's screen
  (separate Codex-based agent), the web side is read-only display + the Deliver-button removal +
  the store picker on create.

## Mobile note (informational — not your scope, but shares this contract)

A separate Codex-based agent builds the mobile receiving screens (list → detail → scan/count/
expiry → finalize) against exactly the endpoints and DTOs documented above. The orchestrator will
write a dedicated mobile-facing handoff once this stage and your web stage have both landed, using
this document as its primary source. Nothing in your web work blocks or is blocked by that.

## Known gap, not yours to fix

No supplier-facing endpoint reads `MarketplaceOrderReceiptDto` yet (RLS already allows it via the
`supplier_read` policy, ADR-033 Decision 3, but no controller action exists). If a future task
wants the supplier cabinet to show received data too, that's a small additive endpoint on
`SupplierCabinetCooperationController`, out of scope for this stage.
