# Handoff: TASK-330 backend → TASK-331 frontend (2FA UI + password hints + lockout UX)

**From:** backend-developer · **Date:** 2026-07-09
**Backend state:** merged-ready on `main` working tree; migration `20260709204440_AuthHardeningAnd2fa` auto-applies on API start. Build 0 errors, 685/685 tests.

## Login flow (changed)

`POST /api/auth/login` — body `{ "email", "password" }` (unchanged):

- 2FA **not** enabled (default): `200 { "accessToken": "...", "user": {...} }` + `refreshToken` HttpOnly cookie — unchanged.
- 2FA enabled: `200 { "requiresTwoFactor": true, "challengeToken": "<jwt>" }` — **no tokens, no cookie**. Challenge JWT lives 5 min.
- Bad credentials / locked account / inactive: `401 { "error": "Invalid email or password." }` — lockout is never revealed (5 wrong passwords or 2FA codes → 15 min lock).
- Rate limit: 10 req/min per IP → `429 { "error": "Too many requests. Try again later." }`.

## New endpoints

### POST /api/auth/2fa/verify  (anonymous, same 10/min limiter)
Body: `{ "challengeToken": "...", "code": "123456" }`
- `code` = 6-digit TOTP **or** a recovery code (`XXXX-XXXX`, case/dash-insensitive; consumed on use).
- Success: `200 { "accessToken", "user" }` + refresh cookie (identical shape to login success). Resets lockout counters.
- Wrong/replayed code: `401 { "error": "Invalid code." }` (counts toward the 5-failure lockout).
- Bad/expired challenge: `401 { "error": "Invalid or expired challenge token." }` → send the user back to step 1.

### POST /api/auth/2fa/setup  [Authorize]
No body. `200 { "secret": "<base32>", "otpauthUri": "otpauth://totp/ShelfGuard:<email>?secret=...&issuer=ShelfGuard" }`
- Renders as QR + manual secret. Secret is *pending* — 2FA stays off until enable.
- Calling again regenerates the pending secret. If already enabled: `400 { "error": "Two-factor authentication is already enabled." }` (must disable first).

### POST /api/auth/2fa/enable  [Authorize]
Body: `{ "code": "123456" }` (code from the authenticator against the pending secret).
- Success: `200 { "recoveryCodes": ["AB2C-3DEF", ...] }` — **8 codes, shown exactly once, never retrievable again** (only SHA256 hashes stored). UI must force copy/download before dismiss.
- Wrong code: `400 { "error": "Invalid code." }`. No pending setup: `400 { "error": "No pending two-factor setup. Call setup first." }`.

### POST /api/auth/2fa/disable  [Authorize]
Body: `{ "password": "...", "code": "123456" }` (code = TOTP or recovery code).
- Success: `204 No Content`. Errors: `400 { "error": "Invalid password." }` / `{ "error": "Invalid code." }` / `{ "error": "Two-factor authentication is not enabled." }`.

## AuthUserDto (extended)

`user.twoFactorEnabled: boolean` added (camelCase over the wire). Present in login/refresh/`GET /api/auth/me` responses. Impersonated sessions always report `false`. Use it to render the enable/disable state on the profile/security page.

## Password policy (frontend hints, TASK-331)

Applied to: invite user, change password, tenant onboarding, provider tenant-user create, provider team invite. Server errors (English, show as-is or map to UA):
- `Password must be at least 12 characters.`
- `Password must contain at least one letter.` / `...at least one digit.`
- `This password is too common. Choose a more unique password.`
- `Password must not contain your email address.`

`POST /api/auth/change-password` additionally **revokes all refresh tokens** — after a successful change the current session's next refresh returns 401, so either re-login the user or warn them.

## Rate limits summary

| Endpoint | Policy | Limit |
|---|---|---|
| POST /api/auth/login, /api/auth/2fa/verify | auth-login | 10/min per IP |
| POST /api/auth/refresh | auth-refresh | 30/min per IP |

429 body is always `{ "error": "Too many requests. Try again later." }`.

## Deviations from the task spec (deliberate)

1. DB columns are PascalCase (`FailedLoginAttempts`, `TotpSecret`, ...) — matches every existing `users` column; the spec's snake_case names would break project convention. Wire contract unaffected.
2. `2fa/disable` accepts a recovery code in `code` as well as TOTP (user who lost the device can still disable with password + recovery code). Contract shape unchanged.
3. Challenge JWT uses a dedicated audience (`Jwt:Audience + ":2fa"`), not just a purpose claim, so it can never be replayed as an API bearer token. Opaque to the frontend.
4. Failed 2FA attempts log activity `user.2fa_failed` (not `user.login_failed`) + `user.locked_out` when the lock trips — clearer audit trail, same counter.
