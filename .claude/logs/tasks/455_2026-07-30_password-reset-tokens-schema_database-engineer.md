# TASK-455: Password reset tokens schema (forgot/reset-password flow, schema only)

**Agent:** database-engineer
**Date:** 2026-07-30
**Status:** done — created, migrated, live-verified against the real (non-superuser) app role. No blocker.

## Context

Part A (TASK-455..459) of `C:\Users\stass\.claude\plans\reflective-churning-quail.md` —
forgot/reset-password flow. This slice is schema only: entity, repository, migration, RLS,
regression-test/doc updates. No `AuthController`/`AuthService`/DTOs — that's TASK-456
(backend-developer), spawned after this.

## Done

- `backend/ShelfGuard.Domain/Entities/PasswordResetToken.cs` — styled like `RefreshToken` (private
  setters, `Create(userId, tokenHash, expiresAt)` factory, computed `IsActive` =
  `UsedAt is null && DateTime.UtcNow < ExpiresAt`, `MarkUsed()`), not `TelegramLinkCode`'s anemic
  style. Fields: `Id`, `UserId`, `TokenHash`, `ExpiresAt`, `UsedAt` (nullable), `CreatedAt`,
  nullable nav `User? User`.
- `backend/ShelfGuard.Domain/Interfaces/IPasswordResetTokenRepository.cs` — exact signature from
  the brief: `InvalidateActiveTokensAsync`, `AddAsync`, `GetActiveByHashAsync`, `SaveChangesAsync`.
  Kept separate from `IUserRepository`/`IRefreshTokenRepository` per instruction.
- `backend/ShelfGuard.Infrastructure/Data/Repositories/PasswordResetTokenRepository.cs` —
  `InvalidateActiveTokensAsync` is a bulk `ExecuteUpdateAsync` (same pattern as
  `TelegramLinkRepository.InvalidateActiveCodesAsync` — no entities loaded into the change
  tracker), `GetActiveByHashAsync` filters `UsedAt == null && ExpiresAt > UtcNow` (same shape as
  `RefreshTokenRepository.GetActiveByHashAsync`). Registered in `DependencyInjection.cs`
  (`AddScoped`).
- `AppDbContext.cs` — `DbSet<PasswordResetToken>` + fluent config: `ToTable("password_reset_tokens")`,
  unique index on `TokenHash` (maxlength 64), non-unique index `idx_password_reset_tokens_user` on
  `UserId` (bulk-invalidation lookup), `HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId)
  .OnDelete(DeleteBehavior.Cascade)` — verified byte-identical delete-behavior to
  `RefreshToken→User` (`AppDbContext.cs:271` before my edit) before writing it.
- Migration `AddPasswordResetTokens` (`20260730090415`) — table has no own `TenantId` column
  (tenant derived transitively via `UserId → users.TenantId`, same as `refresh_tokens`/
  `telegram_link_codes`). RLS: `tenant_isolation` is the **fail-open** `EXISTS`-through-`users` form
  (verified byte-for-byte against `refresh_tokens`' current live policy in
  `20260629010000_FixAllRlsPoliciesNullIfEmptyString` before writing it — same shape, only
  table/column names differ), `provider_bypass` `IN ('provider', 'provider_admin')`, `worker_bypass`
  standard. This is the intended, deliberate exception (anonymous forgot/reset-password request has
  no `app.tenant_id` yet — `TenantConnectionInterceptor` only ever `RESET`s session vars for
  unauthenticated connections, confirmed by reading it directly) — not a bug to "fix" later.
- `RlsCrossTenantIntegrationTests.cs` — added `"password_reset_tokens"` to `allowedFailOpen` (was
  `{ "users", "refresh_tokens" }`), fixed the stale assertion-message text that still listed
  `notification_settings` as a current exception (it was removed by
  `20260715120000_FixNotificationSettingsRlsFailOpen`, TASK-360 — the text just hadn't been
  updated), and added a short doc-comment note explaining why `password_reset_tokens` joins the
  list. `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass` needed no
  edit — it auto-discovers every FORCE RLS table and passed against the new one directly.
- `.claude/docs/database-schema.md` — fixed the same stale `notification_settings` row in the
  "Documented exceptions" table (replaced with `password_reset_tokens`, added a note on why
  `notification_settings` was removed rather than silently dropping the history), and added a new
  `## TASK-455` section (same convention as TASK-419/428) documenting the table, the fail-open
  rationale, and that this file's own `RLS Template` section's `provider_bypass` line
  (`= 'provider'` singular) is stale relative to actual practice (`IN (...)`) since
  `20260714150000_ExpandProviderBypassToProviderAdmin` — flagged, not fixed (out of scope, a
  broader doc pass would need to touch every table's example, not just this one).

## Verification

`docker compose up -d postgres` (Docker Desktop had a stuck WSL backend mid-task requiring a
`wsl --shutdown` + relaunch to recover — environmental, not code-related) then
`dotnet ef database update` applied `20260730090415_AddPasswordResetTokens` cleanly against the
real non-superuser `shelfguard_app_dev` connection (no `crm`-superuser escape hatch needed — brand
new, empty FK column, not an already-populated one referencing an RLS parent, so the documented
FK-validation-under-RLS gotcha's precondition never applied).

- `dotnet build` — 0 warnings, 0 errors.
- `dotnet test --filter "FullyQualifiedName~RlsCrossTenantIntegrationTests"` — **6/6 passed**, 0
  skipped (ran live against Postgres, not soft-skipped).
- Full `dotnet test` — **1213/1213 passed, 0 skipped** (~40s).

## For TASK-456 (backend-developer) — final shape to build against

- `IPasswordResetTokenRepository` has exactly 4 methods (see interface file) — no `GetByHashAsync`
  variant that ignores used/expired state (unlike `IRefreshTokenRepository`, which has both
  `GetActiveByHashAsync` and a plain `GetByHashAsync` for reuse/theft detection). If
  `ResetPasswordAsync` needs to distinguish "token not found" from "token found but already used/
  expired" for logging/telemetry, that distinction isn't available from the repository as built —
  only `GetActiveByHashAsync` (returns `null` for both cases alike). Not flagged as a gap in the
  brief, so left as specified; call it out if `AuthService` design wants that distinction.
- `InvalidateActiveTokensAsync` is a bulk `ExecuteUpdateAsync` — it does **not** load entities into
  the `AppDbContext` change tracker and does **not** call `SaveChangesAsync` itself (it commits
  immediately, same as `TelegramLinkRepository`'s equivalent). Call it before `AddAsync` +
  `SaveChangesAsync` for the new token, not after — there's no ordering guard against the reverse.
- `PasswordResetToken.Create(...)` takes a raw `tokenHash`, not a raw token — hashing (e.g. via
  `IJwtService.HashToken`, the plan's stated precedent) is the caller's (`AuthService`'s)
  responsibility, same division of labor as `RefreshToken`.
- `MarkUsed()` only sets `UsedAt` — it does not call `SaveChangesAsync`; `AuthService` must still
  call `_passwordResetTokens.SaveChangesAsync()` (or share the same `AppDbContext` SaveChanges as
  the password-change write) after calling it, same pattern as `RefreshToken.Revoke()`.
- No `TenantId` anywhere on this entity/table — do not add tenant filtering in `AuthService` for
  this lookup; RLS's fail-open `EXISTS`-through-`users` policy is what makes the anonymous lookup
  work at all, and an app-level tenant filter would have nothing to filter by pre-auth anyway.

## Not in scope (per brief, unchanged)

- No `AuthController`/`AuthService`/DTO changes (TASK-456).
- No unit/integration test written specifically for `PasswordResetTokenRepository` — the existing
  RLS regression suite covers the new table's policy shape; repository behavior itself will be
  covered by TASK-456's `AuthService`-level tests.
- No frontend/mobile/worker changes.

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `backend/ShelfGuard.Domain/Entities/PasswordResetToken.cs` (new)
- `backend/ShelfGuard.Domain/Interfaces/IPasswordResetTokenRepository.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/PasswordResetTokenRepository.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260730090415_AddPasswordResetTokens.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260730090415_AddPasswordResetTokens.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` (DbSet + fluent config added)
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` (DI registration added)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (regenerated by
  `dotnet ef migrations add`, `PasswordResetToken` metadata only)
- `backend/ShelfGuard.Tests/Infrastructure/RlsCrossTenantIntegrationTests.cs` (allowedFailOpen +
  stale assertion text + doc-comment note)
- `.claude/docs/database-schema.md` (exceptions table fix + new `## TASK-455` section)
