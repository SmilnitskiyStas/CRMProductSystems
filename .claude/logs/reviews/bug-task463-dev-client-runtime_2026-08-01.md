## Bug: Fresh Android dev client cannot load current-source project

Date: 2026-08-01  
Severity: high  
Task: TASK-463

Steps: install the fresh current-source debug APK with `adb install -r`; start one Metro dev-client
server with cleared cache; wait until `/status` reports running; open through USB reverse.

Expected: the manifest/update downloads and ShelfGuard JavaScript UI starts.

Actual: the client shows `Failed to download remote update`; Metro logs manifest
`UnexpectedServerError`; logcat reports
`ClassNotFoundException: expo.modules.splashscreen.SplashScreenManager`. No JS screen, login or
offline-cache test is reachable.

Retest after the native fix: `SplashScreenManager` is resolved. The remaining exact cause is Expo CLI
manifest account lookup: `getUserAsync()` fails in GraphQL `client.js:197`, via `UserQuery.js:49` and
`user.js:106`, with empty-message `UnexpectedServerError`. This makes the manifest root HTTP 500 even
though Metro `/status` is running.
