# Stage 14 — Analytics Events

Date: 2026-08-18

## Delivered

- Added a tenant-aware consumer analytics abstraction for:
  - `tenant_selected`;
  - `promotion_opened`;
  - `coupon_opened`;
  - `loyalty_card_opened`;
  - `product_opened`;
  - `retailer_joined`.
- Every accepted event requires a valid non-empty `tenantId`.
- Event payloads are rebuilt through a runtime allowlist. Arbitrary caller properties are never
  spread or forwarded.
- Allowed identifiers are normalized, length-limited, and character-restricted.
- Phone, email, customer name, balance, prices, tokens, QR/barcode values, store address, and
  free-form text are not accepted by any event schema.
- Connected events at centralized tenant switching, promotions, coupon page, loyalty wallet,
  product details, and retailer onboarding points.
- Product events preserve only an allowlisted source (`catalog`, `promotion`, `news`, `direct`).
- Added an injectable transport boundary. The default is intentionally no-op until a production
  ingestion contract exists; no sensitive local analytics queue is created.

## Verification

- `npm run type-check` — passed.
- `npm run lint -- --quiet` — passed with no errors.
- `npm run test:ci` — 53 suites and 227 tests passed.
- `npx expo export --platform android --output-dir .expo-export-stage14` — passed.
- Privacy tests prove arbitrary phone/balance/QR properties are discarded and malformed IDs are
  rejected.

No dependency or native Expo configuration changed; a native rebuild is not required.

## Next stage

Stage 15 should perform the specified hardening matrix across configuration, network, tenant,
feature, URL/image, cache, registry, and authorization failure modes.
