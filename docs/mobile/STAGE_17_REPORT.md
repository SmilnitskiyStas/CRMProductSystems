# Stage 17 — Final backend contract reconciliation

Date: 2026-08-19

## Result

- Removed the mobile draft-preview path. The consumer app now loads only the published
  `GET /api/v1/mobile/config` document, as required by the final product decision.
- Removed the preview token store, header policy, screen, banner, repository call, loader branch,
  and related tests. Draft preview remains a staff/web-admin-only capability.
- Replaced the obsolete UUID/`SGRTL1` invite contract with the shipped slug contract:
  `shelfguard://join/{slug}` and trusted `https://app.shelfguard.ua/join/{slug}` links.
- Added strict link parsing, public retailer resolution through
  `GET /api/v1/retailers/{slug}/public`, and authenticated joining through
  `POST /api/v1/retailers/{slug}/join`.
- After join, the returned tenant becomes the selected loyalty tenant and active mobile tenant.
- Added the Expo Router `/join/[slug]` entry route for the already-registered `shelfguard` scheme.

## Verification

- `npm run type-check` — passed.
- `npm run lint -- --quiet` — passed.
- `npm run test:ci` — 53 suites, 234 tests passed.
- Android Expo export — passed; temporary export removed.

## Native rebuild

No native dependency or `app.json` change was made. The custom `shelfguard` scheme was already
registered, so this stage does not require a new native Expo/EAS build. Universal/App Links still
require production domain association files and verified native configuration; that remains tied
to the store/domain deployment work described by TASK-440.
