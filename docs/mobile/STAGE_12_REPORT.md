# Stage 12 — Preview Mode

> Superseded on 2026-08-19 by Stage 17: the final product decision keeps draft preview in the
> staff web admin only. All mobile preview-token and draft-rendering code described below has been
> removed. See `STAGE_17_REPORT.md` and `docs/integration/MOBILE_API.md` §7B.

Date: 2026-08-18

## Delivered

- Added a dedicated internal preview session isolated from ordinary mobile configuration state.
- Preview can only be enabled when React Native `__DEV__` is true and an explicit non-trivial
  token is entered. Production builds always reject activation and redirect away from the internal
  screen.
- The token is held only in memory, never persisted to AsyncStorage, SecureStore, logs, URLs, or
  the production last-valid cache.
- Preview requests use a dedicated `X-Mobile-Preview-Token` header and anticipated staff API route.
- Preview configuration passes the same runtime validation and strict tenant-ID match as ordinary
  configuration.
- Draft configuration is never written to the published/last-valid cache. Invalid, unavailable,
  or cross-tenant preview fails closed to a safe non-preview state.
- Added an always-visible amber PREVIEW indicator with one-tap exit whenever preview is active.
- Added an internal token-entry screen hidden from normal bottom navigation.

## Current blockers

1. Backend has no `GET /api/v1/mobile/config/preview` endpoint yet.
2. Resolved in Stage 16: mobile runtime validation now supports canonical schema v1. Only the
   preview endpoint itself remains unavailable.

The client architecture is complete, but a real draft cannot be displayed until both contracts
are delivered in lockstep. Ordinary production sessions continue using the existing safe path and
can never opt into preview.

## Verification

- `npm run type-check` — passed.
- `npm run lint -- --quiet` — passed with no errors.
- `npm run test:ci` — 51 suites and 219 tests passed.
- `npx expo export --platform android --output-dir .expo-export-stage12` — passed.
- Tests verify production denial, token requirements, dedicated headers, no cache persistence,
  and cross-tenant rejection.

No dependency or native Expo configuration changed; a native rebuild is not required.

## Next stage

Stage 13 should complete published-config v1 integration and expand offline UX around the existing
tenant-scoped last-valid cache, including a non-blocking stale/offline indicator.
