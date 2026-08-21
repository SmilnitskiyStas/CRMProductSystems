# Handoff: TASK-586 → mobile (Codex agent)

**From:** Claude session (backend + web stages, all 4 done: project-architect ADR-033,
database-engineer schema, backend-developer service/API, frontend-developer web UI).
**Spec of record:** ADR-033 in `.claude/docs/decisions.md` (read it — it's the rationale for
every field/RLS/contract choice below). This document is the mobile-facing extract; it does not
supersede the ADR if the two ever disagree.
**Full API contract source:** `.claude/logs/handoffs/586-to-frontend_backend-developer.md` — every
route, DTO field, and error message below is copied verbatim from there. That document also
covers the web-side changes (not your concern) if you want the fuller picture.

This document is written to stand alone. You do not need to read the conversation that produced
it — everything you need is here or in the two files linked above.

## What this feature is

A client tenant places a B2B marketplace order against a supplier tenant on the platform. Today,
once the supplier ships it, the supplier alone clicks "Delivered" with zero verification of what
actually arrived. **That self-service path is now removed** (backend change already merged — a
supplier's status-update call with `status:"delivered"` always 400s). The only way an order
reaches `delivered` from now on is through **this new mobile flow**: an employee at the receiving
store scans each physical item, confirms it against the order, enters the received quantity and
the batch's expiry date, and finalizes. That finalize call is what flips the order to `delivered`
and creates the actual stock (`ProductStock` batches, FEFO-tracked).

## Existing mobile patterns to build on (read these first)

This app already has a near-identical flow for **non-marketplace** deliveries — study it, it's
your closest template even though it's simpler than what you're building:

- `mobile/app/(app)/receipt/index.tsx` — list screen (`useReceipts()`, cards, tap → detail).
- `mobile/app/(app)/receipt/[id].tsx` — detail/processing screen: progress header ("Оброблено:
  X / Y"), `FlatList` of item rows, tap-to-process, "Підтвердити прийомку" button gated on every
  item being processed.
- `mobile/features/receipt/` — `types.ts`, `api/receiptApi.ts`, `hooks/useReceipts.ts`. Mirror
  this folder structure for the new feature: create `mobile/features/marketplace-orders/` with the
  same three-part shape.
- **Important limitation of the existing Receipts mobile screen, do not copy it as-is:** it never
  actually scans a barcode, and never lets the user enter quantity/batch/expiry — it's a "one-tap,
  accept full ordered quantity" shortcut, even though the backend DTOs support the richer fields.
  Your new marketplace-receiving screen needs the REAL flow (scan → resolve → quantity → expiry
  → optional batch/discrepancy), which this existing screen does not demonstrate end-to-end. Use
  it only for the list/detail *screen structure* and React Query wiring convention, not for the
  per-item interaction.

## Barcode scanning

- `mobile/app/(app)/scan.tsx` is the closest scanning reference: `expo-camera`'s `CameraView` +
  `useCameraPermissions`, barcode types `['ean8', 'ean13', 'qr', 'code128']`. Note the required
  `cssInterop(CameraView, { className: 'style' })` call near the top of that file — without it the
  camera view renders with zero height under NativeWind. Copy this quirk.
- After a scan, resolve the barcode via the **existing, unchanged** endpoint:
  `getProductByBarcode(barcode)` in `mobile/features/stock/api/stockApi.ts` →
  `GET /api/items/by-barcode/:barcode`. This is tenant-scoped to whichever tenant the logged-in
  employee belongs to (the client tenant receiving the order) — it does **not** know anything
  about the marketplace order or the supplier's catalog. If the barcode doesn't resolve (product
  not in this tenant's own catalog), that item cannot be received through this flow in v1 — surface
  a clear "товар не знайдено у вашому каталозі" message and let the employee either skip that item
  or search-and-pick from the catalog manually (this codebase doesn't have a manual product-search
  picker component reusable off the shelf — check `mobile/app/(app)/stock/` or the receipt
  screens for anything close, otherwise build a minimal text-search list as a fallback; this is
  the accepted v1 limitation per the approved plan — no supplier↔client catalog crosswalk exists,
  see ADR-033).
- **There is no shared `useBarcodeScanner` hook.** Every scanning screen in this app
  (`scan.tsx`, `pos/scanner.tsx`, `write-offs/create.tsx`, `transfers/create.tsx`) reimplements the
  camera/permission/overlay boilerplate itself. The one partial exception,
  `mobile/features/pos/components/BarcodeCameraView.tsx` (TASK-407), is narrowly built for one POS
  sub-screen and not a general-purpose export yet. You may either duplicate the boilerplate again
  (consistent with existing project convention) or extract a shared hook now that you're adding a
  5th scanning surface — your call, not gated by anything upstream.

## Date picker — new dependency needed

**No date-picker library exists in `mobile/package.json` today**, and no expiry-date entry UI
exists anywhere in the app (every existing `expiryDate` display is read-only). You'll need to add
one — `@react-native-community/datetimepicker` is Expo SDK 56-compatible and is the natural
choice, but verify current compatibility before installing. There is no existing validation
convention to match (e.g. "must be a future date") — decide sensible validation yourself (the
backend does not reject past/implausible dates itself, so any sanity-checking is a client-side UX
nicety, not a contract requirement).

## Screen flow to build

Mirror the Receipts screen shape (`mobile/app/(app)/receipt/`) at a new route
`mobile/app/(app)/marketplace-orders/`:

1. **List screen** (`index.tsx`) — calls `GET /api/marketplace/orders/awaiting-receipt` (see
   contract below), shows shipped orders belonging to the employee's tenant that still need
   receiving. Tap → detail.
2. **Detail/receiving screen** (`[orderId].tsx` or similar) — on mount, call
   `POST /api/marketplace/orders/{orderId}/receipt` (idempotent create-or-resume — safe to call
   every time this screen opens). Render the receipt's `items[]` (`MarketplaceOrderReceiptItemDto`
   — see shape below), each showing `itemNameSnapshot` and `quantityOrdered` from the order, plus
   whatever's already been resolved (`isResolved: true` items show their captured
   qty/batch/expiry, still editable).
3. **Per-item scan/count sub-flow** — tap an unresolved item → scan barcode → resolve via
   `GET /api/items/by-barcode/:barcode` → show the resolved product name next to the order's
   expected `itemNameSnapshot` for the employee to visually confirm it's a sensible match (**no
   automated identity verification exists** — the app cannot programmatically prove the scanned
   item is "the same" as the ordered line beyond what the employee's own judgment does; this is a
   deliberate v1 limitation, not a bug) → enter quantity received (default-prefill with
   `quantityOrdered`, editable) → enter expiry date (new date picker) → optional batch number /
   discrepancy note (free text, discrepancy is informational only, never blocks) → call
   `PUT /api/marketplace/orders/{orderId}/receipt/items/{itemId}` with the resolved fields.
4. **Finalize** — a "Підтвердити прийомку" button, enabled only once every item's `isResolved` is
   `true` (client-side gate mirroring the server's own gate — check this locally so the button
   disables correctly, but the server enforces it too and will 400 if bypassed). Calls
   `POST /api/marketplace/orders/{orderId}/receipt/finalize`. On success, the order is `delivered`
   and real stock now exists — navigate back to the list (which should no longer show this order,
   since it's no longer `shipped`).

**v1 is explicitly one receiving session per order** — no partial/multi-delivery-per-order model,
no way to reopen a finalized receipt. Don't build for that.

## API contract (all under `/api/marketplace/...`, same JWT bearer auth as every other endpoint
this app already calls — nothing new on the auth side, log in as the client tenant's employee same
as for Receipts/Stock/POS)

Mutations (b, d, e below) require the `CanReceiveStock` policy — same role floor
(`storekeeper`+) the existing Receipts feature already requires, so if an employee's account can
already use the Receipts screen, it can use this one too.

### a. `GET /api/marketplace/orders/awaiting-receipt`
→ `200 MarketplaceOrderDto[]` (shipped orders of the caller's tenant not yet received). Always
`200`, possibly `[]`, no error cases.

### b. `POST /api/marketplace/orders/{orderId}/receipt`
Idempotent create-or-resume.
- `200 MarketplaceOrderReceiptDto`
- `404 {"error": "Замовлення не знайдено."}` — wrong tenant or doesn't exist
- `400 {"error": "Прийом можливий лише для відправлених замовлень."}` — not `shipped` (already
  delivered, or not shipped yet)
- `400 {"error": "У замовлення не вказано магазин-призначення. Зверніться до підтримки."}` — a
  historical order shipped before destination-store tracking existed; dead end, show a support
  message, don't retry

### c. `GET /api/marketplace/orders/{orderId}/receipt`
Read-only fetch, no side effects (useful for re-entering a screen without re-triggering create).
- `200 MarketplaceOrderReceiptDto`
- `404` — no receipt started yet, or order not found for this tenant

### d. `PUT /api/marketplace/orders/{orderId}/receipt/items/{itemId}`
One item per call (not bulk) — this is your scan-one-commit-one interaction. `itemId` is
`MarketplaceOrderReceiptItemDto.id` (from the `items[]` array), not the order line's own id.

Request — all fields optional, send only what changed:
```json
{
  "productId": "guid|null",
  "quantityReceived": 4.5,
  "expiryDate": "2026-12-31",
  "batchNumber": "string|null",
  "discrepancyNotes": "string|null"
}
```
**Field semantics, easy to get wrong:**
- `quantityReceived` / `discrepancyNotes` — overwrite directly. Omitting (or sending `null`)
  clears them. Resend the current value every call if you want to keep it.
- `productId` / `expiryDate` / `batchNumber` — merge. Omitting keeps whatever was already set;
  there's no way to explicitly clear these back to null once set.

- `200 MarketplaceOrderReceiptDto` (the whole receipt — re-render your full item list's progress
  from this response after every call, don't try to patch local state manually)
- `404` — wrong tenant/no receipt, or bad `itemId`
- `400 {"error": "Документ прийому вже підтверджено."}` — already finalized
- `400 {"error": "Отримана кількість не може бути від'ємною."}` — negative quantity
- `400 {"error": "Товар не знайдено у вашому каталозі."}` — `productId` doesn't resolve in the
  caller's tenant (shouldn't happen if you got it from the barcode-lookup endpoint, but a stale/
  cached id could trigger this — handle gracefully, don't crash)

### e. `POST /api/marketplace/orders/{orderId}/receipt/finalize`
Gate: every item needs `productId` + `quantityReceived` + `expiryDate` all set (use each item's
`isResolved` flag to know this without recomputing it yourself).
- `200 MarketplaceOrderReceiptDto` with `status: "received"` — the order is now `delivered`,
  invalidate/refetch your order list.
- `404` — same shapes as b/c
- `400 {"error": "Документ прийому вже підтверджено."}` — repeat call
- `400 {"error": "Усі позиції мають бути відскановані з кількістю та терміном придатності перед підтвердженням."}`
  — gate failed, at least one item still unresolved

## DTO shapes (camelCase over the wire)

```ts
type MarketplaceOrderReceiptDto = {
  id: string;
  marketplaceOrderId: string;
  clientTenantId: string;
  supplierTenantId: string;
  destinationStoreId: string;
  destinationStoreName: string;
  status: "draft" | "received";
  createdByUserId: string | null;
  receivedByUserId: string | null;
  receivedAt: string | null;     // ISO 8601
  createdAt: string;
  updatedAt: string;
  items: MarketplaceOrderReceiptItemDto[];
};

type MarketplaceOrderReceiptItemDto = {
  id: string;                     // use as {itemId} in endpoint d
  marketplaceOrderItemId: string; // order line this closes, rarely needed client-side
  productId: string | null;
  itemNameSnapshot: string;       // what the employee is expected to be scanning
  productName: string | null;     // resolved name once productId is set
  quantityOrdered: number;
  quantityReceived: number | null;
  expiryDate: string | null;      // "YYYY-MM-DD"
  batchNumber: string | null;
  discrepancyNotes: string | null;
  isResolved: boolean;            // productId && quantityReceived && expiryDate all set —
                                   // exact finalize-gate condition, precomputed for you
};
```

`MarketplaceOrderDto` (used by endpoint a's list) is the same DTO already used everywhere else in
the marketplace feature — if mobile ever needs its fuller shape (order number, supplier name,
items, totals), check `frontend/features/marketplace/types.ts` for the canonical TS shape (web and
mobile consume the same backend DTO).

## Known limitations, don't try to build around them

- No barcode crosswalk between the supplier's catalog and the client's own catalog — scanning only
  ever resolves within the client tenant's own `Item` catalog. If the client hasn't catalogued a
  product they're ordering, it can't be scan-received; this is accepted v1 scope (ADR-033).
- No supplier-facing view of receipt data exists yet (RLS already permits it at the DB level, but
  no endpoint is built) — irrelevant to you, mobile is client-tenant-only for this feature.
- One receipt per order, one finalize, no reopening.
