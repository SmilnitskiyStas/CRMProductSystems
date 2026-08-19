# Stage 18 — App Builder runtime integration

Date: 2026-08-19

## Result

- Added a runtime data-source resolver between published App Builder blocks and the mobile block
  renderer. Web-authored props now drive real consumer data instead of being rejected by the old
  fixture-oriented mobile validators.
- Connected banner carousel, loyalty card/balance, promotion carousel/grid, product
  carousel/grid, quick actions, news list, and store list to the existing consumer APIs.
- Preserved static Hero Banner and Section Header authoring; added Hero CTA support and section
  alignment support.
- Quick actions now navigate to their configured mobile destinations.
- Promotions and Catalog use their published server-driven page when blocks are configured, with
  the existing full-featured screens retained as a fallback for empty configurations.
- Home pull-to-refresh now refreshes published mobile config as well as consumer content.
- Applied published theme tokens to the configurable page shell and the key Home shell elements.

## Verification

- `npm run type-check` — passed.
- `npm run lint -- --quiet` — passed.
- `npm run test:ci` — 54 suites, 245 tests passed.
- Android Expo export — passed; temporary export removed.

## Build impact

JavaScript/TypeScript only. No native dependency or app configuration changed, so a native EAS
rebuild is not required. Reload the Expo bundle to exercise the new renderer.
