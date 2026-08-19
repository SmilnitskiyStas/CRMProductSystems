# TASK-435 — NativeWind launch fix

**Date:** 2026-07-29  
**Agent:** mobile-developer (Codex)  
**Status:** fix_ready_for_device_retest  
**Scope:** `mobile/` only

## Root cause

The device error was not caused by a missing Expo Router navigation provider. Dynamically adding
NativeWind `shadow-sm` to an already-mounted tab control triggered the development-only late
CSS-variable upgrade warning in `react-native-css-interop@0.2.5`. Its warning serializer recursively
enumerated React props, reached Expo Router's `NavigationStateContext`, and invoked the guarded
`getKey` getter. That secondary exception surfaced as `Couldn't find a navigation context`.

The Metro history demonstrated the same failure first in service desk and later in schedules, so
the defect was a reusable styling pattern rather than a schedules route defect.

## Implemented

- Removed conditional `shadow-sm` from tab controls in schedules and service desk.
- Removed the same latent crash trigger from POS loyalty and marketplace tabs.
- Preserved selected-state affordance through existing background and text color treatment.
- Did not add a manual `NavigationContainer` or hide any route.

## Verification

- `npm run type-check` — pass.
- `npm run lint` — pass, 0 errors / 13 existing warnings.
- `npm run test:ci` — pass, 17 suites / 74 tests.
- `npx expo export --platform android --output-dir .expo-export-task435` — pass, 2,151 modules.
- Physical-device validation — pending rebuilt APK; no device-pass claim.
