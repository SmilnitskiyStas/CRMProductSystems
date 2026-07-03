# TASK-296 / TASK-297 — 2026-07-03 — frontend-developer

## TASK-296 — Dynamic item category form
Frontend already had most of the wiring in place (types, api fetcher, `useItemCategories`
hook, `ItemCategoryFields` shared component with `findMissingRequiredField` client-side
validator, `CabinetItemModal` state/submit logic). Completed the missing piece: rendered
`<ItemCategoryFields>` inside `CabinetItemModal.tsx` (between "Назва товару" and the
price/minQty grid). Added category badge to `CabinetItemsTable.tsx` (next to item name,
using `useItemCategories()` to resolve `labelUa`; no badge when `category` is unset).

Files touched:
- `frontend/features/supplier-cabinet/components/CabinetItemModal.tsx`
- `frontend/features/supplier-cabinet/components/CabinetItemsTable.tsx`
- `frontend/features/marketplace/hooks/useMarketplace.ts` (query key consolidated to `MARKETPLACE_KEYS.itemCategories`)

Files already correct as found (verified, not modified): `marketplace/types.ts`,
`marketplace/api/marketplace-api.ts`, `marketplace/components/ItemCategoryFields.tsx`,
`supplier-cabinet/types.ts`.

## TASK-297 — Provider panel: Клієнти / Постачальники tabs
`frontend/app/(dashboard)/provider/page.tsx`: `activeTab` → `"clients" | "suppliers" | "logs"`
(default `"clients"`). `clientTenants`/`supplierTenants` derived from `tenants` by
`businessType`, search filter applied per active tab. Tab bar now 3 entries with counts;
`Truck` icon added for Постачальники. Stats block untouched (full `tenants`/`health`).
No new API calls.

## Verification
`npx tsc --noEmit` — clean.
`npm run build` — green (had to clear stale `.next/export` dir first; unrelated cache
artifact, not a code issue).

## Issues found
None blocking. No other cleanup needed.
