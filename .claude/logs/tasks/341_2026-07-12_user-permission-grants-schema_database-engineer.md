# TASK-341 — user_permission_grants schema (ADR-019)

**Agent:** database-engineer
**Date:** 2026-07-12
**Status:** done

## What was done
- `backend/ShelfGuard.Domain/Entities/UserPermissionGrant.cs` — new entity, rich-behavior
  style (private setters, `Create`, `Revoke`, `MarkNotifiedExpiring`, `MarkNotifiedExpired`,
  computed `IsActive`), mirrors `RefreshToken`.
- `AppDbContext.cs` — `DbSet<UserPermissionGrant> UserPermissionGrants`; `OnModelCreating`
  config: table `user_permission_grants`, composite index `(TenantId, UserId)`, partial
  index on `ExpiresAt WHERE "RevokedAt" IS NULL`, three FKs to `users` (see delete-behavior
  note below).
- Migration `20260712170225_AddUserPermissionGrants` — EF-generated table/indexes/FKs,
  hand-added RLS block (`tenant_isolation` + `provider_bypass`, NULLIF guard), symmetric
  Down(). Verified via `dotnet ef migrations script` — table, both indexes (composite +
  partial), all 3 FKs, and RLS policies all present. **No `CREATE EXTENSION`** — not
  needed (pg_trgm irrelevant here), so the prod `shelfguard_app` non-superuser constraint
  is not a concern for this migration.
- `IUserPermissionGrantRepository` / `UserPermissionGrantRepository` — see method list in
  the handoff file (exact names below).
- DI registration in `DependencyInjection.cs`.

## Delete-behavior decision (judgment call, no existing precedent for a 3-FK-to-users table)
- `UserId` (recipient, required) → **Cascade** — row is the user's own data, same
  convention as `RefreshToken.UserId`/`NotificationQueue.UserId`/`ChatSession.UserId`.
- `GrantedByUserId` (granter, required) → **Restrict** — follows this codebase's general
  "required FK → Restrict" convention; users are soft-deleted (`Deactivate()`) in practice,
  so this is not expected to block anything.
- `RevokedByUserId` (revoker, nullable) → **SetNull** — same pattern as
  `CreatedByUserId`/`AssignedToUserId` elsewhere in the schema.

## Build/migration status
- `dotnet build` (full solution): **succeeded**, 0 errors, 1 pre-existing unrelated
  warning in `ShelfGuard.Tests`.
- `dotnet ef migrations add AddUserPermissionGrants`: succeeded.
- `dotnet ef migrations script` (isolated to this migration): inspected, correct.
- Not applied to any live database in this task (schema-only, per role scope).

## Out of scope (left for backend-developer, TASK-342)
Controllers/services, JWT-mint merge, worker job, frontend — untouched, per instructions.
