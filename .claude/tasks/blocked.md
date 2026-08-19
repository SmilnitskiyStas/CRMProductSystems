# Blocked Tasks

Tasks that cannot proceed due to a blocker.

## TASK-462 — Limited offline-read UX rollout
**Status:** review_pending_device · **Agent:** mobile-developer + security-reviewer + qa-tester · **Updated:** 2026-08-01

Implementation and automated acceptance pass. Final closure is pending TASK-463 Android+iOS phone
acceptance for process death, reconnect, stale/hard-expired presentation, owner isolation, logout,
backup/storage pressure and accessibility. This is a device/security acceptance gate, not an
implementation defect; no offline mutation or additional cached family is authorized.

## TASK-446 — Mobile design-system foundation
**Status:** partial_device_pass / accessibility-and-login-smoke pending · **Agent:** mobile-developer + qa-tester · **Updated:** 2026-08-01

Implementation and automated acceptance are complete. Closure waits only for visual/device smoke
of staff login, dashboard, and customers on Android: safe area/keyboard, large font scaling,
TalkBack labels/order, 44 px touch targets, loading/error/empty/search/list states, pull-to-refresh,
Android Back, and confirmation that no css-interop navigation-context crash returned. No build was
installed and no credentials were used during implementation.

Device attempt reached the current bundle without css-interop/navigation regression, but the phone
could not route to the API (`Destination Host Unreachable`) despite Wi-Fi ON and a controlled
off/on retry. Resume after device network routing recovers; do not logout the retained session.

Network routing later recovered. Dashboard and Customers now pass safe-area, accessibility
label/touch-bound, empty/search/clear, Android Back, and css-interop regression checks. Remaining
acceptance is limited to the converted staff-login keyboard/validation/logout-login smoke, large
font (realme blocks `WRITE_SETTINGS`), and TalkBack. A final launch made Metro/ADB uncontrollable,
so QA stopped without logout, credential submission, app-data clearing, or business mutation.

## TASK-435 — Real-device mobile baseline QA
**Status:** in_progress / remaining external blockers · **Agent:** qa-tester · **Updated:** 2026-08-01

**Blocker:** A fresh current-source debug APK installs and native cold-starts on realme RMX2063
(Android 11/API30), but the JS bundle fails before usable authentication UI:
`Couldn't find a navigation context`, through `react-native-css-interop`, reported at
`app/(app)/schedules/index.tsx:123`.

**Evidence:** `.claude/logs/reviews/bug-task435-navigation-context_2026-07-29.md` and
`.claude/logs/reviews/2026-07-29_mobile-baseline.md`.

**Fix prepared:** Dynamic `shadow-sm` toggles that triggered the crashing
`react-native-css-interop` development warning serializer were removed from all equivalent mobile
tab controls. Static checks and Android bundle export pass. A fresh APK install and physical-device
launch are required before removing this entry. Full authenticated QA also requires test
credentials and seeded store/POS/warehouse data.

**Device retest:** PASS for the fixed launch path on a newly packaged/installed APK. Auth choice,
staff login entry, Back, force-stop/relaunch, and unauthenticated guarded deep links work; the
navigation-context defect is closed.

**Authenticated retest:** approved provider, network-manager, store-manager, storekeeper, and
merchandiser logins pass. Enterprise admin reaches mobile 2FA; no current OTP/recovery code was
available. Camera permission/scanner entry passes. Store-manager HOT restoration passes, but
force-stop plus development-client reconnect returns to auth choice.

**Fix device retest:** TASK-437 force-stop restoration and TASK-438 Back cancellation now pass.
Exactly one approved recovery code was accepted and consumed; its value is not recorded and was
not reused. Enterprise-admin navigation/logout and auth localization pass. Both reported defects
are device-closed.

**Current unblock:** provide a current approved TOTP for the remaining TOTP-specific acceptance and
safe seeded POS/module context for mutation/durability QA. Evidence:
`.claude/logs/reviews/bug-task435-2fa-back-navigation_2026-07-29.md` and
`.claude/logs/reviews/bug-task437-force-stop-session-restoration_2026-07-29.md`.

**Latest fix-ready follow-up:** transient offline/server bootstrap failures no longer terminate a
persisted session, and operational drafts are owner-namespaced so account switching cannot delete
another owner's record. Controlled offline cold-start/recovery and multi-owner draft restoration
still require physical retest.

The later same-owner TASK-444 failure also has a second fix ready: incomplete autosaved form
snapshots are accepted during restore and the latest snapshot is flushed on background. Physical
cold restore remains the acceptance gate.

**TASK-443/444 continuation:** POS durability remains blocked by no existing open shift. TASK-444
owner-switch restoration failed because a foreign-owner load deletes the shared draft key.
Offline banner/preservation and explicit discard pass; no test draft remains. A network-transition
cold-start session loss also needs controlled confirmation. Evidence:
`.claude/logs/reviews/bug-task444-owner-switch-deletes-draft_2026-07-29.md` and
`.claude/logs/reviews/bug-task437-offline-cold-bootstrap-logout_2026-07-29.md`.

Current-source follow-up did not clear these blockers: same-owner draft cold restore was blank,
and the offline-cold retry scenario was not rerun after that prerequisite failed. Connectivity
remained at its original Wi-Fi ON / mobile data OFF state.

**TASK-444 transfer update:** final focused device retest closes the transfer owner-isolation
defect. Remaining blockers are receipt-create contract, other operation-specific device coverage,
POS active-shift seed, live TOTP, and the separate offline-cold bootstrap follow-up.

**Pause:** user requested testing stop. App/data/device are retained; owned Metro PID 42044 and
ADB reverse 8082 were stopped/removed. Resume only from
`.claude/logs/reviews/2026-07-29_TASK-435-mobile-device-qa-pause-handoff.md`.

**Resumed 2026-08-01:** controlled TASK-437 offline cold bootstrap/retry passes and the related
defect is closed. Remaining external blockers are a current live TOTP, an already-open seeded POS
shift, receipt-create contract, and safe write-off/production fixtures.

**2026-08-01 prerequisite recheck:** manager POS still has no existing active shift. QA did not
open one. Live TOTP was not attempted without a current six-digit authenticator value.

Write-off draft testing additionally requires a safe real product/barcode fixture. Production is
disabled for the current manager tenant and requires an activated module plus safe recipe/component
fixtures. No mutation was attempted.

**Device transport at end of 2026-08-01 run:** ADB stopped responding after connectivity had been
restored and verified; Metro 8082 is stopped, but the task-owned reverse could not be queried or
removed. Reconnect/unlock the phone and remove only `tcp:8082` if it remains. This does not invalidate
the completed offline-cold pass.

**ADB recovery continuation:** dashboard and empty-state stock/receipts/customers/schedules/Service
Desk plus idle AI assistant pass read-only. Empty seed data blocks all corresponding detail flows.
Marketplace, notifications, and auto-service still require completion/available module context.

Marketplace is now device-covered list/detail. Notifications remains incomplete under the
development client, and auto-service is not offered by current tenant/module navigation.

Notifications list/unread count now pass through the authorized static route. Only pagination,
pull-to-refresh, and exact route Back confirmation remain for that screen.

**Offline-scope decision closed (TASK-445, 2026-08-01):** ADR-025 authorizes durable drafts plus
limited cached reads only. It does not unblock the external TASK-435 fixture/device items and does
not authorize queued/offline submits. Follow-up implementation is TASK-461..463; all business
operations continue to require online server revalidation.

**TASK-461 foundation ready (2026-08-01):** no implementation blocker remains. Device-visible
offline/stale UX is intentionally deferred to TASK-462, while Android+iOS process-death, backup,
storage-pressure and privacy acceptance remain TASK-463 scope; this is not authorization for any
offline mutation or full offline POS behavior.

## TASK-440 — Mobile release build/store readiness

**Status:** blocked_credentials_assets_builds · **Agent:** devops-engineer · **Updated:** 2026-08-01

Local EAS/Expo release configuration is ready. Closure is blocked by missing approved source
artwork (1024px app icon, Android adaptive foreground/background, splash logo/background, monochrome
notification icon), Apple distribution/App Store Connect access, Android upload signing/Play
Console access, store metadata/privacy declarations, and authorization to execute remote EAS
preview/production builds and install smoke tests. No credentials or secrets are committed.
Unblock: product/design supplies approved assets; release owner provisions EAS/Apple/Google access
and separately authorizes build/submit actions.

## TASK-260 — Resend email channel: верифікація домену agrusystems.pp.ua
**Status:** blocked · **Agent:** devops-engineer · **Updated:** 2026-06-19
**Blocker:** DNS-верифікація домену `agrusystems.pp.ua` в Resend ще не завершена (очікування propagation).

**Що вже зроблено:**
- Resend акаунт створено (stassmilnitskiy@gmail.com)
- API ключ додано в `.env` на prod-сервері (`RESEND_API_KEY` + `FROM_EMAIL=noreply@agrusystems.pp.ua`)
- Worker перезапущено з новими змінними
- Тестовий лист через `onboarding@resend.dev` — OK (API ключ валідний)
- Код у `worker/src/services/email.ts` готовий

**Що залишилось:**
1. Додати DNS-записи (SPF, DKIM, DMARC) у DNS-панелі → [resend.com/domains](https://resend.com/domains)
2. Натиснути Verify у Resend
3. Протестувати відправку від `noreply@agrusystems.pp.ua`
4. Перевірити worker logs що email-канал активний

**Unblock:** як тільки домен верифікується — повідомити, email-канал запрацює автоматично.

**Новий залежний (2026-07-30, TASK-455..459; дизайн відтоді замінено — TASK-464..466,
2026-08-04, ADR-026 — див. `decisions.md`):** forgot/reset-password flow теж використовує email
як основний канал доставки — зараз доставляє тимчасовий пароль напряму (раніше доставляв лінк
відновлення, ADR-024, superseded) — і так само чекає на цей DNS-blocker, щоб email-канал став
видимим реальним користувачам. Telegram-fallback (для вже прив'язаних акаунтів) від TASK-260 не
залежить і працює вже сьогодні. Код готовий і чекає з обох сторін — окремого known-issues запису
не створено, це не нова проблема.

## TASK-463 — Cross-platform offline security and device acceptance

**Status:** fix_ready_for_device_retest / ios_device_build_pending · **Agent:** mobile-developer + security-reviewer + qa-tester · **Updated:** 2026-08-01

**Source fix ready:** strict minimal-snapshot offline shell now addresses the Android process-death
failure. Automated checks pass. Work is paused by user request; remaining blockers are Android
device retest and unavailable iOS build/device.

No open Android code/config security finding remains; physical behavior is QA-owned. iOS execution is
blocked because this Windows workspace has no generated iOS build or device. AsyncStorage backup
exclusion, Keychain behavior, process death and device transfer must be proven on iOS before release.

Android physical QA is also blocked until a current-source APK can be built and loaded. The installed
July 29 APK predates TASK-461..463; the current Metro manifest returns HTTP 500
`UnexpectedServerError`, while `assembleDebug` failed on locked generated Android resources and a
missing react-native-worklets CMake reply. Full evidence matrix:
`.claude/logs/reviews/2026-08-01_TASK-463-android-device-qa.md`.

Build/install recovery is complete. The remaining Android blocker is the fresh APK runtime: even
after Metro cache reset reaches running state, the dev client reports `Failed to download remote
update`, Metro returns manifest `UnexpectedServerError`, and logcat shows missing
`expo.modules.splashscreen.SplashScreenManager`. Mobile runtime fix/rebuild is required before QA.

**Build recovery follow-up:** the stale/locked generated outputs were safely cleared and those two
errors did not recur. A clean native rebuild remained active beyond the bounded ~9-minute window,
was stopped cleanly, and produced no APK. Unblock now requires one longer idle local build window;
after a successful artifact, verify hash/native backup config, install preserving data, and resume
device QA. This is no longer diagnosed as a source-code failure.

**Resolved 2026-08-01:** the authorized incremental build completed in 497 seconds and the fresh
APK installed successfully with application data preserved. Remove the Android current-source
build/runtime item from TASK-463 blockers. Remaining TASK-463 blockers are execution of the Android
device matrix and separate iOS build/device/security acceptance.

**Runtime defect resolved 2026-08-01:** the missing SDK56 `expo-splash-screen` native dependency was
added at `~56.0.14`, autolinking now includes `SplashScreenModule`, and a newly built APK was installed
with application data preserved. Native cold launch no longer emits the missing
`SplashScreenManager`, manifest-parser, or fatal signature. Android is no longer blocked on this
runtime defect; QA must still run a current-source Metro/UI smoke and the remaining device matrix.
