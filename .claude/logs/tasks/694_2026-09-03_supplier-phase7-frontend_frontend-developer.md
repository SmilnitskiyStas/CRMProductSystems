# TASK-694 — Supplier portal "Phase 7" frontend (3 changes)

Agent: frontend-developer. HEAD before/after: `eea1df6b`. Status: review — NOT committed.
Frontend only; no backend / mobile / worker.

## 1 — client cancel button now allowed for `confirmed` too

- `frontend/app/(dashboard)/marketplace/orders/page.tsx` — `actions` column render gate
  `order.status === "new"` → `order.status === "new" || order.status === "confirmed"`.
  `ReasonModal` + `useCancelMarketplaceOrder` flow unchanged.
- `toast.error(err.message)` already surfaces the backend `{ error }` string via
  `ApiError` (`lib/api.ts` L106-107 folds `body.error` into `message`) — verified, no
  change needed. New backend text `"Скасувати замовлення можна лише до його
  відвантаження."` will show as-is on a 400.
- Old error string `"Скасувати можна лише замовлення у статусі «нове»."` — **grep clean
  across `frontend/`** (checked `CooperationBadges.tsx`, api layer, all of
  `features/marketplace/`, messages, tests). Nothing to update.
- Doc-comment fix: `marketplace-api.ts` `cancelOrder` `лише зі статусу new` →
  `зі статусу new або confirmed`.

## 2 — "Підтвердив" / "Відвантажив" in the SUPPLIER cabinet only

- `frontend/features/supplier-cabinet/components/CabinetOrdersTab.tsx` →
  `OrderExpandedContent`: two inline lines after the `cancelReasonLabel` line, same
  style as the adjacent detail lines (`#9CA3AF`, 12px, `marginBottom: 8`).
  - `confirmedByLabel` shown when status ∈ {confirmed, shipped, delivered}
  - `shippedByLabel` shown when status ∈ {shipped, delivered}
  - value = `order.confirmedByUserName ?? "—"` / `order.shippedByUserName ?? "—"`
- Buyer view (`marketplace/orders/page.tsx`) deliberately NOT touched (user-scoped).

## 3 — types

- `frontend/features/marketplace/types.ts` — `MarketplaceOrderDto` += `confirmedByUserId`,
  `confirmedByUserName`, `shippedByUserId`, `shippedByUserName` (all `string | null`,
  after `createdByUserName`).
- **Deviation from brief:** `frontend/features/supplier-cabinet/types.ts` does NOT define
  its own `MarketplaceOrderDto` — it re-imports from `features/marketplace/types`
  (comment L290-293 in that file states this explicitly). Only one definition exists
  frontend-side; no generated openapi types file. Single edit covers both consumers.

## 4 — i18n

- `frontend/messages/{uk,en}.json` → `Dashboard.supplierCabinet.ordersTab`:
  - `confirmedByLabel` = "Підтвердив: {name}" / "Confirmed by: {name}"
  - `shippedByLabel` = "Відвантажив: {name}" / "Shipped by: {name}"
- Parity: 4976 = 4976 keys, no diff.

## Verification

- `npx tsc --noEmit` — clean.
- `npx next lint` — clean on touched files (only pre-existing warnings in unrelated
  `consumer-app` / `inventory` / `transfers` / `write-offs`).
- `npx next build` — EXIT 0, `✓ Generating static pages (79/79)`. Pre-existing
  `ENVIRONMENT_FALLBACK` prerender console noise (clustered at the 0/79 mark, next-intl
  error/not-found prerender quirk) — unrelated to this diff, build green.
- i18n parity one-liner — holds.

## Not done / follow-ups

- openapi.json regen — shared deferred debt (backend side).
- NOT committed (per brief).
