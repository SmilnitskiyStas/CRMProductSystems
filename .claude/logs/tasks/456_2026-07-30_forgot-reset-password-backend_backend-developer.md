# TASK-456: Forgot/reset-password — business logic + API + worker (backend)

**Agent:** backend-developer
**Date:** 2026-07-30
**Status:** done — build 0 warnings/0 errors, tests 1220/1220 (was 1213, +7), worker tsc/build clean. No blocker.

## Context

Part B of `C:\Users\stass\.claude\plans\reflective-churning-quail.md` — TASK-455 (database-engineer)
already shipped the schema (`PasswordResetToken`, `IPasswordResetTokenRepository`, migration,
fail-open RLS). This task adds `AuthService.ForgotPasswordAsync`/`ResetPasswordAsync`, the two new
`/api/auth` endpoints, a rate-limit policy, env plumbing, a `NotificationService` allowlist entry,
and the worker's `notification-dispatch.job.ts` formatting for the new outbox event type.

## Done

### AuthService (`Features/Auth/AuthService.cs`, `IAuthService.cs`)
- Constructor gained `IPasswordResetTokenRepository` + `INotificationRepository` (both already
  DI-registered — no new wiring needed). `_frontendBaseUrl` read via
  `Environment.GetEnvironmentVariable("Frontend__BaseUrl") ?? "http://localhost:3000"` — copied
  `TelegramLinkService.cs:26`'s exact comment/pattern (confirmed `ShelfGuard.Application.csproj`
  still has no `Microsoft.Extensions.Configuration` reference).
- `ForgotPasswordAsync(email, ipAddress, ct)`: unknown/inactive email → warning log only, no DB
  write (same no-enumeration posture as `LoginAsync`). Known active user →
  `InvalidateActiveTokensAsync` (bulk, commits immediately) → raw token via
  `Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))` (same generation approach as
  `JwtService.GenerateRefreshToken()`'s raw half, not a call to that method) → hash via
  `_jwt.HashToken` → `PasswordResetToken.Create(...)` (30 min TTL) → `resetUrl =
  "{_frontendBaseUrl}/reset-password?token={Uri.EscapeDataString(rawToken)}"` (added
  `Uri.EscapeDataString` — base64's `+`/`/`/`=` aren't query-string-safe as-is; only changes how the
  token is embedded in the URL, not how it's generated/hashed) → `ActivityLog
  "user.password_reset_requested"` → targeted outbox row (`EnqueueAsync`, `UserId=user.Id`,
  `Channel="system"`, `EventType="auth.password_reset_requested"`, `Payload={resetUrl,
  expiresInMinutes:30}` via `JsonSerializer.Serialize`, same shape as `ReceiptService`/
  `SupplierAgreementService`/`SupplierChatService`'s existing outbox writers).
  **Ordering note (checked whether TASK-370 batching applies, as the brief asked):**
  `NotificationRepository.EnqueueAsync` calls its own `SaveChangesAsync` internally (unlike
  `ActivityLogRepository.LogAsync`, which only stages) — so `EnqueueAsync` is called LAST, after the
  token `AddAsync` and the `ActivityLog` write, so its own commit flushes all three in one round
  trip. A literal single shared `SaveChangesAsync()` call at the very end isn't reachable here since
  `EnqueueAsync` owns its own — this reordering gets the same effect.
- `ResetPasswordAsync(rawToken, newPassword, ct) → string? error`: hash → `GetActiveByHashAsync`
  (null → generic `"Invalid or expired reset link."`) → owner lookup (not found/inactive → same
  generic text, per brief) → `PasswordValidator.Validate` (its message returned verbatim on
  failure) → `user.ChangePassword(_passwordHasher.Hash(...))` → `user.ResetLockout()` →
  `_users.Update(user)` → `token.MarkUsed()` (tracked entity — no `Update` method exists on
  `IPasswordResetTokenRepository` by design, per TASK-455) → `_refreshTokens.RevokeAllForUserAsync`
  (deferred, same call `UserService.ChangePasswordAsync` uses) → `ActivityLog
  "user.password_reset_completed"` → one `_passwordResetTokens.SaveChangesAsync(ct)` (genuinely
  single round trip — none of these calls self-commit).
- Both new `ActivityLog` writes use `TenantId = user.TenantId` unconditionally (nullable, no
  `if (user.TenantId.HasValue)` guard) — matches this file's OWN existing convention
  (`IssueTokensAsync`/`RegisterFailedAttemptAsync`), not `UserService.ChangePasswordAsync`'s guarded
  style; deliberate, since I was editing this file.

### Controller + DTOs
`Dtos/AuthDtos.cs`: `ForgotPasswordRequest(string Email)`, `ResetPasswordRequest(string Token,
string NewPassword)`. Exact new contract — see "For TASK-457" below.

### Rate limiting (`Program.cs`)
New named policy `"auth-forgot-password"`: 5 req/min per client IP, `QueueLimit=0` — byte-for-byte
shape of the existing `"public-leads"` policy (this endpoint sends a real notification per request,
same cost class).

### Env / compose
- `.env.staging.example` → `Frontend__BaseUrl=http://localhost:3101` (next to `Cors__Origins`).
- `.env.production.example` → `Frontend__BaseUrl=https://agrusystems.pp.ua`.
- `docker-compose.staging.yml` / `docker-compose.production.yml` → `api` service env gained
  `Frontend__BaseUrl: ${Frontend__BaseUrl:-http://localhost:3101}` / `:-http://localhost:3000}`.
  **Real (non-`.example`) staging/prod `.env` files still need this var set at the next deploy** —
  not done here, deploy-time step, out of scope.
- Local dev (`docker-compose.yml`, plain `dotnet run`) needs no change — the code-side default
  already covers it.

### NotificationService allowlist
`ValidEventTypes` gained `"auth.password_reset_requested"` — not load-bearing (the outbox insert
path bypasses this check; only `SendTestAsync`/`UpsertSettingAsync` validate against it), added for
consistency per brief.

### Worker (`worker/src/jobs/notification-dispatch.job.ts`)
- `TARGETED_EVENT_CHANNELS["auth.password_reset_requested"] = ["email", "telegram"]` — no `"push"`,
  deliberate (push isn't implemented at all yet — brief's explicit instruction).
- New `formatText`/`formatEmail` branches parsing `row.payload` as `{resetUrl, expiresInMinutes}`
  (`pg` auto-parses jsonb, no `JSON.parse` needed) — real clickable link (`<a href="...">`, Telegram
  `parse_mode: "HTML"` already supports it), subject `"[ShelfGuard] Відновлення пароля"`. Rest of
  the pipeline (`dispatchTargeted`/`deliver`/`logNotifications`) untouched — already
  event-type-agnostic.

### Tests
7 new `AuthServiceTests.cs` cases (NSubstitute): unknown/inactive → zero side effects (no
`AddAsync`/`EnqueueAsync`); valid email → token `AddAsync`'d + outbox `EnqueueAsync`'d with the
right `EventType`/`Channel`/`Payload` + `ActivityLog`; invalid/expired token → generic error; owner
not found/inactive → same generic error; weak new password → its own message, token NOT marked
used, refresh tokens NOT revoked; happy path → password hash swapped, lockout cleared, token
`UsedAt` set, `RevokeAllForUserAsync` called once, completion `ActivityLog` logged, one
`SaveChangesAsync`. Also updated the 3 other test files that construct `AuthService` directly
(`AuthServiceTabsTests.cs`, `TwoFactorAuthTests.cs`, `AuthServiceCapabilitiesTests.cs`) for the two
new constructor params — none of those exercise the new methods, they just needed to keep
compiling.

## Verification

**Found and resolved before writing any code — anonymous-connection RLS risk.** Both new methods
run on `[AllowAnonymous]` endpoints — the same "no `app.tenant_id` set yet" connection state
TASK-455 built `password_reset_tokens`'s fail-open policy for. `activity_logs`/`notification_queue`
needed the same tolerance for this flow's INSERTs, and `users`/`refresh_tokens` for
`ResetPasswordAsync`'s UPDATEs. Read the live migration SQL first
(`20260716120000_FixActivityLogsInsertUnderFailClosedRls.cs` — `activity_logs`/`notification_queue`
both already carry a permissive `WITH CHECK (NULLIF(current_setting('app.tenant_id',true),'') IS
NULL OR ...)`, added for exactly this class of anonymous-write problem), then **live-verified
directly** against the real non-superuser `shelfguard_app_dev` role (`crmproductsystems-postgres-1`
via `docker exec ... psql`) inside a transaction ending in `ROLLBACK`: `RESET
app.tenant_id/app.role/app.user_id/app.consumer_account_id` (the exact anonymous-request state
`TenantConnectionInterceptor` sets), then INSERT into all 3 tables (`password_reset_tokens`,
`activity_logs`, `notification_queue`) and UPDATE all 3 (`password_reset_tokens` MarkUsed, `users`
ChangePassword/ResetLockout, `refresh_tokens` RevokeAllForUserAsync) — all 6 operations succeeded
under real RLS; dev DB confirmed clean afterward (0 residual rows). This was the one real risk in
this task — a wrong assumption here would have meant a 500 on the very first real forgot-password
attempt in production.

- `dotnet build` — 0 warnings, 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`, not touched by this task).
- `dotnet test` — **1220/1220 passed**, 0 skipped (was 1213 from TASK-455; +7 new).
- Worker: `npx tsc --noEmit` and `npm run build` both clean, 0 errors. Docker worker container not
  rebuilt/restarted — not needed given the clean local compile; brief allowed skipping this.

## Contract for TASK-457 (frontend-developer) — read this, don't guess

```
POST /api/auth/forgot-password   [public, rate limit 5/min per IP]
  Body: { email: string }
  204: always, regardless of whether the email exists/is active — do not branch UI copy on
       anything in the response; show the same "check your email" message unconditionally.

POST /api/auth/reset-password    [public, rate limit 10/min per IP — shares "auth-login"]
  Body: { token: string, newPassword: string }
  204: success
  400: { error: string } — either the generic "Invalid or expired reset link." (bad/expired/used
       token, or its owner account no longer exists/is inactive — these are NOT distinguished) or
       a PasswordValidator policy message in English shown as-is (same convention as
       change-password's 400 body) — 12+ chars, needs a letter + a digit, rejects ~100 common
       passwords and anything containing the account's email local-part.
```
Reset link shape the email/Telegram message carries (frontend doesn't build this — just reads
`?token=` off the URL): `{Frontend__BaseUrl}/reset-password?token={urlEncodedRawToken}`. Token is
single-use and expires in 30 minutes.

## Known dependency (not a blocker for this task)

Email channel for `auth.password_reset_requested` won't actually be visible to real users until
TASK-260 (Resend DNS verification) unblocks — same standing dependency already documented for
`weekly-report`. Telegram channel works today for any user who has already linked their account.

## Not in scope (per brief)

- `frontend/` (TASK-457).
- `.claude/docs/known-issues.md` / `decisions.md` / `api-contracts.md` (TASK-459).
- Real (non-`.example`) staging/prod `.env` files — flagged above as a deploy-time step.

## Files

Modified:
- `backend/ShelfGuard.Application/Features/Auth/AuthService.cs`
- `backend/ShelfGuard.Application/Features/Auth/IAuthService.cs`
- `backend/ShelfGuard.Application/Features/Auth/Dtos/AuthDtos.cs`
- `backend/ShelfGuard.Api/Controllers/AuthController.cs`
- `backend/ShelfGuard.Api/Program.cs`
- `backend/ShelfGuard.Application/Features/Notifications/NotificationService.cs`
- `backend/ShelfGuard.Tests/Auth/AuthServiceTests.cs`
- `backend/ShelfGuard.Tests/Auth/AuthServiceTabsTests.cs`
- `backend/ShelfGuard.Tests/Auth/TwoFactorAuthTests.cs`
- `backend/ShelfGuard.Tests/Auth/AuthServiceCapabilitiesTests.cs`
- `worker/src/jobs/notification-dispatch.job.ts`
- `.env.staging.example`
- `.env.production.example`
- `docker-compose.staging.yml`
- `docker-compose.production.yml`

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
