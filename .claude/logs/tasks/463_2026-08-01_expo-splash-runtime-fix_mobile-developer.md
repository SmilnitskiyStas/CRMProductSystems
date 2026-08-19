# TASK-463 — Expo SDK56 dev-client splash runtime fix

**Date:** 2026-08-01  
**Agent:** mobile-developer  
**Status:** fixed / pending QA current-source smoke

## Root cause

The installed SDK56 development client reached `expo-dev-launcher`, whose Android controller
reflectively loads `expo.modules.splashscreen.SplashScreenManager`. The app's generated activity and
theme declared splash-screen integration, but `expo-splash-screen` was absent from `package.json`, the
npm tree and Expo native autolinking. The Metro `UnexpectedServerError` / remote-update message was a
downstream symptom of this incomplete native runtime, not an application-screen error.

## Fix

- Added Expo's SDK56-aligned `expo-splash-screen~56.0.14` direct dependency.
- Added the `expo-splash-screen` config plugin through `expo install`.
- Regenerated Android config with `expo prebuild --platform android --no-install`.
- Added a regression assertion requiring the exact direct dependency and plugin.
- Did not add or invent splash branding/assets.

## Verification

- Native autolinking resolves `SplashScreenModule` / artifact `56.0.14`: PASS.
- Expo public config and introspection: PASS.
- TypeScript: PASS.
- ESLint: PASS, 0 errors / 12 pre-existing warnings.
- Jest: PASS, 28 suites / 128 tests.
- Android Expo export: PASS, 2164 modules.
- Expo Doctor: 20/21; only the already documented tracked `.expo` index warning remains.
- Android `assembleDebug`: PASS in 175.7 seconds.
- APK: `270898766` bytes; SHA-256
  `E205BF1DFAFBC6068920788504AE8F6E96AFFC5BF7E1EA4A226305DA83D87604`.
- `adb install --no-streaming -r`: PASS. `firstInstallTime` remained
  `2026-07-29 20:24:09`; `lastUpdateTime` became `2026-08-01 21:04:58`.
- Native cold launch: PASS for the defect signature; no missing `SplashScreenManager`,
  `DevLauncherManifestParser`, `UnexpectedServerError`, or fatal exception in scoped logcat.

## Remaining QA gate

The bounded post-build Metro attempts did not bind port 8082, so no current-source JS/UI result is
claimed. Return to QA for one owned Metro manifest/UI smoke, then continue the TASK-463 Android
offline/security matrix. iOS remains blocked on an iOS build/device.

## Cleanup

The owned Metro attempts were terminated, `adb reverse tcp:8082` was removed, and the application
was force-stopped with its data retained. No credentials or business mutations were used.
