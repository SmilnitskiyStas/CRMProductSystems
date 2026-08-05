# TASK-465: Temporary-password forgot-password — backend logic (AuthService rewrite)

**Agent:** backend-developer
**Date:** 2026-08-04
**Status:** done — build 0 warnings/0 errors, tests 1220/1220 (was 1221, net -1: removed 8
old link/token tests, added 6 new + 1 in `UserServicePasswordTests.cs`), worker
`tsc --noEmit` clean.

## Numbering note

Per the brief: real date is 2026-08-04 (not 2026-07-30). This task is **465**, not 462 — the
brief it superseded got renumbered to 464 (database-engineer) after TASK-461..463 were taken by
an unrelated, in-progress mobile feature. Confirmed 465 is still free right before writing this
log (`.claude/logs/tasks/` max was 464; `current.md`'s own `## TASK-` headers max was 464 too).

## Context

TASK-464 (database-engineer) dropped `password_reset_tokens`/`PasswordResetToken`/
`IPasswordResetTokenRepository` and added `User.TempPasswordExpiresAt` +
`HasActiveTempPassword`/`SetTempPasswordExpiry`/`ClearTempPasswordExpiry`, leaving
`dotnet build` failing on 2 `CS0246`s in `AuthService.cs` by design — this task's job was to
rewrite `AuthService`/`AuthController`/DTOs/worker/tests for the new temporary-password design
(user receives a usable password directly, valid 3h, no link/token/second step) and get the
build green again.

## Done

### `AuthService.cs` / `IAuthService.cs`
- Constructor: removed `IPasswordResetTokenRepository` param/field, and the now-unused
  `_frontendBaseUrl` field (no URL is built anymore — was only used for the deleted `resetUrl`).
- `ForgotPasswordAsync`: unknown/inactive email → warning log, no DB write (unchanged posture).
  Known active user → generates a temp password (`GenerateTempPassword()`), `user.ChangePassword`
  + `user.SetTempPasswordExpiry(UtcNow.AddHours(3))`, **commits immediately on its own**
  (`_users.SaveChangesAsync`) before the activity log / outbox notification — the credential
  change itself must be durable independent of whether logging/notification succeeds. Then stages
  an `ActivityLog` (`user.password_reset_requested`, unchanged action name) and calls
  `_notifications.EnqueueAsync` last (self-commits, flushing the staged log in the same round
  trip — same idiom TASK-456 established). Outbox payload: `{ tempPassword, expiresInMinutes: 180 }`,
  event type unchanged (`auth.password_reset_requested`, worker already keys off it).
- `ResetPasswordAsync` deleted entirely (interface + implementation) — no second step in the new
  design.
- `GenerateTempPassword()`: `RandomNumberGenerator`-backed, 14 chars, alphabet excludes visually
  ambiguous `0/O/1/I/l`. Letter and digit classes are **constructively guaranteed** — one char
  drawn from a letters-only pool, one from a digits-only pool, rest from the combined pool, then
  an unbiased Fisher–Yates shuffle so the guaranteed positions aren't predictable — not left to
  chance, so it can never fail `PasswordValidator.Validate`'s letter+digit requirement.
- `LoginAsync`: after a successful hash match (never on a mismatch — that stays the generic
  error, no new timing/branch signal), checks
  `user.TempPasswordExpiresAt.HasValue && !user.HasActiveTempPassword` → returns a specific error
  (`"Temporary password has expired. Please request a new one."`), ordered before the TOTP
  challenge branch. A live temp password or a normal permanent password both fall through to the
  existing success path unchanged.
- `ToDto`: now also maps `PasswordIsTemporary`/`TemporaryPasswordExpiresAt` from
  `u.HasActiveTempPassword`/`u.TempPasswordExpiresAt` — since every mint site (login, 2FA verify,
  refresh) and `GetCurrentUserAsync` share this one mapper, the flag is correct and fresh
  everywhere without extra plumbing, and self-clears on its own once the temp password is
  changed or simply expires.

### DTOs (`Dtos/AuthDtos.cs`)
- `AuthUserDto` gained `bool PasswordIsTemporary = false` and
  `DateTime? TemporaryPasswordExpiresAt = null` at the end (both defaulted — no existing
  positional call site breaks, incl. `AuthController.Me()`'s impersonation-branch DTO literal).
- `ResetPasswordRequest` deleted. `ForgotPasswordRequest` unchanged.

### `AuthController.cs`
- `POST /api/auth/reset-password` endpoint removed entirely.
- `POST /api/auth/forgot-password` unchanged (same `"auth-forgot-password"` rate limit, 5/min/IP,
  still always 204) — only its XML doc comment updated.

### `UserService.ChangePasswordAsync`
- Added `user.ClearTempPasswordExpiry()` right after `user.ChangePassword(...)` — the one place a
  user "takes control" of a temp password. No-op when the password wasn't temporary.

### Worker (`worker/src/jobs/notification-dispatch.job.ts`)
- `PasswordResetPayload` type renamed `TempPasswordPayload` → `{ tempPassword?, expiresInMinutes? }`.
- `formatText`/`formatEmail` for `auth.password_reset_requested`: replaced the resetUrl-link
  copy with the temp password itself, shown in `<code>` (Telegram) / monospace-styled `<p>`
  (email), plus new copy ("ваш тимчасовий пароль: XXXX, дійсний N хвилин, увійдіть і встановіть
  новий"). Subject changed to "Тимчасовий пароль".
- **Security-critical redaction carried forward**: `dispatchTargeted`'s pre-`logNotifications()`
  redaction (originally TASK-460's HIGH fix for the leaking `resetUrl`) now redacts
  `tempPassword` the same way — `notification_queue`/`GET /api/notifications/history` never sees
  the live credential, only `{ expiresInMinutes }`. The real value is still used earlier in the
  same function for the actual send. This is if anything a bigger risk than the old link (a
  directly-usable password vs. a single-purpose link), so kept the same defense-in-depth
  treatment without being asked to re-derive it from scratch.

### Tests
- `AuthServiceTests.cs`: removed all 8 old forgot/reset-password + cooldown tests (the types they
  referenced — `PasswordResetToken`, `IPasswordResetTokenRepository` — no longer exist). Added:
  `ForgotPasswordAsync_unknown_email_has_no_side_effects` /
  `_inactive_user_has_no_side_effects` (rewritten for the new design — assert no
  `_users.SaveChangesAsync`/`_notifications.EnqueueAsync`),
  `ForgotPasswordAsync_known_active_user_sets_temp_password_and_enqueues_notification`,
  `LoginAsync_valid_temp_password_succeeds_and_flags_passwordIsTemporary`,
  `LoginAsync_expired_temp_password_returns_specific_error`,
  `LoginAsync_wrong_password_against_expired_temp_password_stays_generic` (guards the
  hash-match-only ordering explicitly).
- `AuthServiceCapabilitiesTests.cs` / `AuthServiceTabsTests.cs` / `TwoFactorAuthTests.cs`:
  removed the now-nonexistent `IPasswordResetTokenRepository` substitute + ctor arg only: no
  other changes.
- `UserServicePasswordTests.cs` (**not** in the brief's explicit file list, added anyway — see
  "Deviation" below): `ChangePassword_clears_temp_password_expiry_on_success`.

## Deviations from the brief (flagging, not blocking)

1. **"change-password скидає TempPasswordExpiresAt" test placed in `UserServicePasswordTests.cs`,
   not `AuthServiceTests.cs`.** The brief listed this as one of the `AuthServiceTests.cs`
   additions, but `ChangePasswordAsync` is a `UserService` method — `AuthService` has no such
   method, so the test cannot be written against `AuthServiceTests.cs`'s `_sut`. Added it to the
   pre-existing `UserServicePasswordTests.cs` instead, which already covers this exact method.
   Straightforward correction, not a real ambiguity.
2. **TASK-460's per-user forgot-password cooldown (60s) was not carried over.** The 9-step
   `ForgotPasswordAsync` sequence in the brief doesn't mention one, and TASK-464 didn't add a
   field that would support one independent of `TempPasswordExpiresAt` itself. Implemented
   exactly the 9 steps as specified — no cooldown. Net effect: the per-IP rate limit
   (`auth-forgot-password`, 5/min) is now the only throttle on repeated notification sends to a
   known/guessed email, same as before TASK-460 existed. Flagging since this was a deliberate
   MEDIUM-severity fix in the superseded design and KI-014 already documents per-IP limiting as
   unreliable in prod — worth a conscious call on whether it's wanted back, not re-added
   unilaterally since it wasn't in scope here and there's no supporting field for it yet.

## Contract for TASK-466 (frontend-developer)

```
POST /api/auth/forgot-password   [public, rate limit 5/min per IP] — UNCHANGED
  Body: { email: string }
  204: always — do not branch UI copy on the response.

POST /api/auth/login
  Body: { email, password }
  200 (unchanged shape) — outcome.User now carries two new fields:
    passwordIsTemporary: boolean
    temporaryPasswordExpiresAt: string | null   // ISO datetime, UTC; null unless the flag is true
  401 error text — one NEW specific case on top of the existing generic
  "Invalid email or password.":
    "Temporary password has expired. Please request a new one."
    (only ever returned when the submitted password DID match a hash that turned out to be an
    expired temp password — never on a genuinely wrong password)
```
`passwordIsTemporary`/`temporaryPasswordExpiresAt` also ride along on `AuthUserDto` everywhere
else it's returned (`/auth/refresh`, `/auth/2fa/verify`, `GET /auth/me`) — safe to read the flag
from any of those, not just the initial login response.

`POST /api/auth/reset-password` **no longer exists** (404) — the `/reset-password?token=...`
page/route and its API client call are dead and should be removed by TASK-466, not just left
unlinked.

## Verification

- `dotnet build ShelfGuard.sln` — 0 warnings, 0 errors (only the pre-existing unrelated
  `MarketplaceServiceTests.cs` warning remains).
- `dotnet test` — **1220/1220 passed**, 0 skipped (was 1221 before this task; net -1 matches the
  8-removed/7-added test delta exactly).
- Worker: `npx tsc --noEmit` — clean, 0 errors.
- Repo-wide grep for `PasswordResetToken|IPasswordResetTokenRepository|ResetPasswordRequest|
  ResetPasswordAsync|resetUrl|_frontendBaseUrl` after all edits — zero hits outside immutable
  migration history (`20260730090415_AddPasswordResetTokens*`,
  `20260804194648_DropPasswordResetTokensAddTempPasswordExpiry*`, never edited after the fact)
  and historical prose in `RlsCrossTenantIntegrationTests.cs` (already updated by TASK-464).

## Not in scope (per brief)

- `frontend/`, `mobile/` — TASK-466 removes the `/reset-password` page/components.
- `.claude/docs/*` — TASK-468 (documentation-writer).
- Re-adding a forgot-password cooldown — see Deviation #2 above.

## Files

Modified:
- `backend/ShelfGuard.Application/Features/Auth/AuthService.cs`
- `backend/ShelfGuard.Application/Features/Auth/IAuthService.cs`
- `backend/ShelfGuard.Application/Features/Auth/Dtos/AuthDtos.cs`
- `backend/ShelfGuard.Api/Controllers/AuthController.cs`
- `backend/ShelfGuard.Application/Features/Users/UserService.cs`
- `backend/ShelfGuard.Tests/Auth/AuthServiceTests.cs`
- `backend/ShelfGuard.Tests/Auth/AuthServiceCapabilitiesTests.cs`
- `backend/ShelfGuard.Tests/Auth/AuthServiceTabsTests.cs`
- `backend/ShelfGuard.Tests/Auth/TwoFactorAuthTests.cs`
- `backend/ShelfGuard.Tests/Users/UserServicePasswordTests.cs`
- `worker/src/jobs/notification-dispatch.job.ts`
- `.claude/tasks/current.md`

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
