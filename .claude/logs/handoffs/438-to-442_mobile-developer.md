# TASK-438 → TASK-442 security handoff

Review the mobile-only 2FA login implementation from TASK-438.

## Security invariants implemented

- `challengeToken` is held only in Zustand memory and is absent from SecureStore.
- No challenge token or verification code is logged.
- Missing/malformed challenge and authenticated responses fail closed.
- `/auth/2fa/verify` `401` responses never enter the access-token refresh path.
- Route entry without a current in-memory challenge redirects to login.
- Successful authentication and navigation away clear the challenge.
- Mobile setup/enable/disable/recovery-code generation remains out of scope and web-only.

## Review targets

- `mobile/features/auth/store.ts`
- `mobile/features/auth/api/authApi.ts`
- `mobile/features/auth/hooks/useLogin.ts`
- `mobile/features/auth/types.ts`
- `mobile/features/auth/twoFactorCode.ts`
- `mobile/app/(auth)/two-factor.tsx`
- `mobile/lib/api-client.ts`

## Outstanding evidence

Automated checks pass (8 suites/30 tests), but Android live TOTP, challenge-expiry, Back-navigation,
and recovery-code one-time-use checks await the device/AVD required by TASK-435.
