# Stage 7 — Dynamic Navigation

Date: 2026-08-17

## Delivered

- Personal bottom tabs are resolved from the validated tenant mobile configuration.
- Configuration order controls tab order; disabled or unavailable entries are hidden.
- Supported identifiers are limited to `home`, `promotions`, `catalog`, `loyalty`,
  `coupons`, `stores`, `news`, and `profile`.
- Icons use a separate whitelist identifier mapped locally to Ionicons. Backend data cannot
  provide a React component name, arbitrary icon name, URL, or Expo route.
- Route definitions, feature requirements, personal-access requirements, local screen names,
  hrefs, and icons are centralized in `features/retail-navigation/policy.ts`.
- Config validation rejects unknown icons, duplicate routes, and configurations without the
  critical `home` and `profile` routes.
- Promotions, coupons, and news received safe server-driven page shells. They render configured
  blocks and show a neutral setup state when no page content has been published.
- Existing detail, scan, product, and history screens remain reachable internally but never
  become configurable tabs.

## Safety boundary

The backend selects only stable route and icon identifiers. The mobile application owns every
mapping to an Expo Router path and React component. Unknown runtime identifiers are ignored even
if an invalid object somehow bypasses TypeScript; invalid persisted or remote configurations are
rejected by the Stage 3 validation boundary.

## Verification

- `npm run type-check` — passed.
- `npm run lint` — passed.
- `npm run test:ci` — passed, including navigation policy and config validation coverage.
- `npx expo export --platform android --output-dir .expo-export-stage7` — passed.

No dependency or native configuration changes were introduced in Stage 7, so an Expo native
rebuild is not required for these changes. Restarting Metro is sufficient during development.

## Next stage

Stage 8 should extend the existing centralized feature-flag policy from tab visibility to route
guards and server-driven block visibility, so disabled features cannot be opened through a deep
link and their widgets are consistently omitted.
