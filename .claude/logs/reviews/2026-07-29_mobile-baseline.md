# TASK-435 — Mobile real-device baseline QA

**Date:** 2026-07-29  
**Agent:** qa-tester (Codex)  
**Status:** partial_pass_remaining_pos_and_totp  
**Scope:** `mobile/` only

## Verdict

A fresh current-source Android development build was built, installed, fixed, rebuilt, and
retested on the authorized physical device. Native cold start, current JS bundle, auth choice,
staff login entry, Android Back, force-stop/relaunch, and unauthenticated guarded deep links pass.
The original navigation-context launch blocker is closed. Authenticated role/module, 2FA, POS, and
operational-draft acceptance remains blocked only by missing approved credentials and seeded data.

Resolved defect report:
`.claude/logs/reviews/bug-task435-navigation-context_2026-07-29.md`.

## Environment and build evidence

- Device: realme RMX2063, Android 11 / API 30, arm64-v8a.
- ADB serial: `13cb6660`; state `device`, USB debugging authorized.
- Package: `com.shelfguard.mobile`.
- Installed version: `versionCode=1`, `versionName=1.0.0`, target SDK 36.
- Fresh install time: `2026-07-29 20:24:09`.
- Build profile: Android debug / Expo development client, current working tree.
- APK: `mobile/android/app/build/outputs/apk/debug/app-debug.apk`;
  262,091,007 bytes; timestamp `2026-07-29 20:13:29`.
- API configuration: `https://api.agrusystems.pp.ua:10054/api`.
- Metro: localhost port 8081 with `adb reverse tcp:8081 tcp:8081`.
- Test accounts/roles: none supplied; no credentials were used.
- Camera and microphone permissions remained denied; no phone data or unrelated app was erased.
- A final `pm clear com.shelfguard.mobile` discriminator was attempted, but realme/Oppo firmware
  rejected ADB shell with `SecurityException` for `CLEAR_APP_USER_DATA`; no data was cleared. The
  original install was already fresh (`pm list packages` returned no ShelfGuard package before
  installation), so the failure is not attributed to a pre-existing mobile session.

## Commands and results

- `android\gradlew.bat assembleDebug` — initial wrapper/dependency build exceeded the command
  timeout while native compilation continued; the resulting current-source APK was produced.
- `adb -s 13cb6660 install -r ...\app-debug.apk` — **PASS** (`Success`).
- `adb -s 13cb6660 shell am start ...` — **PASS**, native cold launch reported `COLD`,
  `MainActivity`, total time 2268 ms.
- `npm run type-check` — **PASS**.
- `npm run lint` — **PASS**, 0 errors / 13 existing warnings.
- `npm run test:ci` — **PASS**, 17 suites / 74 tests.

## Device QA matrix

| Flow | Result | Evidence / exact reason |
|---|---|---|
| Fresh install | pass | ADB streamed install returned `Success`. |
| Native cold start | pass | `MainActivity`, `LaunchState: COLD`, no Android process crash. |
| Current JS bundle load | fail | Reproducible missing navigation context error before usable UI. |
| Auth entry/UI | fail | Auth screen is not usable because rendering terminates first. |
| Staff login/logout/refresh | not tested | Launch-blocking defect; no credentials. |
| Consumer register/login/logout | not tested | Launch-blocking defect; no credentials. |
| Mobile 2FA entry/TOTP/recovery | not tested | Auth cannot be reached; no 2FA credentials. |
| Existing-session restoration | not tested | Fresh install had no existing session; render blocker prevents setup. |
| Role/module navigation and deep links | not tested | No authenticated role and render blocker. |
| Dashboard/stock/receipts | not tested | Authenticated UI unreachable. |
| Write-offs/transfers/production drafts | not tested | Authenticated forms and test data unreachable. |
| Durable POS force-stop restoration | not tested | POS, shift, and credentials unavailable. |
| Notifications/customers/schedules/marketplace/service desk/auto-service/AI | not tested | Authenticated UI unreachable. |
| Background/foreground | not tested | No stable application screen to preserve. |
| Android Back | not tested | Only development-client error UI was reachable. |
| Camera permission/scanning | not tested | Scanner unreachable; CAMERA remains `granted=false`. |
| Slow/lost network and API error UI | not tested | App UI cannot progress to API-backed screens. |
| Tenant/location/FEFO isolation | not tested | Requires credentials and seeded multi-tenant stock data. |

## Runtime defect evidence

Metro recorded:

- `Error: Couldn't find a navigation context. Have you wrapped your app with
  'NavigationContainer'?`
- origin: `expo-router/.../NavigationStateContext.js:43`;
- traversal: `react-native-css-interop/.../render-component.js`;
- rendered source frame: `app/(app)/schedules/index.tsx:123`;
- component stack: `SchedulesScreen` → `AppLayout` → `RootLayout`.

The error was reproduced after a force-stop and a new development-client connection. UI hierarchy
showed `Error loading app`; no login controls were available.

## Exit and next action

The critical launch defect is now fixed and device-verified. TASK-435 stays open as
`blocked_test_credentials`. Provide an approved staff account, a 2FA-enabled account and recovery
code, plus seeded tenant/store/location/product/POS-shift data. Then finish authenticated
TASK-437–444 acceptance.

## Post-fix device retest

- Fresh APK timestamp `2026-07-29 21:13:24`; install time `21:35:45`; SDK56 custom dev client.
- Current bundle/auth choice: **PASS**; staff login entry and Android Back: **PASS**.
- Force-stop/relaunch and reopen current server: **PASS**.
- Unauthenticated schedules/service-desk/POS/marketplace deep links: **PASS**, fail closed to auth.
- Navigation-context/FATAL error after fix: absent.
- Direct authenticated tab switching: not tested, no credentials.
- Packaging needed temporary CLI-only Gradle heap 8 GB; repo config was not changed.
- `adb install --no-streaming -r` succeeded after streaming install stalled.
- Expo Go 55 was not used; ShelfGuard uses its own SDK56 development client.

## Unauthenticated QA continuation

Executed without any real or historical credentials:

- Staff empty submit: validation appears for both fields — **PASS functionally**, but the shared
  `Required` text is English (low localization bug recorded separately).
- Staff malformed email: `Невірний email` — **PASS**.
- Obviously synthetic nonexistent staff credentials: actionable
  `Невірний email або пароль` — **PASS**; no account data was exposed.
- Consumer login entry: phone/password form, registration CTA, and staff-switch CTA render —
  **PASS**. Registration was not submitted and no account was created.
- Consumer empty submit: validation appears — **PASS functionally**, same English `Required`
  localization issue.
- Background/foreground from populated staff form: **PASS** (`LaunchState: HOT`, 125 ms); input
  and visible error state remained intact, with no fatal/navigation exception.
- Wi-Fi test: original `wifi_on=1`, `mobile_data=0`; Wi-Fi was disabled and restored to the exact
  original state. The visible login result remained the generic invalid-credentials error, so a
  distinct offline-error presentation is **inconclusive**, not claimed pass/fail.
- Camera permission entry: **not reachable unauthenticated**; route guards prevent scanner access.
- Documentation search found only historical/demo identities, not a currently approved TASK-435
  QA account. None were used.

Localization defect:
`.claude/logs/reviews/bug-task435-auth-required-localization_2026-07-29.md`.

## Engineering follow-up — 2026-07-29

The launch fix is prepared and TASK-435 is now `fix_ready_for_device_retest`. Diagnosis confirmed
that the missing navigation-context message was secondary to
`react-native-css-interop@0.2.5` recursively serializing Expo Router context while warning about a
late NativeWind shadow-variable upgrade. Conditional `shadow-sm` utilities were removed from all
equivalent tab controls. TypeScript, lint (0 errors/13 warnings), Jest (17 suites/74 tests), and an
Android bundle export pass. This does not change the failed device matrix above; it must be rerun
with a rebuilt APK before recording a launch pass.

The required-field localization defect found during unauthenticated QA is also
`fix_ready_for_device_retest`. Staff login, consumer login, and consumer registration now provide
explicit empty form values and share field-specific Ukrainian Zod schemas. Malformed-email and API
error behavior is preserved. TypeScript, lint (0 errors/13 warnings), and Jest
(18 suites/78 tests) pass; physical-device confirmation remains pending.

## Authenticated physical-device continuation — 2026-07-29

Approved QA accounts were used only in their login forms. Passwords, tokens, OTPs, and recovery
codes were not captured in this log. No sale, shift, receipt, transfer, write-off, registration, or
other business mutation was submitted.

| Role / flow | Result | Device evidence |
|---|---|---|
| `provider` | pass with guarded access | Login succeeds. Dashboard exposes only the shell/AI entry. Direct POS selection fails closed back to dashboard. |
| `enterprise_admin` | blocked at 2FA | Valid credentials reach the mobile six-digit TOTP screen with recovery-code option. No current OTP/recovery code was available. One accidental invalid code occurred after Android Back failed to leave the challenge; no retry was made. |
| `network_manager` | pass | Full dashboard and Dashboard/Stock/Scanner/POS/Receiving/More tabs render. |
| `store_manager` | partial / TASK-437 fail | Login and dashboard pass. HOT background/foreground preserves identity. Force-stop plus dev-client reconnect returns to unauthenticated auth choice. |
| `storekeeper` | partial pass | Dashboard/Scanner/More render. Profile exposes Stock, Receiving, Transfers, and Write-offs. POS was not shown in profile despite policy allowing it when the seeded `pos` module/tab is enabled; direct POS was not repeated after logout under the one-login safety constraint. |
| `merchandiser` | pass | Dashboard/Stock/Scanner/More render. Profile exposes Stock. Schedule opens successfully as allowed by the current tenant-staff policy; direct Write-offs deep link renders explicit `Доступ заборонено`. |
| Camera permission | pass | Android 11 permission prompt renders; “while using app” opens the live scanner screen. No code was scanned. |
| POS cart | not tested | No safe authenticated storekeeper/POS session remained after the one-login role pass; no sale or shift mutation was attempted. |
| Runtime stability | pass for exercised flows | No `FATAL EXCEPTION`, React Native JS fatal, or navigation-context crash in post-login logcat. Device firmware emits unrelated Oplus statistics permission warnings. |

The required-field localization fix is device-verified for staff login, consumer login, and
consumer registration. All empty required fields now show Ukrainian `Введіть ...` validation.

Open evidence:

- `.claude/logs/reviews/bug-task435-2fa-back-navigation_2026-07-29.md`
- `.claude/logs/reviews/bug-task437-force-stop-session-restoration_2026-07-29.md`
- `.claude/logs/reviews/bug-task435-auth-required-localization_2026-07-29.md`

TASK-435 is no longer blocked by general credentials or device access. Remaining acceptance is
blocked by a current approved OTP/recovery code, safe seeded POS context, and resolution or
release-build diagnosis of TASK-437 cold session restoration.

## Session and 2FA Back engineering follow-up — 2026-07-29

Both open device defects are `fix_ready_for_device_retest`. Session bootstrap now has explicit
hydration gating across auth/staff/consumer routes and waits for SecureStore plus `/auth/me` or
terminal cleanup before route selection. The 2FA challenge now treats hardware Back, Android IME
Back dismissal, header Back, and unmount as safe challenge cancellation. Automated verification:
TypeScript pass, lint 0 errors/13 existing warnings, Jest 20 suites/84 tests, Android bundle export
pass. The prior device results remain historical evidence; no new device pass is claimed.

## Offline bootstrap and owner-draft follow-up — 2026-07-29

The later offline cold-bootstrap and draft owner-switch defects are
`fix_ready_for_device_retest`. Retryable bootstrap failures now preserve SecureStore auth, clear
private query cache, withhold unverified identity/routes, and offer `/auth/me` retry; only terminal
auth failures clean the session. Operational drafts now use tenant+user+kind+scope storage keys
with owner-safe legacy migration. TypeScript passes, lint has 0 errors/13 existing warnings, Jest
passes (20 suites/90 tests), and Android export passes. No new physical-device pass is claimed.

TASK-444 same-owner restoration received a second fix after the first device retest still failed.
Incomplete form snapshots are no longer deleted merely because source/location fields are not yet
available, and the shared hook flushes its latest draft on background. Deterministic restart and
foreign-owner integration tests pass. Current Jest baseline is 20 suites/92 tests; physical retest
is still required.

## Current-source fix acceptance — 2026-07-29

Retested through the installed SDK 56 development client and current Metro bundle; no native rebuild
was required.

- Required-field Ukrainian localization remains **PASS**.
- Store-manager force-stop/reconnect: loading/bootstrap completes, then the same authenticated
  identity and dashboard restore — **PASS**.
- Store-manager HOT resume and logout/private-UI cleanup — **PASS**.
- Enterprise-admin 2FA hardware Back before input — safe cancellation to staff login, **PASS**.
- Enterprise-admin Back after focusing the OTP field — safe cancellation without submit, **PASS**.
- Exactly one user-approved recovery code was submitted and accepted — **PASS**. The code is
  consumed; its value is not recorded and reuse was not tested.
- Enterprise-admin dashboard, role tools, and logout/private-UI cleanup — **PASS**.
- Logcat contains no React Native fatal, navigation-context, or SecureStore exception. The observed
  Oplus/Google service permission warnings are firmware/service noise outside ShelfGuard.

TASK-437 is device-complete. TASK-438 recovery and Back acceptance pass; live TOTP remains
untested. Safe seeded POS/durable-mutation coverage also remains outside this retest.

## TASK-443/444 safe draft acceptance — 2026-07-29

- POS is reachable for manager and storekeeper, but no active shift exists. Cart/customer,
  force-stop restoration, and duplicate-submit acceptance are **not tested** because opening a
  shift was prohibited. No shift or sale was created.
- Transfer local-note autosave and offline banner/preservation pass without submit.
- User switching hides the manager draft from storekeeper, but opening the same draft kind as the
  foreign owner deletes the shared snapshot; returning to manager does not restore it — **FAIL**.
- A separate disposable transfer draft was explicitly deleted; cold restart confirms no test
  marker remains.
- Write-off draft requires a real scanned product/batch; production safe form/data was unavailable.
- Offline test restored exact connectivity: Wi-Fi ON, mobile data OFF.
- A force-stop immediately after restoring Wi-Fi returned to auth choice. This is recorded as an
  offline cold-bootstrap follow-up because network/API readiness was not proven before restart.

Evidence:

- `.claude/logs/reviews/bug-task444-owner-switch-deletes-draft_2026-07-29.md`
- `.claude/logs/reviews/bug-task437-offline-cold-bootstrap-logout_2026-07-29.md`

## Interrupted current-source isolation retest

The same-owner prerequisite still failed: a manager transfer-note marker did not restore after
force-stop/reconnect. Development-client bootstrap remained textless for about one minute before
returning to the authenticated dashboard; logcat had no React Native fatal/navigation/SecureStore
exception. Because this first assertion failed, storekeeper switching and offline-cold retry were
not repeated. No marker was visible afterward. Connectivity was never toggled and ended Wi-Fi ON /
mobile data OFF.

## Final TASK-444 transfer isolation acceptance

The incomplete transfer-note cycle now passes background preservation, same-owner cold restore,
storekeeper non-disclosure, manager-return restore, explicit delete, and cold absence. No transfer
was submitted and no marker remained. Development-client bootstrap reached authenticated dashboard
in about 64 seconds; no timeout/retry UI appeared at 15 seconds. Offline-cold bootstrap was not
rerun. Final connectivity remained Wi-Fi ON / mobile data OFF.

## TASK-437 controlled offline cold-start closure — 2026-08-01

The dedicated physical-device retest passes. Offline cold bootstrap preserved the authenticated
manager session behind a retry-safe Ukrainian screen and withheld private UI. After restoring
Wi-Fi and independently proving API-host reachability, Retry restored the same manager dashboard
without login. Focused logcat was clean and connectivity returned to Wi-Fi ON/mobile data OFF.

## TASK-438/443 prerequisite check — 2026-08-01

- Live TOTP remains not tested: no current six-digit authenticator value or TOTP seed was supplied.
  No code was guessed or submitted, and no recovery code was consumed.
- Manager POS was inspected read-only and still reports `Зміна не відкрита`. The Open
  shift action was not tapped; no shift, cart, sale, or payment mutation occurred.

## TASK-444 prerequisite check — 2026-08-01

- Write-off create opens and requires a real scanned product before a draftable line can be added.
  No product was scanned and no draft or server mutation was created.
- Production is absent from the manager's allowed navigation. A read-only guarded deep link renders
  `Модуль вимкнено` / `Цей модуль не активований для вашої компанії.`
  No production draft or mutation was created.
- One non-reproduced navigation observation showed the write-off detail error state
  `Не вдалося завантажити списання`. It is evidence only, not yet classified as a
  defect because the exact detail identifier/navigation path was not established.

## Read-only baseline continuation — 2026-08-01

Manager dashboard, stock, receipts, customers, Service Desk, schedules, and AI assistant screens
render without a fatal error. Stock, receipts, customers, tickets, and schedules expose valid empty
states, so their detail screens are not available without guessing identifiers. AI was not sent a
prompt. Android Back passed for the exercised screens. Marketplace remains visible but not opened;
auto-service is unavailable in this tenant context; notifications remain inconclusive because the
attempt opened the development-client menu. No create/update/delete/mark action occurred.

Marketplace follow-up passes for the visible list and an existing supplier detail/catalog.
Notifications pagination/refresh/badge remains incomplete in the development client.

Notifications static-route follow-up passes authenticated list rendering and the visible unread
count (`50`) with existing entries. No read-state mutation was performed. Pagination, pull-to-refresh,
and exact Back confirmation remain untested/inconclusive.
