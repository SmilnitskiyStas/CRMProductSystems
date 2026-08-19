# TASK-438: Mobile two-factor authentication

**Date:** 2026-07-29
**Agent:** mobile-developer (Codex main session)
**Status:** device_pass_recovery_totp_pending

## Product boundary

Mobile supports sign-in for staff accounts whose TOTP 2FA was configured through the web profile.
The mobile app does not set up, enable, disable, or regenerate 2FA/recovery codes in this task.

## What was implemented

- Password login recognizes `{ requiresTwoFactor: true, challengeToken }` without creating a
  partial authenticated session.
- Added a dedicated Ukrainian 2FA route and screen.
- Added six-digit TOTP entry with numeric keyboard and one-time-code autofill metadata.
- Added a recovery-code mode with normalization and validation for `XXXX-XXXX`.
- Added `POST /auth/2fa/verify` API mapping and normal authenticated-session activation on success.
- Invalid code, expired challenge, rate limit, and network failure have actionable Ukrainian text.
- Direct navigation to the verification route without a challenge redirects to login.
- Leaving the verification route, including native Back navigation, clears challenge state.
- The challenge token and email exist only in the non-persisted Zustand store; no SecureStore or
  logging path was added.
- `/auth/2fa/verify` is classified as public auth traffic in the Axios interceptor. Its expected
  `401` cannot start refresh, loop, clear the challenge, or terminate another session.

## Files changed

- `mobile/app/(auth)/_layout.tsx`
- `mobile/app/(auth)/login.tsx`
- `mobile/app/(auth)/two-factor.tsx`
- `mobile/features/auth/types.ts`
- `mobile/features/auth/api/authApi.ts`
- `mobile/features/auth/hooks/useLogin.ts`
- `mobile/features/auth/store.ts`
- `mobile/features/auth/twoFactorCode.ts`
- `mobile/lib/api-client.ts`
- `mobile/features/auth/api/__tests__/authApi.test.ts`
- `mobile/features/auth/__tests__/store.test.ts`
- `mobile/features/auth/__tests__/twoFactorCode.test.ts`
- `mobile/lib/__tests__/api-client.test.ts`

## Verification

- `npx tsc --noEmit`: PASS
- `npm run lint`: PASS — 0 errors, 19 pre-existing recorded warnings
- `npx jest --runInBand --watch=false`: PASS — 8 suites, 30 tests
- Focused ESLint for auth/API files: PASS — 0 errors, one existing Axios import warning
- Android live TOTP test: NOT RUN — TASK-435 has no connected device/AVD
- Android live recovery-code test: NOT RUN — TASK-435 has no connected device/AVD

Automated coverage includes successful password response mapping, challenge response validation,
successful verify response mapping, TOTP/recovery normalization, memory-only challenge behavior,
challenge cleanup through auth/session operations, and invalid-code `401` refresh exclusion.

## Scoped security review

No critical/high finding was identified in the implemented mobile boundary:

- challenge has no persistence or logging sink;
- malformed success/challenge payloads do not create authenticated state;
- route access requires an in-memory challenge;
- expected 2FA `401` is isolated from refresh/session cleanup;
- authentication success uses the existing SecureStore/session boundary;
- navigation away clears the short-lived challenge.

TASK-442 must still include this flow in the full mobile security review.

## Remaining acceptance

The code implementation is complete, but the task is not marked `done`. When TASK-435 is unblocked:

1. use a web-configured 2FA staff account on a fresh Android build;
2. verify a current authenticator TOTP signs in;
3. verify a wrong code stays on the screen with the Ukrainian error;
4. verify challenge expiry requires a new password login;
5. verify one recovery code signs in and cannot be reused;
6. verify Back and app restart cannot reuse the old challenge.

After those checks pass, move TASK-438 from `review_pending_device` to `done`.
