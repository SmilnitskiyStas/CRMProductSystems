# TASK-587: Supplier orders — transit duration + on-time delivery column

**Status:** done

## What changed

- `frontend/features/marketplace/utils.ts` — added `getDeliveryOutcome(shippedAt, deliveredAt, estimatedDeliveryDays)` alongside `getShippingEta`. Returns `{ transitDays, isOnTime }` (rounded calendar days between `shippedAt`/`deliveredAt`, `isOnTime = transitDays <= estimatedDeliveryDays`, `null` if `estimatedDeliveryDays` wasn't captured), or `null` if the order isn't delivered yet. Reuses the same `startOfDay`/`MS_PER_DAY` helpers `getShippingEta` already uses — no new date-math introduced. `getShippingEta`/`ShippingEtaHint` untouched.
- `frontend/features/supplier-cabinet/components/CabinetOrdersTab.tsx` — new "Доставка" column between Status and Total:
  - New `<th>{t("headerDelivery")}</th>` (header row) and a `<td>` calling `DeliveryOutcomeCell`.
  - New `DeliveryOutcomeCell` component (placed after `ShippingEtaHint`, before `ShippingDetail`): renders "—" when `getDeliveryOutcome` is null (not shipped, still in transit, or cancelled); otherwise shows `"{transitDays} дн."` plus a green "Вчасно" badge (`#4ADE80`, same token as `deliveredAt` in `ShippingDetail`) when on time, a red "Запізнення N дн." badge (`#F87171`, same token as `delayReason`) when late, or no badge when `isOnTime` is null (legacy orders with no captured ETA).
  - Bumped the expanded-detail row's `colSpan` from 7 to 8 to match the new column count.
- `frontend/messages/uk.json` / `frontend/messages/en.json` — added `headerDelivery`, `deliveryTransitDays`, `deliveryOnTime`, `deliveryLate` under `Dashboard.supplierCabinet.ordersTab` in both locale files (mirrored — `en.json` wasn't explicitly requested but the two files are kept in 1:1 key parity throughout this namespace, and next-intl has no fallback for a missing EN key).

## Scope respected

- No backend/DTO changes. `getShippingEta`/`ShippingEtaHint` logic untouched — verified by re-reading all branches: not-shipped, in-transit-shipped, delivered (hint no longer applies since status ≠ "shipped", unaffected by this change), and cancelled all still resolve exactly as before.
- Client-facing `app/(dashboard)/marketplace/orders/page.tsx` not touched.
- No filter/sort added — column only.

## Verification

- `npx tsc --noEmit` in `/frontend` — clean, no errors.
- `npm run lint` in `/frontend` — clean, no warnings/errors.
- `node -e "JSON.parse(...)"` on both message files — valid JSON.
- Live-data visual check **not** performed (would require standing up backend + DB + a logged-in supplier session + seeded orders in each state). Verified instead by tracing `DeliveryOutcomeCell`/`getDeliveryOutcome` logic against all four required states (on-time delivered, late delivered, legacy delivered with no ETA, still-in-transit/not-shipped) and confirming `colSpan`/header-count parity (8 `<th>` vs `colSpan={8}`).
