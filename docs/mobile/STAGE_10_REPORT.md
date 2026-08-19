# Stage 10 — Promotions / Catalog

Date: 2026-08-17

## Delivered

- Replaced the promotions placeholder with a real tenant/store-scoped promotion list.
- Added promotion loading, empty, retry, store-not-selected, discount, pricing, and product-detail
  states.
- Extended catalog browsing with category chips, search, pagination, store availability, and
  product details.
- Centralized selected membership/store resolution in `useSelectedConsumerContext`; screens no
  longer fall back to the first retailer when the active tenant is missing.
- Verified all promotion and catalog HTTP requests carry both the selected tenant route parameter
  and store query parameter.
- Namespaced the runtime product-detail cache by tenant ID. Identical product IDs from different
  retailers can no longer overwrite or expose each other's detail data.
- Existing cart and favorites behavior was preserved.

## Current backend-contract limits

- Categories are currently derived from the first 100 catalog products because there is no
  consumer category-list endpoint.
- There is no consumer product-by-ID endpoint. Product details are available after opening a
  product from a fetched catalog/promotion list, but a cold-start deep link cannot re-fetch it.
- Promotions are discounted-product projections; there is no promotion campaign detail endpoint.

Mobile fails closed when tenant-specific runtime details are unavailable and never falls back to
another tenant's cached product.

## Verification

- `npm run type-check` — passed.
- `npm run lint` — passed with no errors.
- `npm run test:ci` — 49 suites and 207 tests passed.
- `npx expo export --platform android --output-dir .expo-export-stage10` — passed.

No dependencies or native Expo configuration changed; a native rebuild is not required.

## Next stage

Stage 11 should implement QR retailer onboarding: scan, resolve the tenant through an explicit
backend contract, show a safe preview, confirm, join, switch active tenant, and refresh config.
