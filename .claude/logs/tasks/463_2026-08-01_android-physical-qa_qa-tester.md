# TASK-463 — Android physical QA attempt

**Date:** 2026-08-01  
**Agent:** qa-tester  
**Status:** android_partial_device_pass_process_death_failed

Final device subset: `EXPO_OFFLINE=1` unblocked current UI; marketplace online, reconnect/Retry and
POS offline open-shift guard passed. Marketplace cache after force-stop failed at the saved-session
network gate (HIGH). Owner/logout and remaining cache families were not tested.

Physical QA was attempted on the connected realme Android 11 phone. ADB and cleanup passed, but the
installed/last successful APK predates TASK-461..463. The current Metro manifest returned HTTP 500
`UnexpectedServerError`, and a bounded current-source `assembleDebug` failed on locked generated
Android resources plus a missing react-native-worklets CMake reply file. Therefore none of the
offline cache, account-switch or POS guard scenarios is claimed as device-tested.

Full matrix: `.claude/logs/reviews/2026-08-01_TASK-463-android-device-qa.md`.
Security/config automated evidence remains valid; iOS remains separately blocked.

Build recovery later installed the fresh APK with data preserved. Metro cache reset reached running
state, but the fresh dev client still failed before JS UI with `Failed to download remote update` /
manifest `UnexpectedServerError`; logcat showed missing `expo.modules.splashscreen.SplashScreenManager`.
The Android behavior matrix remains not tested and is blocked by a mobile runtime defect.

The replacement APK fixes the native missing-class issue. Retest still cannot reach JS because Expo
CLI manifest generation calls `getUserAsync()`, whose GraphQL request throws an empty-message
`UnexpectedServerError` (`client.js:197 → UserQuery.js:49 → user.js:106`). Metro `/status` is healthy,
but manifest root is HTTP 500. Physical acceptance remains blocked at CLI user resolution.
