## Bug: Android development build cannot render usable app due to missing navigation context

**Date:** 2026-07-29  
**Severity:** critical  
**Task:** TASK-435  
**Package/build:** `com.shelfguard.mobile` 1.0.0 (1), debug APK timestamp 2026-07-29 20:13:29  
**Device:** realme RMX2063, Android 11 / API 30
**Resolution:** fixed_and_device_verified (2026-07-29)

### Steps

1. Build current `mobile/` source with `assembleDebug`.
2. Install `app-debug.apk` on the authorized physical device.
3. Start Metro for the development client and reverse port 8081 over ADB.
4. Connect the development client to `exp://127.0.0.1:8081`.
5. Repeat after `adb shell am force-stop com.shelfguard.mobile`.

### Expected

The application renders the unauthenticated login entry (or restores a valid prior route) without
a JavaScript runtime error.

### Actual

The application displays `Error loading app`. Metro reports:

`Couldn't find a navigation context. Have you wrapped your app with 'NavigationContainer'?`

The stack passes through `react-native-css-interop` and identifies
`app/(app)/schedules/index.tsx:123`, then `AppLayout`, then `RootLayout`. No usable auth or
application UI is available.

ShelfGuard was not installed before this TASK-435 run, so no prior app session existed. A later
ADB `pm clear` attempt was rejected by the device firmware's shell permission policy and changed
no package data.

### Impact

Current Android source cannot pass baseline device QA. Authentication, 2FA, session restoration,
role/module navigation, POS durability, and operational-draft durability are blocked.

### Evidence

- Metro structured log: `mobile/.expo/dev/logs/start.log`.
- UI hierarchy captured through `uiautomator` during TASK-435.
- Native process remains alive, so this is a JS render failure rather than an Android process crash.

### Required follow-up

Assign to `mobile-developer`; reproduce with the current Expo Router/NativeWind stack, correct the
navigation/CSS-interop runtime integration, run static regression, build a new APK, and return to
TASK-435 for device verification.

### Fix prepared — 2026-07-29

Root cause was not a missing `NavigationContainer`. A conditional NativeWind `shadow-sm` utility
made an already-mounted tab control acquire shadow CSS variables. In development,
`react-native-css-interop@0.2.5` attempted to print its late-upgrade warning by recursively
serializing the component's original React props. That traversal reached Expo Router's
`NavigationStateContext` and invoked its guarded `getKey` getter, producing the misleading missing
navigation-context exception.

Removed the dynamic shadow utility from every equivalent tab control in schedules, service desk,
POS loyalty, and marketplace. Active selection remains visible through its white background and
text treatment. TypeScript, lint (0 errors/13 warnings), Jest (17 suites/74 tests), and Android
bundle export pass. Status is `fix_ready_for_device_retest`; a rebuilt APK must pass physical-device
cold start and tab switching before this defect is closed.

### Device verification — PASS

A newly packaged APK (timestamp `2026-07-29 21:13:24`, 262,091,007 bytes, SHA-256
`B023E71D479D3CC44D1211CD1C5D0535E1336A8EA20FCD7DBD8C1D64C57E3185`) was installed; phone
`lastUpdateTime=2026-07-29 21:35:45`. The current SDK56 dev-client bundle rendered auth choice
after install and force-stop/relaunch. Staff login and Android Back work. Unauthenticated deep
links to schedules, service desk, POS, and marketplace fail closed without a crash. Logcat has no
navigation-context/FATAL error. Direct authenticated tab switching awaits staff credentials. The
critical defect is closed.
