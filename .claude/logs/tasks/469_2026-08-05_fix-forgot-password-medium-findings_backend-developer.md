# TASK-469: Fix forgot-password MEDIUM findings (per-user cooldown + refresh-token revocation)

**Agent:** backend-developer
**Date:** 2026-08-05
**Status:** done — build 0 warnings/0 errors, tests 1222/1222 (was 1220, net +2: 2 new cooldown
tests added, 1 existing test gained an extra assertion).

## Numbering note

Confirmed 469 was free right before writing this log: `.claude/tasks/current.md`'s own
`## TASK-` headers max was 468, `.claude/logs/tasks/` max was 468 (`46*`/`47*` glob, no 469 file
yet).

## Context

TASK-467 (security-reviewer) audited TASK-464..466's temp-password forgot-password redesign and
gave a **CLEAR TO SHIP** verdict with 2 MEDIUM findings, recommended fixed now rather than
deferred. This task closes both, per the security review's own concrete-fix suggestions.

## Done

### MEDIUM #1 — per-user forgot-password cooldown (`AuthService.cs`)

Added `ForgotPasswordCooldownSeconds = 60` constant. In `ForgotPasswordAsync`, after the
unknown/inactive-email branch (so an unknown email's timing/response stays identical — no new
enumeration oracle) but before generating a new temp password: if `user.TempPasswordExpiresAt`
has a value, derive `issuedAt = TempPasswordExpiresAt - TempPasswordValidHours` (no new column,
exactly the review's suggested derivation) and, when `UtcNow - issuedAt < 60s`, treat the call
**exactly** like the unknown-email branch — log a warning, return, zero side effects, no
difference in the controller's response (`POST /api/auth/forgot-password` was already
unconditionally 204 regardless of outcome; unchanged).

Once the cooldown has elapsed (temp password issued 60s+ ago, whether still valid within its 3h
window or already expired), a new request proceeds normally and re-issues, same as before this
task — the cooldown only throttles rapid repeats, not all re-requests.

### MEDIUM #2 — anti-hijack session revocation (`AuthService.cs`)

Added `await _refreshTokens.RevokeAllForUserAsync(user.Id, ct)` inside `ForgotPasswordAsync`,
mirroring `UserService.ChangePasswordAsync`'s existing call (`UserService.cs:419`). Placed
*before* the early `_users.SaveChangesAsync(ct)` that commits the new password hash + expiry —
confirmed via `RefreshTokenRepository.RevokeAllForUserAsync` (`RefreshTokenRepository.cs:28-36`)
that it only stages the revocation in-memory (marks each active token's `RevokedAt`, no internal
`SaveChangesAsync`), and confirmed via the existing load-test comment on `IssueTokensAsync`
that `_users`/`_refreshTokens`/`_activityLogs` share one scoped `AppDbContext` — so the single
`_users.SaveChangesAsync(ct)` call flushes both the credential change and the revocation together
in the same round trip, exactly as the review asked for ("ideally flushed in the same early
round trip").

## Tests (`AuthServiceTests.cs`)

- `ForgotPasswordAsync_within_cooldown_has_no_side_effects` — a user with `TempPasswordExpiresAt`
  set to `UtcNow.AddHours(3)` (i.e. issued effectively "now") re-requests immediately; asserts
  `PasswordHash` stays exactly `"hash"` (never touched a second time), and none of
  `_users.SaveChangesAsync` / `_notifications.EnqueueAsync` / `_activityLogs.LogAsync` /
  `_tokens.RevokeAllForUserAsync` fire.
- `ForgotPasswordAsync_after_cooldown_elapsed_issues_new_temp_password` — `TempPasswordExpiresAt`
  backdated so `issuedAt` derives to ~61s ago (just past the 60s window); asserts the request
  succeeds normally (new hash, one `SaveChangesAsync`, one notification, one
  `RevokeAllForUserAsync`) — guards against the cooldown accidentally becoming permanent.
- `ForgotPasswordAsync_known_active_user_sets_temp_password_and_enqueues_notification` (existing,
  extended): added `await _tokens.Received(1).RevokeAllForUserAsync(user.Id, default);`.
- `ForgotPasswordAsync_unknown_email_has_no_side_effects` /
  `_inactive_user_has_no_side_effects` (existing): added
  `await _tokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), default);` to each, for
  the same reason the pre-existing assertions there check `_users`/`_notifications` — no new mock
  field needed, `_tokens` (the `IRefreshTokenRepository` substitute) already existed in this test
  class.

No time-mocking abstraction exists in this codebase (`DateTime.UtcNow` is called directly, not
through an `IClock` seam), so both new tests construct the "already in cooldown" /
"cooldown elapsed" state directly via `user.SetTempPasswordExpiry(...)` rather than mocking time
— matches the existing test file's own convention (`LoginAsync_expired_temp_password_...` etc.
already do the same). `TempPasswordValidHours=3` is hardcoded as `AddHours(3)` in the new tests,
same as three pre-existing tests in this file already do (it's a private `AuthService` constant,
not exposed to tests).

## Verification

- `dotnet build ShelfGuard.sln` — 0 warnings, 0 errors (only the pre-existing unrelated
  `MarketplaceServiceTests.cs` warning remains).
- `dotnet test ShelfGuard.sln --no-build` — **1222/1222 passed**, 0 skipped (was 1220 before this
  task; net +2 matches the 2 new tests exactly).
- `dotnet test --filter "FullyQualifiedName~ForgotPasswordAsync"` — 5/5 passed (both new + the
  three existing, extended ones).

## Not in scope (per brief)

- `frontend/`, `worker/` — untouched.
- `.claude/docs/*` — already done (TASK-468); no further doc changes needed, this task only
  changes internal behavior already described there as "not yet fixed, MEDIUM, fix soon" (ADR-026
  §4) — that ADR entry is now stale in the sense that both findings are closed, but per the
  brief's "Не роби" section this task does not touch docs. Flagging for whoever picks up the next
  docs pass: ADR-026 §4 (`decisions.md`) and the TASK-467 summary in `current.md` still describe
  the cooldown/revocation as open gaps.

## Files

Modified:
- `backend/ShelfGuard.Application/Features/Auth/AuthService.cs`
- `backend/ShelfGuard.Tests/Auth/AuthServiceTests.cs`

## Git

Not committed — working tree left for review (repo convention: main session/user commits).
