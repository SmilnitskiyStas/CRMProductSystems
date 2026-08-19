# Stage 9 — Retailer-specific Loyalty

Date: 2026-08-17

## Delivered

- The wallet now renders exactly one active retailer membership at a time.
- Retailer switching changes the loyalty card, rotating QR/barcode, balance, mobile config,
  theme, and transaction context through the existing active-tenant bridge.
- Every loyalty-code request now carries an explicit tenant ID and uses a tenant-scoped React
  Query key. A code request is disabled until a valid active membership is selected.
- Removed the wallet list that exposed balances from several retailers on one screen.
- Added a strict membership selector that never falls back to another tenant and rejects blocked
  memberships.
- Added the selected retailer name, balance, tier placeholder, and a link to its tenant-scoped
  transaction history.
- QR and Code 128 rendering, foreground/focus-aware rotation, pagination, and existing consumer
  authentication behavior were preserved.

## Tenant isolation

The active tenant is now required before loyalty card data can load. On tenant switch, prior
tenant code/history queries are cancelled and removed by the existing query-isolation policy.
Tests verify that a missing, blocked, or unknown selected membership never falls back to another
retailer's balance.

## Contract gap

The current backend `LoyaltyMembershipSummaryDto` has no tier field. Mobile accepts an optional
future `tier` property and displays `Не налаштовано` while it is absent. It does not infer or invent
a tier from the balance. Backend support is required before a real retailer-defined tier can be
shown.

## Verification

- `npm run type-check` — passed.
- `npm run lint` — passed with 0 errors and 22 existing warnings.
- `npm run test:ci` — 47 suites and 204 tests passed.
- `npx expo export --platform android --output-dir .expo-export-stage9` — passed.

No dependencies or native configuration changed; a native rebuild is not required.

## Next stage

Stage 10 should complete tenant-scoped promotions/catalog navigation and details, including
category browsing and explicit centralized API context for every request.
