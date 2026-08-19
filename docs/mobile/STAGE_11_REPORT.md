# Stage 11 — QR Retailer Onboarding

Date: 2026-08-17

## Delivered

- Extended the existing camera scanner to recognize retailer QR codes in addition to product
  barcodes.
- Added a strict, versioned invite parser supporting:
  - `SGRTL1.<tenant UUID>` QR payloads;
  - `shelfguard://retailer/<tenant UUID>` custom links;
  - `https://app.shelfguard.ua/retailer/<tenant UUID>` future universal/app links.
- Rejects malformed UUIDs, arbitrary schemes, untrusted HTTPS hosts, unexpected paths, and any QR
  content that attempts to carry UI/configuration data.
- Resolves the parsed tenant against the existing consumer-safe retailer network catalogue.
- Shows retailer name and store preview before any mutation.
- Requires explicit user confirmation, then performs the idempotent loyalty join, selects the
  membership, switches the active tenant, and triggers normal tenant-config refresh.
- Handles invalid QR, unavailable retailer, missing consumer identity, network errors, retry, and
  cancellation states.

## Link architecture

The route parser and onboarding screen are reusable from QR, custom-scheme links, and future HTTPS
links. The existing Expo `shelfguard` scheme is unchanged. OS-level iOS Associated Domains and
Android intent filters are deliberately deferred with external domain verification, as allowed by
the Stage 11 scope; adding those later will require a native rebuild.

## Backend contract limit

There is no dedicated retailer-invite resolve endpoint. Mobile currently validates the tenant ID
against `GET /api/consumer/loyalty/networks` before showing it. A signed/opaque invite resolve API
is recommended before public campaign QR distribution.

## Verification

- `npm run type-check` — passed.
- `npm run lint` — passed with 0 errors and existing warnings only.
- `npm run test:ci` — 50 suites and 214 tests passed.
- `npx expo export --platform android --output-dir .expo-export-stage11` — passed.

No dependency or native Expo configuration changed in this stage, so a native rebuild is not
required for QR scanning through the in-app scanner.

## Next stage

Stage 12 should introduce an explicit internal/dev preview channel that can load validated draft
configuration without ever exposing draft data to ordinary production sessions.
