# Stage 8 — Feature Flags

Date: 2026-08-17

## Delivered

- Kept feature evaluation centralized in `features/feature-flags/policy.ts` and the existing
  `useRetailFeature` abstraction.
- Dynamic navigation continues to omit items whose feature is disabled.
- Added a personal-layout route guard so disabled features cannot be opened through a deep link
  or programmatic navigation.
- Loyalty wallet/history routes additionally require a personal consumer identity.
- Added optional, whitelisted `feature` requirements to server-driven block configuration.
- `PageRenderer` omits blocks whose declared feature is disabled before resolving or rendering
  their component.
- Mobile config validation accepts only the eight known feature identifiers and rejects arbitrary
  feature names.

## Centralized behavior

Feature checks now have one policy boundary for boolean evaluation, one navigation policy for
route requirements, and one renderer filter for configurable widgets. Screens and block
components do not duplicate tenant feature checks.

## Verification

- `npm run type-check` — passed.
- `npm run lint` — passed with 0 errors and 22 pre-existing warnings outside Stage 8 changes.
- `npm run test:ci` — 46 suites and 202 tests passed.
- `npx expo export --platform android --output-dir .expo-export-stage8` — passed.

No dependencies or native Expo configuration changed. A native rebuild is not required; restarting
Metro is sufficient.

## Next stage

Stage 9 should connect retailer-specific loyalty UI to the active tenant context while preserving
the current consumer/staff compatibility behavior and tenant-isolated query keys.
