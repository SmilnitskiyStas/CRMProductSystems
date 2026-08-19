# Stage 15 — Hardening

Date: 2026-08-18

## Changes delivered

- Added a 15-second finite timeout to workspace, personal, and refresh-token HTTP requests.
- Hardened asset URL resolution: rejects executable/data schemes and protocol-relative URLs;
  production accepts HTTPS remote assets, while development can still use HTTP local services.
- Server-driven image props now require HTTPS URLs and reject malformed or executable schemes.
- Added runtime image-error fallback so an unreachable valid image becomes a neutral placeholder
  without breaking its block or page.
- Added explicit integration coverage for Tenant A → Tenant B cache isolation.
- Added configuration-change coverage proving a newer valid version atomically replaces the old
  cached version.

## Failure matrix

| Scenario | Protection / result |
|---|---|
| Invalid configuration | AJV boundary rejects it; same-tenant last-valid or safe default is used. |
| Missing images | Optional image props render neutral placeholders. |
| Broken URL | Unsafe schemes are rejected; failed HTTPS loads fall back to placeholders. |
| API timeout | Both API clients and token refresh terminate after 15 seconds; retry/error UI remains authoritative. |
| Unauthorized | Workspace 401 refresh/retry/termination and personal 401 propagation are regression-tested. |
| Tenant removed | Membership reconciliation moves to another active tenant or clears selection. |
| Feature disabled | Navigation hidden, deep links redirected, server-driven widgets omitted. |
| Configuration changed | New valid version replaces cache and provider state; invalid replacement cannot poison cache. |
| Stale cache | Timestamped last-valid config is shown with a non-blocking stale/offline banner. |
| Unknown block | Registry reports and omits only that block; siblings continue rendering. |
| Unknown navigation item | Runtime route policy ignores it; validator rejects it at the boundary. |

## Tenant isolation acceptance

- Tenant A consumer-content, loyalty code, and history query families are cancelled and removed on
  switch.
- Tenant B loading can only read Tenant B's cache key.
- Product runtime details are keyed by tenant plus product ID.
- Loyalty selection has no fallback to another retailer.
- No retailer-name conditionals or retailer-specific layouts were introduced.

## Verification

- `npm run type-check` — passed.
- `npm run lint -- --quiet` — passed with no errors.
- `npm run test:ci` — 53 suites and 233 tests passed.
- `npx expo export --platform android --output-dir .expo-export-stage15` — passed.

No dependency or native Expo configuration changed; a native rebuild is not required.

## Remaining release blockers

- Canonical published config schema v1 alignment was completed in Stage 16.
- Implement the staff-only preview API before enabling real draft preview.
- Add the previously documented category/product-detail and retailer-invite resolve APIs for full
  cold-start/deep-link behavior.
