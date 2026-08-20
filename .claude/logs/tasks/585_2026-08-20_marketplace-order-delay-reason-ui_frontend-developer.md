# TASK-585 (part 3/3) — Marketplace order delay reason UI

**Agent:** frontend-developer · **Status:** done · **Date:** 2026-08-20

## What changed

- `frontend/features/marketplace/types.ts` — `MarketplaceOrderDto` gained `delayReason:
  string | null` (after `deliveredAt`).
- `frontend/features/supplier-cabinet/types.ts` — new `SetOrderDelayReasonRequest { reason:
  string }`.
- `frontend/features/supplier-cabinet/api/supplier-cabinet-api.ts` — `setOrderDelayReason(id,
  body)` → `POST .../orders/{id}/delay-reason`.
- `frontend/features/supplier-cabinet/hooks/useCabinetCooperation.ts` — new
  `useSetOrderDelayReason()` mutation, invalidates `CABINET_COOP_KEYS.orders` on success (same
  shape as `useUpdateCabinetOrderStatus`).
- `frontend/features/supplier-cabinet/components/CabinetOrdersTab.tsx`:
  - **ETA parity (item 1):** new `ShippingEtaHint` sub-component, mirrors the client-facing one
    exactly, rendered under `OrderStatusBadge` in `OrderRow`'s status `<td>`.
  - **Delay reason (item 2):** `case "shipped"` in `actionsFor` now also renders a "Record
    delay reason" ghost button, shown only when `getShippingEta(...)?.isOverdue` is true.
    Opens the existing `ReasonModal` (reused directly, `variant="primary"`, `required`) via new
    `delayReasonTarget` state, wired the same way as the existing cancel/ship modals. On
    confirm calls `useSetOrderDelayReason().mutate(...)` with toast success/error.
  - `ShippingDetail` gained a `delayReason` prop; renders it (reddish `#F87171`, same style as
    `cancelReason`) when present.
- `frontend/app/(dashboard)/marketplace/orders/page.tsx` — `ShippingDetail` gained the same
  `delayReason` prop/render, read-only (no button — client can't set it).
- `frontend/messages/{en,uk}.json` — added to `Dashboard.supplierCabinet.ordersTab`:
  `etaInTransit`, `etaOverdue`, `delayReasonButton`, `delayReasonModalTitle`,
  `delayReasonModalLabel`, `delayReasonModalConfirm`, `toastDelayReasonSaved`,
  `delayReasonLabel`; added `delayReasonLabel` to `Dashboard.marketplace.ordersPage.ordersTab`.

No backend files touched (schema/DTO/service/API from prior sessions in this same TASK-585,
see `.claude/logs/handoffs/585-to-frontend_backend-developer.md`).

## Verification

- `npx tsc --noEmit` — clean.
- `npm run lint` — clean, no warnings.
- Full manual browser pass (dev servers on :3001/:5000, local Postgres via
  `crmproductsystems-postgres-1` container):
  - DB had 0 `marketplace_orders`/`marketplace_order_items` and 1 unrelated `supplier_agreements`
    row. Inserted a temp `active` agreement + `shipped` order (backdated: shipped 10 days ago,
    3-day estimate → overdue) + 1 item, tied to seed tenants `alpha@supplier.local`
    (supplier) / `ea@demo.local` (client), via direct SQL (`docker exec ... psql`).
  - Supplier cabinet (`/supplier/orders`, logged in as `alpha@supplier.local`): row shows
    "Estimated delivery date has passed" under the status badge; "Record delay reason" button
    appears next to "Delivered" (only because overdue); opens `ReasonModal` titled "Delay
    reason for order MP-TEST-585"; Save button disabled with empty textarea, enabled after
    typing; submit → `POST /api/supplier-cabinet/orders/{id}/delay-reason` → 200; expanded row
    then shows "Delay reason: Carrier delayed due to weather conditions" in red.
  - Client page (`/marketplace/orders`, logged in as `ea@demo.local`): same order's expanded
    row shows the identical "Delay reason: ..." line, read-only, no action button.
  - Cleaned up all 3 fixture rows after; DB counts confirmed back to baseline (1 agreement, 0
    orders, 0 items).

### Tooling note

`computer left_click` on the login submit button was a no-op again (same intermittent issue
TASK-584 hit) — dispatching a native `MouseEvent('click')` via `javascript_tool` on the same
element worked reliably throughout, used for every button click in this session.
`localStorage.clear()` alone did not fully log out (an httpOnly refresh cookie silently
restored the session) — used the real `POST /api/auth/logout` endpoint via `fetch` instead
before switching accounts.

## Issues found

None in the implementation.
