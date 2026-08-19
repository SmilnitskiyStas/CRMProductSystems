# TASK-435 defect — Android Back does not leave staff 2FA challenge

**Date:** 2026-07-29  
**Device:** realme RMX2063, Android 11 / API 30  
**Build:** `com.shelfguard.mobile` 1.0.0 (SDK 56 development client)  
**Severity:** medium  
**Status:** resolved / device verified

## Reproduction

1. Open staff login.
2. Submit valid credentials for the approved 2FA-enabled `enterprise_admin` account.
3. Observe the mobile 2FA screen with a six-digit authenticator-code field and recovery-code CTA.
4. Send one Android Back action while the 2FA screen is visible.

## Actual

The 2FA screen remains active. Subsequent ADB text intended for the returned login form was accepted
by the still-active challenge screen and caused one invalid six-digit submission. Testing stopped
immediately; no second challenge attempt was made.

## Expected

Back must safely return to staff login (or present an explicit cancel action), clear the in-memory
challenge, and make it impossible for input intended for the login screen to reach the OTP form.

## Acceptance note

The positive TASK-438 finding remains valid: mobile 2FA is implemented and the previous
“2FA is not supported in the mobile app” result is no longer reproduced. Completing TOTP and
recovery-code acceptance still requires a current OTP or approved recovery code.

## Fix retest — 2026-07-29

- Hardware Back before entering a code returned to staff login and cleared the challenge — **PASS**.
- After focusing the OTP field, the first Back action cancelled the challenge and returned to
  staff login without verification submission or error — **PASS**.
- A fresh challenge followed by exactly one approved recovery-code verification signed in the
  enterprise administrator — **PASS**.
- Enterprise-admin dashboard and role tools rendered; logout removed private UI — **PASS**.

One approved single-use recovery code was consumed. Its value is intentionally not recorded.
No invalid-code or reuse attempt was performed.

## Fix prepared — 2026-07-29

The screen now owns focused Back handling. Hardware Back explicitly clears the memory-only
challenge, resets verification state, and replaces the route with staff login. On Android, an open
IME can consume the first hardware Back event; `keyboardDidHide` is therefore handled as the same
explicit cancellation so one device Back action cannot leave a live challenge behind. The visible
header Back uses the same cancellation boundary, and focus cleanup clears the challenge on every
unmount/navigation path.

A focused regression test verifies cancellation never submits verification and performs all three
safe actions. Status is `fix_ready_for_device_retest`; physical Back/header Back and successful
TOTP/recovery navigation still require device verification.
