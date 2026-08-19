# TASK-461/463 offline process-death — pause/resume handoff

**Paused:** 2026-08-01 by user request  
**Status:** source fix ready for Android device retest; iOS build/device pending

## Completed before pause

- Fixed the HIGH defect where TASK-437's offline bootstrap gate hid valid allowlisted cache after
  Android force-stop/process death.
- Added a versioned 24-hour SecureStore snapshot with only tenant ID, user ID, role and exact routes
  verified during the last online session. Tokens remain only in existing SecureStore auth keys;
  no email/name/broad permissions/module payload is copied.
- Offline bootstrap requires pointer/snapshot owner match, hydrates that owner namespace, then keeps
  only routes whose allowlisted query family actually has retained data.
- Offline shell allows only schedule list, marketplace supplier list and production recipe list.
  Dashboard, stock, POS, search, detail screens, pull refresh and all mutations are denied/suppressed.
- Reconnect reruns `/auth/me`, reloads module settings and only then returns to normal navigation.
- Invalid/expired/corrupt snapshot, owner mismatch, terminal 401/403 and explicit logout fail closed
  and clean snapshot/current-owner cache.

## Checks

- TypeScript: PASS.
- ESLint: 0 errors; 12 unrelated existing warnings.
- Focused auth/cache: 8 suites / 43 tests PASS.
- Full Jest: 29 suites / 136 tests PASS.
- No APK/export/device action was run after this fix because the user requested a stop.

## Resume exactly here

1. Reconnect the Android phone and confirm current-source build/runtime before changing app data.
2. Online as a test staff owner, load marketplace suppliers and confirm offline-session snapshot is
   produced after module settings resolve.
3. Disable all network, force-stop, cold launch: cached marketplace list must open with offline time.
4. Prove dashboard, stock, POS, marketplace search/detail and every mutation are denied/no-request.
5. Restore network: `/auth/me` + module settings must succeed before dashboard/normal routes appear.
6. Repeat corrupt/expired snapshot, owner switch and logout cleanup without deleting another owner.
7. Record Android evidence in TASK-463; run equivalent iOS process-death/Keychain/backup matrix only
   when an iOS build and device are available. Do not infer iOS PASS from Android.
