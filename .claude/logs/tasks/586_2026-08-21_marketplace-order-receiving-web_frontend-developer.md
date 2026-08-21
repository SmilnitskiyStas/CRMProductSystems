# TASK-586 (stage 4/4) — Marketplace order receiving: web layer

**Agent:** frontend-developer · **Status:** done
**Depends on:** TASK-586 stage 3 (backend-developer) — API/DTOs already live
**Spec:** ADR-033 (`.claude/docs/decisions.md`), handoff `.claude/logs/handoffs/586-to-frontend_backend-developer.md`

## What changed

- `frontend/features/marketplace/types.ts` — `MarketplaceOrderDto` gained `destinationStoreId:
  string | null`; `CreateMarketplaceOrderRequest` gained required `destinationStoreId: string`;
  new `MarketplaceOrderReceiptDto`/`MarketplaceOrderReceiptItemDto` (web only needs the read-only
  shape — mutation endpoints b/d/e from the handoff are mobile-only for this stage).
- `frontend/features/marketplace/api/marketplace-api.ts` — new `getOrderReceipt(orderId)` →
  `GET /api/marketplace/orders/{orderId}/receipt`.
- `frontend/features/marketplace/hooks/useCooperation.ts` — new `useMarketplaceOrderReceipt(orderId,
  enabled)`, `retry: false` (404 = "nothing to show yet", not an error to retry).
- `frontend/features/marketplace/components/SupplierOrderCart.tsx` — required destination-store
  `<select>` (via `useStores()`, same pattern as `OpenShiftDialog.tsx`; deliberately **not**
  `usePrimaryStoreId()` per ADR-033 Decision 2 — an order is a chosen future-delivery destination,
  not inferred from ambient store context). Submit disabled until a store is chosen; hint text
  shown while empty. `storeId` reset on successful submit alongside the existing `comment` reset.
- `frontend/features/supplier-cabinet/components/CabinetOrdersTab.tsx` — removed the dead
  "Deliver" button in `case "shipped"` (backend-confirmed the transition always 400s now);
  replaced with a muted status hint. `transition()`'s type narrowed to `"confirmed"` only (its
  only remaining call site). Delay-reason button/flow untouched.
- `frontend/app/(dashboard)/marketplace/orders/page.tsx` — new `ReceiptDetail`/`ReceiptItemsTable`
  components, rendered in the expanded-row detail when `order.status === "delivered"`: destination
  store, received-at, and a per-item table (name, ordered/received qty, batch, expiry,
  discrepancy). 404/loading handled inline (404 renders nothing).
- `frontend/messages/{en,uk}.json` — added to `Dashboard.marketplace.orderCart`:
  `destinationStoreLabel`, `destinationStorePlaceholder`, `destinationStoreRequiredError`,
  `loadingStores`; to `Dashboard.marketplace.ordersPage.ordersTab`: `receiptTitle`,
  `receiptDestinationStoreLabel`, `receiptReceivedAtLabel`, `receiptHeaderOrdered/Received/Batch/
  Expiry/Discrepancy`; in `Dashboard.supplierCabinet.ordersTab`, `deliverButton` replaced with
  `awaitingClientConfirmationHint`.

No `backend/` or `mobile/` files touched. No scan/barcode/camera UI built (mobile's scope).

## Supplier-cabinet receipt-detail reachability finding (item 4, supplier side)

**Skipped, confirmed unreachable — not built.** Per the handoff's flagged "known gap," checked
whether `GET /api/marketplace/orders/{orderId}/receipt` is callable from a supplier session
before assuming it wasn't:

- **Code review:** `MarketplaceOrderReceiptService.GetAsync` (backend/ShelfGuard.Application/
  Features/Marketplace/MarketplaceOrderReceiptService.cs:108-113) hard-checks
  `order.ClientTenantId != clientTenantId → OrderNotFoundError`, where `clientTenantId` is just
  the caller's own tenant from the JWT — not role-aware. A supplier tenant's id can never equal
  a marketplace order's `ClientTenantId`, so this branch is a deterministic 404 for every
  supplier-tenant call, regardless of the `supplier_read` RLS policy allowing the DB-level read.
- **Live confirmation:** called the endpoint from an authenticated `alpha@supplier.local`
  (supplier_admin) browser session against a real delivered order + receipt fixture. Got `403
  {"error":"Module not activated"}` — the controller's class-level `[RequireModule("marketplace")]`
  blocks this supplier tenant even earlier than the tenant check above (this tenant doesn't have
  the `marketplace` module active on its own account; supplier-cabinet endpoints are gated
  separately from the client-facing `marketplace` module).

Either wall independently blocks it. Left `CabinetOrdersTab.tsx` without the received-detail
block; the client-side page is the sole received-detail surface for this stage. Follow-up
(a small additive supplier-facing endpoint) is out of scope here per the handoff.

## Verification

- `npx tsc --noEmit` — clean.
- `npm run lint` — clean, no warnings.
- Full manual browser pass (dev servers on :3001/:5000, local Postgres via
  `crmproductsystems-postgres-1`):
  - DB had 0 `marketplace_orders`/`marketplace_order_receipts` and 1 unrelated `supplier_agreements`
    row. Inserted a temp `active` agreement between seed tenants `alpha@supplier.local` (supplier)
    / `ea@demo.local` (client) via direct SQL.
  - **Order creation** (`ea@demo.local`): added item to cart, opened checkout — store `<select>`
    required, "Confirm order" `disabled=true` with no store chosen, `disabled=false` after
    picking one, required-error hint visible while empty. Submitted → `POST
    /api/marketplace/suppliers/{id}/orders` → `201 Created`; DB row confirmed
    `DestinationStoreId` matches the picked store.
  - **Supplier cabinet** (`alpha@supplier.local`): confirmed → shipped the order through the
    existing UI flow. `shipped` row's actions column shows "Awaiting client confirmation" text,
    no Deliver button; delay-reason button correctly absent (not overdue).
  - **Receipt reachability** (still as `alpha@supplier.local`): direct `fetch` to
    `GET /api/marketplace/orders/{orderId}/receipt` → `403 Module not activated` (see finding
    above).
  - **Received-detail block** (`ea@demo.local`): set order to `delivered` + inserted a
    `marketplace_order_receipts`/`_items` fixture via SQL (mobile finalize UI doesn't exist yet).
    Expanded row on `/marketplace/orders` rendered "Received" section: "Store: Свіжий Кут
    Центральний · Received: <date>" and the item table (product name resolved via `productId`,
    ordered 10 / received 9, batch `BATCH-586`, expiry `12/31/2026`, discrepancy note) — all
    fields matched the fixture.
  - Cleaned up all fixture rows (receipt items, receipt, order items, order, agreement) after;
    DB counts confirmed back to baseline (0 orders, 0 receipts, 1 pre-existing agreement).

### Tooling note

`computer left_click` was an intermittent no-op on several buttons in this session (same issue
noted in TASK-584/585) — dispatching a native `MouseEvent('click')` via `javascript_tool` worked
reliably throughout. Used the real `POST /api/auth/logout` + `localStorage.clear()` to switch
accounts cleanly (an httpOnly refresh cookie otherwise silently restores the session).

## Issues found

None in the implementation. The supplier-cabinet reachability gap is pre-existing/by-design per
ADR-033 and the handoff, not a bug introduced here.
