# TASK-435 mobile device QA — pause / resume handoff

**Paused:** 2026-07-29  
**Scope:** `mobile/` and `.claude/` only  
**Reason:** user explicitly requested testing stop/pause  
**Status:** `paused_user_request` — do not infer release readiness

## Tested device and build

- Device: realme RMX2063, Android 11 / API 30, arm64-v8a.
- ADB serial: `13cb6660`; final state `device`.
- Package retained: `com.shelfguard.mobile`, version 1.0.0 (1), SDK 56 development client.
- Installed/current APK: `mobile/android/app/build/outputs/apk/debug/app-debug.apk`.
- APK size: 262,091,007 bytes; timestamp 2026-07-29 21:13:24.
- SHA-256: `B023E71D479D3CC44D1211CD1C5D0535E1336A8EA20FCD7DBD8C1D64C57E3185`.
- API: `https://api.agrusystems.pp.ua:10054/api`.
- Current-source JS was served through Metro on localhost/ADB reverse port 8082.
- Approved role accounts were supplied and used in forms. Passwords, tokens, OTP values, and
  recovery-code values are intentionally absent from all durable logs.
- One approved single-use recovery code was consumed successfully. Count: **1**. It must not be
  reused.

## Result summary

### TASK-435 baseline

- Fresh install/native cold launch/current bundle/auth selection/staff and consumer entry — PASS.
- Original navigation-context launch blocker — fixed and device-verified.
- Android Back, malformed staff email, invalid credentials, HOT form preservation — PASS.
- Required-field Ukrainian localization for staff login, consumer login, registration — fixed and
  device-verified.
- Camera permission and live scanner entry — PASS; no barcode was scanned.
- Provider guarded POS transition fails closed; merchandiser write-off deep link shows explicit
  access denial — PASS.
- Role smoke: provider, network manager, store manager, storekeeper, merchandiser — PASS.
- Enterprise-admin role navigation after recovery login — PASS.
- No React Native fatal/navigation-context/SecureStore crash in final exercised logcat paths.

### TASK-437 session lifecycle

- Store-manager normal login, HOT background/foreground, logout/private UI cleanup — PASS.
- Force-stop/reconnect authenticated session restoration — fixed and device-verified.
- Separate offline-cold bootstrap/network-transition case — **pending**. An earlier run returned to
  auth choice after Wi-Fi was re-enabled but before API readiness was proven. A controlled current
  fix retest was not completed.

### TASK-438 mobile 2FA

- Mobile challenge screen and recovery-code mode — PASS.
- Hardware Back before input and after focusing OTP — fixed and device-verified; no accidental
  verify submission.
- Exactly one approved recovery code accepted; enterprise-admin dashboard/tools/logout — PASS.
- Live authenticator TOTP success — **pending** because no current approved TOTP was supplied.
- 2FA setup/enable/disable remains intentionally web-only.

### TASK-443 durable POS

- Manager and storekeeper can reach POS.
- Seeded environment reports `Зміна не відкрита`.
- Cart/customer draft, force-stop restore, ambiguous retry, and duplicate-submit device acceptance
  — **not tested / blocked by no existing active shift**.
- No shift was opened and no sale/payment was submitted.

### TASK-444 operational drafts

- Transfer note autosave, background preservation, offline banner/preservation — PASS.
- Final focused incomplete-transfer cycle:
  - manager same-owner cold restore — PASS;
  - storekeeper non-disclosure without discard — PASS;
  - manager return restore — PASS;
  - explicit Delete and cold absence — PASS.
- Owner-isolation defect is fixed and transfer path is device-verified.
- Dev-client reconnect reached authenticated dashboard in about 64 seconds; at 15 seconds no
  timeout/retry UI was visible.
- Receipt-create — **blocked by missing approved mobile/API contract**.
- Write-off draft — **not tested**, form required a real scanned product/batch.
- Production draft — **not tested**, no safe enabled production data/form was established.

## Safety and final state

- No server-side business mutation was performed: no shift open/close, sale, payment, receipt,
  write-off, transfer, production order, registration, or stock change.
- No test draft marker remains; final explicit delete was cold-verified.
- App remains installed; app data was not cleared; package was not uninstalled.
- Phone final connectivity: Wi-Fi ON (`wifi_on=1`), mobile data OFF (`mobile_data=0`).
- ADB remains running and device remains connected/authorized.
- Owned Metro process stopped: PID `42044`, Node.js, started 2026-07-29 21:36:11, listener 8082.
- Port 8082 final listener count: 0.
- ADB reverse `tcp:8082 -> tcp:8082` removed; final reverse list is empty.
- No unrelated Node process, ADB daemon, app package, or phone data was stopped/removed.

## Remaining blockers / next acceptance

1. Controlled TASK-437 offline cold bootstrap: start authenticated, disable Wi-Fi with mobile data
   already OFF, force-stop/reconnect without invalidating token, verify retryable offline/bootstrap
   UI, restore Wi-Fi, prove API readiness, tap Retry, confirm authenticated dashboard.
2. TASK-438 live TOTP success with a current user-approved TOTP. Do not reuse the consumed recovery
   code.
3. TASK-443 seeded existing active POS shift plus safe products/customer fixture, with explicit
   authorization for local cart only; do not submit a sale.
4. TASK-444 receipt-create contract decision and safe write-off/production fixtures.
5. Release-like build regression; current measurements include development-client reconnect time.

## Exact safe resume commands

From `C:\Users\stass\source\CRMProductSystems\mobile`:

```powershell
npx.cmd expo start --dev-client --port 8082
```

After Metro is listening, from any directory:

```powershell
$adb = 'C:\Users\stass\AppData\Local\Android\Sdk\platform-tools\adb.exe'
& $adb -s 13cb6660 get-state
& $adb -s 13cb6660 reverse tcp:8082 tcp:8082
& $adb -s 13cb6660 reverse --list
& $adb -s 13cb6660 shell monkey -p com.shelfguard.mobile -c android.intent.category.LAUNCHER 1
```

If the development-client home appears, tap the existing recent server
`http://127.0.0.1:8082`. Allow up to about 65 seconds before diagnosing bootstrap failure.
Before any offline test record:

```powershell
& $adb -s 13cb6660 shell settings get global wifi_on
& $adb -s 13cb6660 shell settings get global mobile_data
```

Always restore the exact original connectivity and remove only this task's reverse when pausing:

```powershell
& $adb -s 13cb6660 reverse --remove tcp:8082
```

Do not uninstall the app, clear its data, stop ADB globally, reuse the consumed recovery code, open
a POS shift, or submit any business form without new explicit authorization.

## Files written or updated by this QA stream

- `.claude/logs/reviews/2026-07-29_TASK-435-mobile-device-qa-pause-handoff.md`
- `.claude/logs/reviews/2026-07-29_mobile-baseline.md`
- `.claude/logs/reviews/bug-task435-navigation-context_2026-07-29.md`
- `.claude/logs/reviews/bug-task435-auth-required-localization_2026-07-29.md`
- `.claude/logs/reviews/bug-task435-2fa-back-navigation_2026-07-29.md`
- `.claude/logs/reviews/bug-task437-force-stop-session-restoration_2026-07-29.md`
- `.claude/logs/reviews/bug-task437-offline-cold-bootstrap-logout_2026-07-29.md`
- `.claude/logs/reviews/bug-task444-owner-switch-deletes-draft_2026-07-29.md`
- `.claude/logs/tasks/437_2026-07-29_auth-refresh-session-cleanup_mobile-developer.md`
- `.claude/logs/tasks/437_2026-07-29_cold-session-hydration_mobile-developer.md`
- `.claude/logs/tasks/438_2026-07-29_mobile-2fa-login_mobile-developer.md`
- `.claude/logs/tasks/438_2026-07-29_android-back-cancellation_mobile-developer.md`
- `.claude/logs/tasks/443_2026-07-29_durable-pos-network-recovery_mobile-developer.md`
- `.claude/logs/tasks/444_2026-07-29_durable-operational-drafts_mobile-developer.md`
- `.claude/tasks/mobile-roadmap.md`
- `.claude/tasks/current.md`
- `.claude/tasks/blocked.md`

Some task implementation logs above were initially created/updated by mobile-development work in
the shared worktree and then amended with device acceptance. Existing unrelated mobile/source
changes were preserved.
