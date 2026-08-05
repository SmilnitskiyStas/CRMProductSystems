# TASK-464: Drop password_reset_tokens, add User.TempPasswordExpiresAt (forgot/reset-password redesign, schema only)

**Agent:** database-engineer
**Date:** 2026-08-04
**Status:** schema done. `dotnet build ShelfGuard.sln` currently fails — expected, not a defect
in this task's scope (see "Build blocker for TASK-465" below).

## ⚠️ Renumbered from the brief (read this first)

Originally assigned as "TASK-461" with a stated follow-up of "TASK-462" (backend-developer). Both
numbers are already in use by a real, unrelated, in-progress mobile feature (offline-read
cache/UX rollout):
- `.claude/logs/tasks/461_2026-08-01_allowlisted-offline-read-cache_mobile-developer.md`
- `.claude/logs/tasks/462_2026-08-01_limited-offline-read-ux_mobile-developer.md`
- `.claude/logs/tasks/463_2026-08-01_*.md` (3 files — QA/security/mobile follow-ups of the same feature)
- Extensive `.claude/tasks/blocked.md` entries for TASK-461/462/463, most recently updated 2026-08-01

Confirmed the true current max task-log number is 463 (`ls .claude/logs/tasks/ | grep -oE
"^[0-9]+" | sort -n | uniq | tail`) before picking **464 (this task)** / **465 (backend-developer
follow-up)** as the next free pair. Renamed every "TASK-461"/"TASK-462" reference I had already
written (this task's own doc-comments, `database-schema.md`) to 464/465 — confirmed zero
remaining stray references via repo-wide grep. Whoever owns the authoritative task sequence
should confirm 464/465 is correct going forward.

## Context

Product owner decided to redesign the forgot/reset-password flow (TASK-455..460, deployed to
prod 2026-07-30 as commit `647bde4c`) from a one-time email/Telegram link+token to a **temporary
password**: the system generates a temp password, the user receives it and can log in with it
directly — no link, no separate "click link, enter new password" step. The temp password becomes
the user's real, immediately-usable password, valid 3 hours unless changed first via the existing
authenticated change-password flow. This makes the token/link concept entirely unnecessary —
`password_reset_tokens` and everything backing it are dropped, not deprecated.

## Done

- **Deleted** (no dead code left):
  - `backend/ShelfGuard.Domain/Entities/PasswordResetToken.cs`
  - `backend/ShelfGuard.Domain/Interfaces/IPasswordResetTokenRepository.cs`
  - `backend/ShelfGuard.Infrastructure/Data/Repositories/PasswordResetTokenRepository.cs`
  - `AppDbContext.cs`: `DbSet<PasswordResetToken>` + its fluent config block
  - `DependencyInjection.cs`: the `IPasswordResetTokenRepository` DI registration
- **`User.cs`** — new field + methods, styled directly after the pre-existing `LockoutUntil`/
  `IsLockedOut` pair (TASK-329) per the brief's instruction: private setter, no public setter,
  dedicated methods instead of exposing the field. Exact shape (this is what TASK-465 builds
  against):
  ```csharp
  public DateTime? TempPasswordExpiresAt { get; private set; }

  public bool HasActiveTempPassword =>
      TempPasswordExpiresAt.HasValue && TempPasswordExpiresAt.Value > DateTime.UtcNow;

  public void SetTempPasswordExpiry(DateTime expiresAt) => TempPasswordExpiresAt = expiresAt;

  public void ClearTempPasswordExpiry() => TempPasswordExpiresAt = null;
  ```
  `SetTempPasswordExpiry` does **not** also set `PasswordHash` — caller (TASK-465's `AuthService`)
  must call the pre-existing `ChangePassword(string newHash)` itself, same division of labor as
  `RefreshToken`/`PasswordResetToken` had. `ChangePassword` itself is deliberately untouched and
  does **not** auto-clear `TempPasswordExpiresAt` — it's called from two flows that want opposite
  outcomes on this field (issuing a temp password needs to SET the expiry alongside; the user
  setting their own password needs to CLEAR it), so folding either behavior into `ChangePassword`
  itself would be wrong for the other caller. TASK-465 is expected to call
  `SetTempPasswordExpiry`/`ClearTempPasswordExpiry` explicitly at each call site.
- **Migration** `DropPasswordResetTokensAddTempPasswordExpiry` (`20260804194648`):
  - `Up()`: `DropTable("password_reset_tokens")` (table + its RLS policies — Postgres drops a
    table's policies automatically with the table, no separate `DROP POLICY` needed — confirmed
    live, see Verification) + `AddColumn<DateTime>("TempPasswordExpiresAt", "users", nullable:
    true)`.
  - `Down()`: recreates the table/columns/indexes but **not** the RLS policies (documented in an
    inline comment on the method) — EF's auto-scaffolding only diffs the entity model, it has no
    visibility into the original `AddPasswordResetTokens` migration's raw-SQL RLS `Sql(...)`
    calls. Not hardened further: this redesign is one-way by intent (the C# entity/repository
    backing the table are deleted, not just the schema), so a real rollback would need code
    reverted too, at which point missing RLS would need re-adding by hand regardless.
  - Class-level doc comment cross-references the entity methods above and the superseded
    TASK-455 design, matching this codebase's convention for substantial migrations.
- **`RlsCrossTenantIntegrationTests.cs`** — removed `"password_reset_tokens"` from
  `allowedFailOpen` (back to `{ "users", "refresh_tokens" }`, matching pre-TASK-455 state), fixed
  the assertion message text and the doc-comment that documented the 3rd exception (now explains
  it was dropped by this task, not deleted the historical context outright).
- **`.claude/docs/database-schema.md`**:
  - "Documented exceptions" table back to 2 rows (`users`, `refresh_tokens`); added a short note
    explaining `password_reset_tokens`' removal with pointers to both `## TASK-455` (superseded)
    and the new `## TASK-464` section.
  - Fixed the regression-test description that said "three exceptions" → "two exceptions".
  - `## TASK-455` section kept as historical context (not deleted) with a `⚠️ Superseded by
    TASK-464` note at the top per the brief's instruction.
  - New `## TASK-464` section: full detail on what was dropped/added, the `User.cs` shape above,
    and the AuthService build-blocker note (below) so TASK-465 doesn't have to rediscover it.
  - `**Updated:**` date bumped to 2026-08-04.

## Verification

- `dotnet ef migrations add DropPasswordResetTokensAddTempPasswordExpiry --project
  ShelfGuard.Infrastructure --startup-project ShelfGuard.Api` — **could not run directly**: EF
  tooling builds the whole `ShelfGuard.Api` → `Application` → `Infrastructure` → `Domain` startup
  graph, and `ShelfGuard.Application/Features/Auth/AuthService.cs` already has a hard compile-time
  dependency on the types this task deletes (added by TASK-456/460, well before this task — see
  blocker section below). Generated the migration by temporarily stubbing `AuthService.cs` (kept
  every method signature exact per `IAuthService`, bodies replaced with
  `NotImplementedException`/trimmed) purely so the solution would compile long enough for the EF
  tool to diff the model — the migration's actual content (table/column DDL) has zero dependency
  on `AuthService`'s business logic, only on the Domain entities + `AppDbContext` config. Restored
  `AuthService.cs` to its exact original content immediately after via `Write` with the
  previously-`Read` original text; confirmed byte-for-byte via `git diff`/`git status` showing
  **no changes** to that file both times (generation pass and the live-apply pass below). Net
  effect on `AuthService.cs`: zero — matches the brief's "Не чіпай AuthController/AuthService"
  instruction.
- `dotnet ef database update` (same temporary-stub technique) — applied cleanly against the real
  non-superuser `shelfguard_app_dev` connection (`ConnectionStrings__DefaultConnection` env var;
  the design-time `AppDbContextFactory` doesn't read `appsettings.Development.json`, needed the
  connection string set explicitly). No FK-validation-under-RLS gotcha triggered (brand-new
  nullable column, no FK; `DROP TABLE` needs no FK validation).
- Live-checked directly against Postgres afterward:
  - `\d users` shows `TempPasswordExpiresAt | timestamp with time zone |` nullable.
  - `SELECT to_regclass('public.password_reset_tokens')` → NULL (table gone).
  - `SELECT policyname FROM pg_policies WHERE tablename = 'password_reset_tokens'` → 0 rows (RLS
    policies gone with the table, as expected).
  - `__EFMigrationsHistory` shows `20260804194648_DropPasswordResetTokensAddTempPasswordExpiry`
    applied, directly after `20260730090415_AddPasswordResetTokens`.
- `dotnet build ShelfGuard.Domain/ShelfGuard.Domain.csproj` in isolation — **0 warnings, 0
  errors** (this task's own entity change is clean; Domain has no dependency on Application).
- `dotnet build ShelfGuard.sln` (real, non-stubbed state) — **FAILS**, exactly 2 errors, both
  `CS0246: The type or namespace name 'IPasswordResetTokenRepository' could not be found`, at
  `AuthService.cs:38` (field) and `:52` (constructor parameter). This is the accurate, final,
  expected state — see below.
- `dotnet test` — not run; would fail for the same reason (`ShelfGuard.Tests` depends on
  `ShelfGuard.Application`, which doesn't compile).

## Build blocker for TASK-465 (backend-developer) — not a defect in this task

`AuthService.cs` (`ForgotPasswordAsync`/`ResetPasswordAsync`, added by TASK-456, extended by
TASK-460's cooldown check) has a real, substantial dependency on `IPasswordResetTokenRepository`/
`PasswordResetToken` — constructor injection plus full method bodies built around them, not a
one-line reference. Same coupling also breaks 4 files under `ShelfGuard.Tests/Auth/`:
`AuthServiceTests.cs` (8+ test methods directly testing the token flow) plus
`AuthServiceCapabilitiesTests.cs`, `TwoFactorAuthTests.cs`, `AuthServiceTabsTests.cs` (these three
only via a `Substitute.For<IPasswordResetTokenRepository>()` constructor-injection field needed
purely to construct `AuthService` directly — unrelated to what they actually test).

This task's brief explicitly excluded `AuthController`/`AuthService`/DTO changes ("це TASK-465").
Rewriting `ForgotPasswordAsync`/`ResetPasswordAsync` for the temp-password design — and fixing
`IAuthService.ResetPasswordAsync(string rawToken, string newPassword, ...)`'s signature, since a
raw token no longer exists in the new design — is TASK-465's actual scope, not a mechanical
fix-up available to bolt on here. `dotnet build`/`dotnet test` will not go green again until
TASK-465 lands; this is expected sequencing, not something this task left broken by omission.

## Not in scope (per brief, unchanged)

- No `AuthController`/`AuthService`/DTO changes.
- No code to generate/hash/send the actual temporary password, no expiry-window constant value,
  no login-time enforcement of `HasActiveTempPassword` — all TASK-465.
- No frontend/mobile/worker changes.

## Git

Not committed — working tree left for review (repo convention: main session/user commits).

## Files

- `backend/ShelfGuard.Domain/Entities/PasswordResetToken.cs` (deleted)
- `backend/ShelfGuard.Domain/Interfaces/IPasswordResetTokenRepository.cs` (deleted)
- `backend/ShelfGuard.Infrastructure/Data/Repositories/PasswordResetTokenRepository.cs` (deleted)
- `backend/ShelfGuard.Domain/Entities/User.cs` (new field + 3 methods)
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` (DbSet/config removed, new column config added)
- `backend/ShelfGuard.Infrastructure/DependencyInjection.cs` (DI registration removed)
- `backend/ShelfGuard.Infrastructure/Migrations/20260804194648_DropPasswordResetTokensAddTempPasswordExpiry.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260804194648_DropPasswordResetTokensAddTempPasswordExpiry.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (regenerated)
- `backend/ShelfGuard.Tests/Infrastructure/RlsCrossTenantIntegrationTests.cs` (allowedFailOpen + doc/assertion text)
- `.claude/docs/database-schema.md` (exceptions table, TASK-455 superseded note, new TASK-464 section)
- `.claude/tasks/current.md` (new TASK-464 entry)

Not modified (verified byte-identical to the pre-task committed state via `git diff`):
- `backend/ShelfGuard.Application/Features/Auth/AuthService.cs`
