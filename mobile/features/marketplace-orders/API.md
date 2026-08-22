# Marketplace Order Receiving — API Reference

> **Status: ALREADY IMPLEMENTED AND DEPLOYED.** Backend (TASK-586, ADR-033) and mobile are both
> live in production as of this file's creation date (2026-08-22, commit `aeb830fc`, CI/CD green).
> This is a **reference document** for extending, fixing, or building adjacent to this feature —
> it is not a build spec. Every route, DTO field, and policy below was read directly from the
> current source and cross-checked against it while writing this file.
>
> **Addendum (2026-08-22, TASK-596..599, commits `96a4fefe` / `40632ba8`):** two more backend
> changes landed on top of the above and are folded into this file below — client-catalog
> auto-provisioning at order time (affects the "Known v1 limitations" barcode-crosswalk note),
> and two new `MarketplaceOrderReceiptItemDto` fields, `price` and `referenceImageUrl`, plus a
> finalize-time discrepancy auto-ticket. The new fields exist on the wire today but are **not yet
> rendered by the mobile screen** — see the dedicated "Not yet built" section below. Re-verified
> directly against current backend source, same discipline as the rest of this file.

## What this feature does

A client-tenant employee receives a shipped B2B marketplace order via the mobile app: they
scan/confirm each item's product identity against the client's own catalog, then enter received
quantity, expiry date, and an optional batch number, per item. Finalizing is now the **only**
remaining path for an order to reach `delivered` — the supplier's old one-click self-service
"Deliver" button was removed as part of this same change (ADR-033 Decision 4).

## Where the code lives

**Mobile:**
- `mobile/app/(app)/marketplace-orders/index.tsx` — list screen (orders awaiting receipt)
- `mobile/app/(app)/marketplace-orders/[orderId].tsx` — receiving/detail screen: scan, manual
  search fallback, quantity/expiry/batch entry, finalize
- `mobile/features/marketplace-orders/types.ts` — TS types
- `mobile/features/marketplace-orders/api/marketplaceOrdersApi.ts` — API client functions
- `mobile/features/marketplace-orders/hooks/useMarketplaceOrders.ts` — React Query hooks

**Backend:**
- `backend/ShelfGuard.Api/Controllers/MarketplaceCooperationController.cs` — region
  `// ── Marketplace order receiving (TASK-586, ADR-033) ──` (around line 183)
- `backend/ShelfGuard.Application/Features/Marketplace/MarketplaceOrderReceiptService.cs` —
  business logic, error-message constants
- `backend/ShelfGuard.Application/Features/Marketplace/Dtos/CooperationDtos.cs` — DTOs
  (`MarketplaceOrderDto`, `MarketplaceOrderReceiptDto`, `MarketplaceOrderReceiptItemDto`,
  `UpdateMarketplaceOrderReceiptItemRequest`)

## API contract

All routes under `/api/marketplace/orders/...`, JWT bearer auth (same as every other mobile
call). Controller-level gate on the whole class: `[Authorize]` + `[RequireModule("marketplace")]`.
The two read endpoints (a, c) need nothing beyond that. The three mutating endpoints (b, d, e)
additionally require `[Authorize(Policy = AppPolicies.CanReceiveStock)]` — the same role floor
(storekeeper and above) the existing non-marketplace Receipts feature already requires.

Route addressing is order-centric throughout — `orderId`, never a separately surfaced `receiptId`
— since a receipt is 1:1 with its order.

### a. `GET /api/marketplace/orders/awaiting-receipt`

Shipped orders of the caller's tenant not yet received.
- `200 MarketplaceOrderDto[]` — always 200, possibly `[]`, no error cases.

### b. `POST /api/marketplace/orders/{orderId}/receipt`

Idempotent create-or-get: starts a receiving session for a shipped order, or returns the one
already in progress.
- `200 MarketplaceOrderReceiptDto`
- `404 {"error": "Замовлення не знайдено."}` — wrong tenant or order doesn't exist
- `400 {"error": "Прийом можливий лише для відправлених замовлень."}` — order isn't `shipped`
- `400 {"error": "У замовлення не вказано магазин-призначення. Зверніться до підтримки."}` — a
  historical order shipped before `DestinationStoreId` tracking existed; dead end, no retry helps

### c. `GET /api/marketplace/orders/{orderId}/receipt`

Read-only fetch, no side effects (re-enter the screen without re-triggering create).
- `200 MarketplaceOrderReceiptDto`
- `404` — no receipt started yet, or order not found for this tenant

### d. `PUT /api/marketplace/orders/{orderId}/receipt/items/{itemId}`

One item per call (not bulk) — the scan-one-commit-one interaction. `itemId` is
`MarketplaceOrderReceiptItemDto.Id` (from the `items[]` array), not the order line's own id.

Request body — `UpdateMarketplaceOrderReceiptItemRequest`, all fields nullable/optional:
```json
{
  "productId": "guid|null",
  "quantityReceived": 4.5,
  "expiryDate": "2026-12-31",
  "batchNumber": "string|null",
  "discrepancyNotes": "string|null"
}
```

**Field semantics (verbatim from the service's XML doc comment, easy to get wrong):**
- `quantityReceived` / `discrepancyNotes` — **overwrite** directly. Omitting (or sending `null`)
  clears them. Resend the current value every call if you want to keep it.
- `productId` / `expiryDate` / `batchNumber` — **merge** with the existing value. Omitting keeps
  whatever was already set; there is no way to explicitly null these back out once set.

> **What happens to `discrepancyNotes`?** As of TASK-599 (2026-08-22): when a receipt is
> finalized (endpoint e below) and one or more items have non-empty `discrepancyNotes`, the
> backend automatically opens a support ticket to the supplier tenant with those details — this
> happens entirely server-side, triggered by the same `finalize` call mobile already makes.
> Nothing new for mobile to call or handle; just context so this field doesn't read as purely
> decorative.

> **Does the mobile client need to worry about this in practice? No.** `saveItem()` in
> `[orderId].tsx` (line ~151) always builds and sends all five fields together in a single object
> on every save — `productId`, `quantityReceived`, `expiryDate`, `batchNumber` (`?? null`),
> `discrepancyNotes` (`?? null`) — there is no partial-field save path in the current UI. The
> merge-vs-overwrite distinction only matters if a future caller (or a new UI path) starts sending
> partial payloads; as shipped, every PUT is a full snapshot.

- `200 MarketplaceOrderReceiptDto` (the whole receipt — the mobile screen re-renders its full item
  list's progress from this response after every call, it does not patch local state manually)
- `404` — wrong tenant/no receipt, or bad `itemId`
- `400 {"error": "Документ прийому вже підтверджено."}` — already finalized
- `400 {"error": "Отримана кількість не може бути від'ємною."}` — negative quantity
- `400 {"error": "Товар не знайдено у вашому каталозі."}` — `productId` doesn't resolve in the
  caller's tenant

### e. `POST /api/marketplace/orders/{orderId}/receipt/finalize`

Gate: every item needs `ProductId` + `QuantityReceived` + `ExpiryDate` all set (mirrored by each
item's `isResolved` flag, so callers don't need to recompute the gate condition themselves).
- `200 MarketplaceOrderReceiptDto` with `status: "received"` — the order is now `delivered`, real
  stock (`ProductStock`/`StockMovement`) has been created; invalidate/refetch the order list
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
  discrepancyNotes: string | null; // non-empty on finalize -> auto-opens a supplier support
                                   // ticket server-side (TASK-599, see endpoint d's field notes)
  isResolved: boolean;            // productId && quantityReceived && expiryDate all set —
                                   // exact finalize-gate condition, precomputed for you
  price: number;                  // frozen purchase price from the order line — always present,
                                   // unrelated to scan/receive progress (TASK-599)
  referenceImageUrl: string | null; // reference photo so the employee can visually confirm the
                                   // item before/while scanning (TASK-599). Once productId
                                   // resolves: the client's own catalog Item.imageUrl (may be
                                   // null if that item has no photo — no fallback once scanned).
                                   // Before that: falls back to the order line's linked
                                   // SupplierItem's primary image. Null when neither exists.
};
```

`MarketplaceOrderDto` (endpoint a's list item) carries the full order shape — order number,
supplier/client names, status, amounts, `items: MarketplaceOrderItemDto[]`, and the nullable
`destinationStoreId` (read-only from the mobile side, per ADR-033 Decision 2; `null` on orders
placed before this column existed — those can never be received through this flow, see endpoint
b's second 400 case). The mobile `types.ts` `MarketplaceOrder` interface only carries the subset
the list/detail screens actually use — check `CooperationDtos.cs` directly for the full field set
if extending list-screen UI.

The mobile-side interface for the PUT body (`UpdateReceiptItemRequest` in `types.ts`) declares
`productId`, `quantityReceived`, `expiryDate` as **required** (not optional) — tighter than the
backend's fully-nullable `UpdateMarketplaceOrderReceiptItemRequest` — because it models what the
mobile UI actually always sends, not the full backend contract. Keep that in mind if reusing the
mobile type elsewhere: it is not a faithful mirror of the backend request shape, it's the shape of
one specific call site.

## Auth

- Reads (`GET awaiting-receipt`, `GET .../receipt`) need only the controller's class-level
  `[Authorize]` + `[RequireModule("marketplace")]`.
- Mutations (`POST .../receipt`, `PUT .../receipt/items/{itemId}`, `POST .../receipt/finalize`)
  additionally require `[Authorize(Policy = AppPolicies.CanReceiveStock)]` — storekeeper rank and
  above, the same floor the existing (separate, non-marketplace) Receipts feature uses.

## Known v1 limitations

- **One receipt per order, no reopening after finalize.** `MarketplaceOrderReceipt.MarketplaceOrderId`
  has a unique index enforcing this at the DB level.
- **No barcode crosswalk between supplier and client catalogs** — still true, but hit
  meaningfully less often since TASK-596..598 (2026-08-22). Scanning only ever resolves within
  the client tenant's own `Item` catalog (via the unchanged, pre-existing
  `GET /api/items/by-barcode/:barcode`) — there is still no direct supplier-SKU → client-SKU
  lookup endpoint. What changed: placing a marketplace order now auto-provisions (or links to an
  existing) `Item` in the client's own catalog from the supplier's listing data at order time,
  unless a barcode collision is detected (resolved through a new web-only checkout flow — not
  mobile's concern). So by the time a shipment physically arrives for receiving, the barcode
  usually already resolves. The manual-search fallback below is still necessary for: orders
  placed before this shipped, barcode collisions the client chose to resolve as "create new item
  anyway" with a still-unrelated barcode, and other edge cases — it has not been removed, and an
  unresolved item still blocks finalize exactly as before.
- **No supplier-facing view of receipt data exists yet.** RLS already permits the supplier tenant
  read access at the DB level (ADR-033 Decision 3), but no endpoint has been built to expose it.

## Confirmed-implemented details (verified against the shipped mobile code)

- **Manual catalog-search fallback is built.** The original pre-implementation handoff flagged
  this as "no picker exists, build a minimal one" — it exists now. In `[orderId].tsx`, a `search`
  boolean UI mode inside the same full-screen `Modal` used for scanning shows a text input wired
  to `searchCatalogProducts(search)` → `GET /api/items?search=` (via
  `marketplaceOrdersApi.ts`), rendering a tappable result list; picking a result sets the resolved
  `product` state the same way a successful scan would. It's reachable three ways: tapping "Знайти
  вручну" from the camera-permission-denied screen, tapping "Знайти вручну" from the live camera
  overlay, or tapping "Пошук" on the alert shown when a scanned barcode fails to resolve
  (`handleScan`'s catch branch).
- **Date picker dependency:** `@react-native-community/datetimepicker` version `9.1.0`
  (`mobile/package.json`). Used in "Термін придатності" as a native date picker triggered from a
  pressable field; `minimumDate={new Date()}` enforces expiry dates can't be entered in the past
  (client-side UX only — the backend does not itself reject past/implausible dates).
- **Barcode types scanned:** `['ean8', 'ean13', 'qr', 'code128']` — same set `mobile/app/(app)/scan.tsx`
  uses, passed via `CameraView`'s `barcodeScannerSettings` prop in `[orderId].tsx`.

## Not yet built: price + reference-photo display in the receiving UI

> **This section describes intended UX, not shipped behavior.** Unlike the rest of this file
> (which documents already-shipped, verified-against-source behavior), nothing below has been
> built in the mobile screen as of this update — treat it as a build note for whoever picks this
> up next, not documentation of existing code.

The two new DTO fields documented above (`price`, `referenceImageUrl`, TASK-599) are already
present on the wire — `useMarketplaceReceipt`'s response carries them today — but
`mobile/app/(app)/marketplace-orders/[orderId].tsx` doesn't read or render either one yet, and
`mobile/features/marketplace-orders/types.ts`'s `MarketplaceOrderReceiptItem` interface hasn't
been extended to include them either (both would need updating alongside any UI work here).

Intended UX:
- **Item list row (`ItemRow` component, `[orderId].tsx` ~line 48).** Show `item.price` alongside
  the "Замовлено: …" line, and a small thumbnail from `item.referenceImageUrl` near the row's
  status icon — gives the employee a visual before they even tap into an item.
- **Detail/scan view (the non-scanning, non-search branch of the item `Modal`, ~lines 227-240).**
  Show the same photo larger, plus price, ideally above the quantity/expiry/batch form fields.
  Critically, this should be visible **before** a successful scan, not only after — that's the
  whole point of `referenceImageUrl`'s pre-resolution fallback to the supplier listing's photo:
  give the employee something to visually match against while still physically locating/
  confirming the item, not just as post-scan confirmation.
- **No-image state.** Follow the placeholder pattern already established elsewhere in this app
  for a missing product photo rather than inventing a new one — see
  `mobile/app/(personal)/catalog.tsx` (~lines 199-204) and
  `mobile/app/(personal)/product/[id].tsx` (~lines 105-108): a rounded `bg-gray-100` container
  showing `<Image source={{ uri }} resizeMode="contain" className="h-full w-full" />` when a URL
  is present, else an `<Ionicons name="cube-outline" />` glyph.

## Deeper source material (not duplicated here)

- `.claude/docs/decisions.md`, **ADR-033** — full design rationale: why a separate
  `MarketplaceOrderReceipt`/`Item` entity pair instead of reusing `StockReceipt`, the split
  client-write/supplier-read RLS policy design, the historical-data caveat about pre-existing
  shipped orders with no `DestinationStoreId`, and all other Decision-numbered rationale referenced
  above.
- `.claude/logs/handoffs/586-to-mobile-codex.md` — the original pre-implementation build spec
  given to the Codex agent that built these mobile screens. Superseded by this file as the
  contract reference, but still useful for **why** certain choices were made (e.g. why per-item
  PUT instead of a bulk payload, why no shared `useBarcodeScanner` hook exists in this codebase,
  why the existing non-marketplace Receipts screen was a poor template beyond basic screen shape).
