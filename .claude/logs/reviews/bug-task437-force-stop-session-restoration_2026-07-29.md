# TASK-437 defect — staff session not restored after force-stop

**Date:** 2026-07-29  
**Device:** realme RMX2063, Android 11 / API 30  
**Build:** `com.shelfguard.mobile` 1.0.0 (SDK 56 development client)  
**Role:** `store_manager`  
**Severity:** high  
**Status:** resolved / device verified

## Reproduction and evidence

1. Log in once with the approved store-manager account.
2. Confirm the authenticated dashboard and role-specific navigation.
3. Send the app to the background with Android Home and reopen it.
4. Confirm the dashboard and the same signed-in identity remain visible — **PASS**.
5. Run `am force-stop com.shelfguard.mobile`.
6. Relaunch the development client and reopen the recent current-source server
   (`http://127.0.0.1:8082`).
7. Wait for the JS bundle and auth bootstrap to finish.

## Actual

The app returns to the unauthenticated “Оберіть, як ви хочете увійти” screen. The staff session is
not restored.

## Expected

The access token/session kind must survive process death. On cold JS startup the app should restore
SecureStore state, call `/auth/me` (refreshing if required), restore the staff identity, and return
to the authenticated application.

## Diagnostic boundary

The test proves a user-visible cold-restoration failure, but not yet its internal cause. Logcat
contains no React Native fatal exception, explicit SecureStore failure, `/auth/me` status, or
refresh status. Because this is a custom Expo development client, relaunch first opens the
development-client home and requires reconnecting the current server. A release APK retest plus
non-secret auth-bootstrap telemetry is required to distinguish:

- missing/unreadable SecureStore token;
- `/auth/me` or refresh failure followed by `terminateSession()`;
- a routing/bootstrap race specific to dev-client reconnect.

No token, password, OTP, or recovery code is recorded in this report.

## Fix retest — 2026-07-29

Retested on the same installed SDK 56 development client with the current Metro JS bundle:

1. Store-manager login and authenticated dashboard — **PASS**.
2. Force-stop, relaunch development client, reopen current server — **PASS**.
3. Connecting/loading bootstrap completed without exposing auth/private UI.
4. The same store-manager identity and authenticated dashboard were restored — **PASS**.
5. HOT background/foreground preserved the session — **PASS**.
6. Logout returned to staff login; previous identity/dashboard were absent — **PASS**.

No React Native fatal, navigation-context exception, or SecureStore exception was present in
logcat. The original user-visible session-loss result is no longer reproduced.

## Fix prepared — 2026-07-29

The route groups previously evaluated the initial in-memory `accessToken=null` while SecureStore
restoration and `/auth/me` ran later in a root `useEffect`. This allowed auth choice to become the
active route before bootstrap completed, with no authenticated redirect from the auth group.

Added explicit `pending`/`ready` auth hydration. Auth, staff, and consumer route groups now remain on
a neutral loading state until SecureStore and, for staff, `/auth/me` finish. A valid restored staff
or consumer session redirects from auth to its correct isolated route group. Invalid tokens,
failed refresh, unreadable persistence, and incomplete consumer snapshots still use centralized
`terminateSession()` before hydration becomes ready.

Five bootstrap tests cover delayed token loading, delayed `/auth/me`, valid staff/consumer restore,
invalid restore, and force-stop-equivalent empty initialization. Status:
`fix_ready_for_device_retest`; physical force-stop/relaunch remains required.
