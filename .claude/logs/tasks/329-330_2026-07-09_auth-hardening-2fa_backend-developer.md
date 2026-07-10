# TASK-329 + TASK-330 — Auth hardening + 2FA TOTP (backend)

**Date:** 2026-07-09 · **Agent:** backend-developer · **Status:** done
**Source:** security audit `.claude/logs/reviews/2026-07-09_security-audit_auth-infra.md`

## TASK-329 — Auth hardening core

- **Rate limiting** (built-in .NET 8, no new packages): `Program.cs` — policy
  `auth-login` (10 req/min per IP) on POST `/api/auth/login` + `/api/auth/2fa/verify`,
  `auth-refresh` (30 req/min per IP) on POST `/api/auth/refresh`. 429 body:
  `{ "error": "Too many requests. Try again later." }`. `UseForwardedHeaders`
  (XForwardedFor|XForwardedProto, KnownNetworks/KnownProxies cleared) runs first
  so the partition key is the real client IP behind nginx.
- **Account lockout**: `users.FailedLoginAttempts` (int, default 0) +
  `users.LockoutUntil` (timestamptz null). 5 failures → 15 min lock, counter reset;
  locked login returns generic "Invalid email or password." without revealing lockout
  (hash still verified for constant-ish timing). Activity: `user.login_failed`,
  `user.locked_out` (with IP); unknown email → ILogger warning only, no DB write.
  Wrong 2FA codes share the counter (`user.2fa_failed`).
- **Password policy**: new `ShelfGuard.Application/Common/PasswordValidator.cs` —
  min 12 chars, ≥1 letter, ≥1 digit, ~100-entry common-password blocklist
  (case-insensitive, incl. ukrainian-keyboard classics), rejects email local-part
  inside password. Applied in `UserService.InviteAsync` + `ChangePasswordAsync`,
  `TenantAdminService.CreateTenantAsync`, `ProviderService.CreateTenantUserAsync`,
  `ProviderTeamService.InviteMemberAsync` (auto-generated fallback password is now
  crypto-random 16 chars and policy-compliant). Existing hashes unaffected.
- **Revoke sessions on password change**: `IRefreshTokenRepository.RevokeAllForUserAsync`
  called from `UserService.ChangePasswordAsync` and the `ProviderTeamService`
  reactivation path (only flows that reset a password; no other admin reset exists).
- **Refresh reuse detection**: `AuthService.RefreshAsync` fetches by hash including
  revoked (`GetByHashAsync`); presenting a revoked/rotated token → all active tokens
  of that user revoked + `auth.refresh_reuse_detected` activity + generic 401.
- **Security headers** middleware in `Program.cs`: nosniff, X-Frame-Options DENY,
  Referrer-Policy no-referrer, Permissions-Policy camera/mic/geo off.

## TASK-330 — 2FA TOTP (opt-in per user)

- Package `Otp.NET` 1.4.0 (MIT) in Infrastructure only; `ITotpService` in Application,
  `TotpService` impl (30s step, 6 digits, ±1 timestep window).
- Schema: `users.TotpSecret` (text null), `TotpEnabled` (bool default false),
  `TotpRecoveryCodes` (jsonb — SHA256 hashes, explicit JSON conversion like
  `Permissions`, EnableDynamicJson not required), `TotpLastTimestep` (bigint null,
  anti-replay).
- Endpoints (see handoff `.claude/logs/handoffs/330-backend-to-frontend.md` for the
  exact contract): login returns `{ requiresTwoFactor, challengeToken }` when enabled;
  `/api/auth/2fa/verify|setup|enable|disable`. Challenge JWT: 5 min, purpose="2fa",
  **dedicated audience** (`Jwt:Audience + ":2fa"`) so it can never pass bearer auth.
- `AuthUserDto.TwoFactorEnabled` added (impersonation path in `AuthController.Me`
  passes false).

## Migration

`20260709204440_AuthHardeningAnd2fa` — additive only, 6 columns on `users`
(PascalCase per existing project convention, not snake_case). Applied + verified
against the local dev DB.

## Verification

- `dotnet build` — 0 errors; `dotnet test` — **685/685 green** (was 645; +40 new:
  lockout flow, PasswordValidator, refresh-reuse revocation, TOTP verify/replay/
  recovery-code consumption via real Otp.NET codes, change-password revocation).
- Live smoke test against local API + Postgres: 401 `{error}` shape, 10th login in
  a minute → 429 with correct body, `/2fa/verify` shares the login limiter and
  returns 401 "Invalid or expired challenge token." for garbage tokens, all four
  security headers present.

## Not touched (parallel owners)

docker-compose*/infra/nginx (TASK-332, devops), frontend 2FA UI (TASK-331).
