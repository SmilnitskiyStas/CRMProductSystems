# Mobile Stage 4 Report

Date: 2026-08-17  
Status: complete for the retail-engine foundation

## Implemented

- Dynamic tenant theme remains limited to whitelisted configuration:
  - primary/secondary/background/surface colors;
  - primary/secondary text colors;
  - button/card radius;
  - compact/comfortable spacing preset.
- Semantic runtime tokens now also derive:
  - readable `onPrimary` foreground using primary-color luminance;
  - a neutral theme-derived border color.
- Added reusable theme-aware primitives:
  - `RetailScreen`;
  - `RetailCard`;
  - `RetailPressableCard`;
  - `RetailPrimaryButton` with pressed/disabled/pending behavior.
- Personal navigation already uses tenant primary/text/surface tokens.
- Personal profile was migrated to theme-aware primitives as the first legacy-screen migration.
- Workspace styling remains independent and unchanged.

## Migration boundary

The theme engine is ready for all new server-driven blocks. Existing large personal screens such
as home, wallet and catalog still contain legacy NativeWind colors. They should be migrated as
their UI is decomposed into reusable blocks during Server-Driven UI/Core Blocks stages, instead of
performing a risky one-shot visual rewrite now.

The backend still controls no production theme because the canonical config schema/API are not
available. Current values come from validated `schemaVersion: 0` mock/last-valid configuration.

## Tests

- semantic colors/radii/spacing generation;
- dark primary receives white foreground;
- light primary receives black foreground;
- unsafe theme values remain rejected at the config boundary.

## Expo delivery impact

Stage 1-4 changes are TypeScript/JavaScript only. They do not add native dependencies or modify
Expo native configuration, so they can run in the existing compatible development build and can be
delivered through EAS Update after preview verification.

The wider current working tree already contains older uncommitted native-affecting changes
(`expo-location`, config plugins, permission/backup configuration, EAS Update setup). If no binary
has been built since those changes were introduced, one new preview/development build is required
before treating that full working tree as OTA-compatible.

## Verification

- strict TypeScript — passed;
- targeted Stage 4 ESLint — passed;
- full ESLint — 0 errors, 22 pre-existing warnings outside Stage 4;
- targeted theme/config tests — 6/6 passed;
- full regression — 42/42 suites, 184/184 tests passed;
- Android Expo export — passed (2,326 modules bundled).
