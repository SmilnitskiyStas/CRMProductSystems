# TASK-584 (part 3/3) — Marketplace order shipping UI

**Agent:** frontend-developer · **Status:** done · **Date:** 2026-08-20

## What changed

- `frontend/features/marketplace/types.ts` — `MarketplaceOrderDto` gained `shippedAt`,
  `estimatedDeliveryDays`, `deliveredAt` (all nullable).
- `frontend/features/supplier-cabinet/types.ts` — `UpdateMarketplaceOrderStatusRequest`
  gained `estimatedDeliveryDays?: number`.
- `frontend/features/marketplace/utils.ts` — new `getShippingEta(shippedAt,
  estimatedDeliveryDays, now?)` helper: derives `daysElapsed` (clamped ≥ 0, clock-skew
  guard), `estimatedDeliveryDate`, `isOverdue`. Shared by both pages below so the date math
  lives in one place.
- `frontend/features/supplier-cabinet/components/EstimateDeliveryModal.tsx` (new) — number
  modal mirroring `ReasonModal.tsx`'s wiring; confirm disabled until a positive integer is
  entered (client-side validation, doesn't rely solely on the backend 400).
- `frontend/features/supplier-cabinet/components/CabinetOrdersTab.tsx` — "Ship" button now
  opens `EstimateDeliveryModal` instead of calling `transition(order, "shipped")` directly;
  submits `{status:"shipped", estimatedDeliveryDays}`. Expanded row detail gained a
  `ShippingDetail` sub-component showing shipped date + estimated delivery date, swapped for
  the actual delivered date once `deliveredAt` is set.
- `frontend/app/(dashboard)/marketplace/orders/page.tsx` — compact `ShippingEtaHint` under
  `OrderStatusBadge` in the table row itself (visible without expanding — the part that
  directly answers the original complaint): "In transit: N of M days" or, once past the
  estimate, "Estimated delivery date has passed" (no NaN/negative-day states). Expanded row
  detail gained the same `ShippingDetail` pattern as the supplier cabinet.
- `frontend/messages/{en,uk}.json` — added matching keys to both `Dashboard.marketplace
  .ordersPage.ordersTab` (etaInTransit, etaOverdue, shippedAtLabel, estimatedDeliveryLabel,
  deliveredAtLabel) and `Dashboard.supplierCabinet.ordersTab` (shipModalTitle/Label/
  Placeholder/Confirm/Pending, shippedAtLabel, estimatedDeliveryLabel, deliveredAtLabel).

## Deviation from the brief (noted per CLAUDE.md's judgment-call rule)

The brief said supplier-cabinet uses "raw Ukrainian strings not `useTranslations`" (citing
`CooperationBadges.tsx:3-14`, "Block 7, not yet translated") and told me to keep new
supplier-cabinet strings as plain Ukrainian to match. Direct inspection of
`CabinetOrdersTab.tsx` shows it already fully uses `useTranslations("Dashboard
.supplierCabinet.ordersTab")` for every existing string, and that namespace already exists,
fully translated, in both `en.json`/`uk.json`. The "Block 7, not yet translated" comment
refers to the exported `*_STATUS_LABELS` maps other supplier-cabinet components import
directly (e.g. `CabinetSupportTab.tsx`'s filter `<option>` list), not this file. I followed
the file's actual, observed convention (`useTranslations` + JSON keys in both locales)
instead of the brief's stale assumption — this keeps the new strings consistent with every
other string already in the file.

## Verification

- `tsc --noEmit` — clean (one error fixed: `estimatedDeliveryDays` needed a `!= null` guard
  before passing to `t()`, since ICU values can't be `null`).
- `npm run lint` — clean, no warnings.
- Manual browser verification (dev servers on :3001/:5000) — full end-to-end pass:
  - Logged in as `alpha@supplier.local` (existing seed-password fixture per TASK-308's
    convention) and `ea@demo.local`. Local dev DB had zero `marketplace_orders` /
    `supplier_agreements` rows, so inserted two temporary test-fixture orders directly via
    SQL (one `confirmed`, one pre-backdated `shipped` for the overdue path) tied to those two
    existing tenants, then deleted them after — dev DB confirmed back to 0 rows in both
    tables.
  - Supplier "Ship" flow: modal opens on click, confirm button disabled at empty/`0` input,
    enabled at a valid positive integer, submits `POST .../status` → `200`, row status
    updates to Shipped, expanded detail shows shipped date + estimated delivery date.
  - Client orders page: row shows "Shipped" badge with "In transit: 0 of 4 days" directly in
    the table row (no expand needed); expanded detail matches the supplier's view exactly.
  - Overdue path: backdated fixture (shipped 10 days ago, 3-day estimate) correctly renders
    "Estimated delivery date has passed" — no NaN, no negative numbers.
  - Delivered transition: both supplier and client detail views correctly swap the estimated
    delivery line for "Delivered: <date>" once `deliveredAt` is set.

### Tooling note

The Browser pane's `computer` `left_click` on plain `<button>` elements was unreliable in
this session (pane reported "not displayed" for screenshots) — several clicks (login submit,
Ship button, modal confirm) dispatched with no effect (no network request fired). Verified
each case wasn't a code bug by dispatching a native `MouseEvent('click')` via
`javascript_tool` on the same element, which worked every time and produced the expected
network calls/UI updates. `form_input` (used for all text/number field entry) worked
reliably throughout and correctly drove React state (confirmed via the modal's disabled-state
toggling before/after each fill).

## Issues found

None in the implementation. No backend files touched.
