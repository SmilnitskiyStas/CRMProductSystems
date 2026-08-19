# Stage 16 — Canonical Config Integration & Acceptance

Date: 2026-08-18

## Delivered

- Connected `MobileConfigProvider` to `GET /api/v1/mobile/config?tenantId=...` through the
  personal/anonymous-compatible API client.
- Added canonical `schemaVersion: 1` runtime support while retaining schema v0 solely for local
  mock/backward-compatible development data.
- Normalizes canonical partial feature objects by explicitly defaulting missing known flags to
  `false`; unknown feature keys remain visible to AJV and are rejected.
- Added canonical theme `logoUrl` support.
- Maps the known canonical Ionicon identifiers into the mobile navigation icon whitelist. Unknown
  backend icon strings are rejected rather than rendered dynamically.
- Published responses are validated, tenant-matched, cached, and exposed as source `published`.
- Published 404, timeout, cross-tenant, incompatible, or invalid responses use the Stage 13
  same-tenant last-valid/safe-default path; production no longer treats mock as its primary source.

## Acceptance coverage

- Unit: configuration validation, theme generation, feature policy, component registry.
- Integration: Tenant A cache/query data is cleared or isolated before Tenant B is rendered.
- Loyalty: code/history/membership selection never falls back to another retailer.
- Runtime: invalid configuration, unknown blocks/navigation, feature-disabled routes, image/URL
  failures, unauthorized, timeout, removed tenant, config replacement, and stale cache.
- Architecture scan: no retailer-name branching or retailer-specific home screen was introduced.

## Verification

- `npm run type-check` — passed.
- `npm run lint -- --quiet` — passed with no errors.
- `npm run test:ci` — 54 suites and 236 tests passed.
- `npx expo export --platform android --output-dir .expo-export-stage16` — passed.

No dependency or native Expo configuration changed; a native rebuild is not required.

## Remaining external blockers

- Staff-only preview endpoint (Stage 12 architecture is ready, actual API absent).
- Dedicated consumer category/product-detail endpoints for complete cold-start details.
- Signed retailer-invite resolve endpoint for public QR campaigns.
- Analytics ingestion transport; mobile currently uses the safe no-op adapter.
