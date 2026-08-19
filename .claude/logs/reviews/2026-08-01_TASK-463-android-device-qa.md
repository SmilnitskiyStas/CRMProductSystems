# TASK-463 Android physical QA

**Date:** 2026-08-01  
**Device:** realme RMX2063, Android 11, serial recorded in local QA instructions only  
**Verdict:** android_partial_device_pass_process_death_failed

## Evidence matrix

- ADB authorization/connectivity: **PASS**.
- Device baseline and cleanup (Wi-Fi on, mobile data off, no ADB reverse): **PASS**.
- Current-source APK install: **PASS**. Fresh APK metadata is recorded in the recovery section below;
  `adb install -r` preserved application data.
- Current-source app start: **FAIL BEFORE JS UI**. After targeted `expo start --clear`, Metro reached
  running state, but the dev client returned `Failed to download remote update`; Metro logged
  manifest `UnexpectedServerError`.
- Native runtime: **FAIL**. Fresh-APK logcat reports missing
  `expo.modules.splashscreen.SplashScreenManager` during dev-launcher initialization.
- Online cache creation, last-updated UI, offline/process-death restore, stale warning, reconnect/refetch,
  cached refresh failure: **NOT TESTED** (app UI never loaded from current source).
- Cross-owner manager → storekeeper → manager isolation and logout cleanup: **NOT TESTED**.
- POS offline open/close/sale guards: **NOT TESTED**.
- Storage pressure and hard expiry: **AUTOMATED/SECURITY EVIDENCE ONLY**, not forced physically.
- Non-allowlisted persistence/privacy and Android backup configuration: **CODE/CONFIG SECURITY EVIDENCE
  ACCEPTED** from TASK-463 security review; no secret/storage dump was performed.
- Logcat: no app-JS fatal was observed because current-source JS UI did not start. Native dev-launcher
  manifest failure is the blocking runtime condition.
- iOS: **NOT TESTED / BLOCKED**; no iOS build/device is available in this Windows workspace.

No credentials were submitted, no cache bodies/owner identifiers were recorded, no server business
mutation was performed, and no app data was cleared.

## Resume gate

1. Fix the fresh APK's dev-client/native integration so the manifest/update loads and
   `SplashScreenManager` is present or no longer referenced.
2. Rebuild/install preserving data and confirm JS UI loads through one owned Metro session.
3. Re-run the full physical matrix.

## Cleanup

Owned Metro was stopped, `tcp:8082` reverse removed, application force-stopped and retained, Wi-Fi
restored on, mobile data left off. Port 8082 has no listening process after cleanup.

## Build recovery follow-up — 2026-08-01

DevOps stopped one workspace Gradle daemon, resolved and cleared only generated
`android/app/build`, `android/.gradle`, `react-native-worklets/android/.cxx`, and
`react-native-worklets/android/build`. Windows required elevated removal for locked generated
native `.so` files; no package source was removed. A fresh 4 GB heap `assembleDebug` passed Gradle
configuration and entered full native/CMake compilation without reproducing the original locked
`values.xml` or missing worklets CMake reply errors. It did not finish within the bounded ~9-minute
window and produced no APK, so it was terminated; its single-use daemon and two build-started Java
compiler workers were stopped. Current-source install/package checks remain not run.

Next action: reserve a longer, otherwise idle build window and run the exact stop-parsed command in
the build-recovery log. Do not clean again unless either original generated-output error returns.

**Final recovery:** an authorized incremental build then completed in 497 seconds. Fresh APK SHA-256
`3FC63D56A5F3BC32C012E3CD9EB0E15BE9D4B54DDEEA8C1ABB6F8508D1498058` was installed with
`adb install --no-streaming -r`; package lastUpdateTime is `2026-08-01 20:47:06` while original
firstInstallTime remains `2026-07-29 20:24:09`. Android backup exclusions and permission policy are
present in packaged resources. The current-source build/install blocker is cleared; the actual
TASK-463 Android QA matrix remains not run in this devops step.

## Fresh APK runtime follow-up — 2026-08-01

QA resumed without rebuilding. The fresh package was confirmed installed. Initial Metro startup found
a corrupt file-map cache; the authorized targeted `--clear` recovery succeeded and `/status` reported
the packager running. Nevertheless, project open failed with `Failed to download remote update`;
Metro emitted `UnexpectedServerError`, and logcat independently showed
`ClassNotFoundException: expo.modules.splashscreen.SplashScreenManager`. This is a mobile
native/dev-client runtime defect, not an ADB, stale-APK, API or remaining Metro-cache blocker.

## Runtime-fix retest — 2026-08-01

The replacement APK installed at 21:04:58 removes the prior `SplashScreenManager` signature.
One owned Metro with a cleared cache reached `packager-status:running`, but its manifest root returned
HTTP 500 `{"error":"UnexpectedServerError"}` to the dev client and direct Android-header request.
The exact redacted stack was reproduced independently: Expo CLI `ManifestMiddleware` calls
`getUserAsync()`, which fails at `api/graphql/client.js:197:34`, through
`UserQuery.currentUserAsync:49:22` and `user.getUserAsync:106:22`. The error message is empty.
Therefore this is an Expo CLI account/API user-resolution blocker; JS UI and the prioritized physical
cache/owner/POS matrix remain **NOT TESTED**. No additional Metro retry was performed.

## Offline-CLI recovery and physical subset — 2026-08-01

Authorized `EXPO_OFFLINE=1` produced manifest 200 and loaded current UI. Marketplace suppliers online:
**PASS**. Wi-Fi off + force-stop privacy fail-closed: **PASS**, but allowlisted cache restore:
**HIGH FAIL** (saved-session network gate only). Reconnect + Retry: **PASS**. POS with no active shift:
offline `Відкрити зміну` was disabled (`clickable=false`, `enabled=false`), **PASS**, no request or
mutation. Owner/logout, schedules and production were not run. Metro was stopped and app data retained;
ADB cleanup verification then hit a transport hang.
