# TASK-463 Android QA → mobile/devops handoff

Produce a current-source Android debug or preview APK and restore a working Expo dev-client manifest
before QA resumes. The July 29 APK must not be used to accept Aug 1 TASK-461..463 behavior. Current
blockers are Metro manifest HTTP 500 `UnexpectedServerError` and local Gradle generated-output locks/
missing react-native-worklets CMake reply. Preserve application data. After install, hand back to QA
for online cache → offline/process-death → reconnect, owner switch/logout and offline POS guard tests.

Do not broaden the cache allowlist or mutation scope. iOS acceptance remains a separate blocker.

**Build recovery update (2026-08-01):** generated locks/caches were safely cleared and the original
resource-lock/missing-CMake errors did not recur. Clean native compilation exceeded the bounded
window and was intentionally stopped before an APK was produced. Continue from
`.claude/logs/tasks/440_2026-08-01_android-build-recovery_devops-engineer.md`; use a longer idle build
window, then install with `adb install --no-streaming -r` to preserve application data.

**Ready for QA:** final incremental build/install succeeded. Installed APK hash is
`3FC63D56A5F3BC32C012E3CD9EB0E15BE9D4B54DDEEA8C1ABB6F8508D1498058`; package update time
`2026-08-01 20:47:06`, app data preserved. Native backup policy is verified. QA may now resume the
Android matrix without rebuilding; no Metro or app flow was started by devops.

**QA resume result:** build/install is no longer the blocker. Mobile developer now owns a fresh-APK
runtime defect: after Metro cache reset reaches running state, project open fails with
`Failed to download remote update`, manifest `UnexpectedServerError`, and logcat reports missing
`expo.modules.splashscreen.SplashScreenManager`. Fix native/dev-client integration, rebuild/install
preserving app data, then return to QA. Do not change offline product scope.

**Runtime-fix retest:** missing `SplashScreenManager` is resolved. Remaining blocker is Expo CLI
manifest user resolution: `ManifestMiddleware → getUserAsync → UserQuery.currentUserAsync → GraphQL
client.js:197` throws empty-message `UnexpectedServerError`, producing manifest HTTP 500 while Metro
`/status` remains healthy. Repair the Expo session/API path or use the supported offline CLI mode,
then return to QA. APK rebuild is unnecessary unless the fix changes native inputs.
