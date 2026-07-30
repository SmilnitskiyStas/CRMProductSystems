# TASK-460: Security remediation — forgot/reset-password (TASK-458's HIGH + MEDIUM)

**Agent:** backend-developer
**Date:** 2026-07-30
**Status:** done — build 0 warnings/0 errors, tests 1221/1221 (was 1220, +1), worker `tsc --noEmit`
clean. Both TASK-458 findings closed.

## HIGH — live reset token leaking via notification history (closed)

`worker/src/jobs/notification-dispatch.job.ts`, `dispatchTargeted()`: before the `logNotifications()`
call, the payload for `auth.password_reset_requested` is now redacted to `{ expiresInMinutes }`
only — `resetUrl` never reaches the `notification_queue` row that `GetHistoryAsync`/
`GET /api/notifications/history` returns to any same-tenant user. The unredacted `row.payload` is
still used earlier in the same function (`formatText`/`formatEmail`, unchanged) for the actual
email/Telegram send — delivery unaffected, only what gets persisted for history changed. Every
other event type's payload passes through the same ternary untouched (byte-identical to before),
so no regression for `receipt.created`/`supplier.message`/`supplier_agreement.signed`/`access.*`.

Verified the redaction logic in isolation (Node eval of the exact ternary against a reset-payload
fixture and a non-reset fixture, no DB/worker involved): output for the reset event is
`{"expiresInMinutes":30}` with zero trace of the token; the other event type's payload passes
through unchanged. Did not attempt a full live worker rebuild + real outbox dispatch — the shared
dev worker container (`crmproductsystems-worker-1`) runs built `dist/`, not live source, so a real
check would mean rebuilding/restarting a shared container and driving a real send; not "simple" per
the brief's fallback, so unit test + code review + the isolated logic check above stand in for it.

Left `NotificationRepository.GetHistoryAsync`/`NotificationsController` untouched — confirmed
out of scope, per the review's own explicit call that the endpoint's tenant-only scoping is a
separate, wider access-control pattern, not something to fix here.

## MEDIUM — no per-user cooldown on forgot-password (closed)

New `IPasswordResetTokenRepository.HasRecentActiveTokenAsync(userId, window, ct)` +
`PasswordResetTokenRepository` implementation — `AnyAsync` on `UserId` + `UsedAt == null` +
`CreatedAt > utcNow - window`, deliberately ignoring `ExpiresAt` (checks "was one just requested",
not "is one still usable"). `AuthService.ForgotPasswordAsync` calls it with a new 60s
`PasswordResetCooldown`, immediately after the unknown/inactive-email check and BEFORE
`InvalidateActiveTokensAsync` (which only prevents two simultaneously-active tokens, not request
frequency). On a hit: identical warning log + no-op as the unknown-email branch — no distinguishable
side effect or response, so this can't become a new enumeration signal.

## Verification

- `dotnet build ShelfGuard.sln` — 0 warnings/0 errors (the 1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs` is untouched by this task).
- `dotnet test` — **1221/1221 passed**, 0 skipped (was 1220 from TASK-456; +1 new
  `ForgotPasswordAsync_within_cooldown_window_has_no_side_effects`; also added an explicit
  `HasRecentActiveTokenAsync(...) → false` stub to the existing happy-path test so its precondition
  isn't implicit).
- Worker: `npx tsc --noEmit` in `/worker` — clean, 0 errors.

## Recommendation

Both findings are narrowly closed, scoped exactly as the review recommended. A fresh full
security-reviewer pass isn't strictly required before shipping — the diffs are small and targeted,
and don't touch the broader `GetHistoryAsync`/`NotificationsController` gap the review deliberately
left for a separate ticket — but given the HIGH severity of the original finding, a quick
confirm-only re-read of these two diffs (not a full re-audit) would be cheap insurance before real
users hit this flow.

## Files

Modified:
- `backend/ShelfGuard.Domain/Interfaces/IPasswordResetTokenRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/PasswordResetTokenRepository.cs`
- `backend/ShelfGuard.Application/Features/Auth/AuthService.cs`
- `backend/ShelfGuard.Application/Features/Auth/IAuthService.cs`
- `backend/ShelfGuard.Tests/Auth/AuthServiceTests.cs`
- `worker/src/jobs/notification-dispatch.job.ts`

## Not in scope (per brief)

- `NotificationRepository.GetHistoryAsync` / `NotificationsController` per-user scoping.
- frontend/mobile.

## Git

Not committed (repo convention — main session/user commits).
