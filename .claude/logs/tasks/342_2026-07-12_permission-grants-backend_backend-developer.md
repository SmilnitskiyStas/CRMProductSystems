# TASK-342 — Temporary permission grants: backend (ADR-019)

**Agent:** backend-developer
**Date:** 2026-07-12
**Status:** done

## Scope

Built on TASK-341 (schema/repo, already in place): JWT-mint merge, grant management API,
new notification event types, worker expiry job + targeted delivery.

## Changes

**`backend/ShelfGuard.Application/Features/Auth/AuthService.cs`**
- New `IUserPermissionGrantRepository _permissionGrants` dependency (ctor param added
  before `logger`).
- New private `BuildEffectivePermissionsAsync(User, ct)`: `user.Permissions ?? {}` merged
  with every active grant (`GetActiveGrantsForUserAsync`) forced to `true`. No-tenant users
  (`TenantId is null`) short-circuit to just their permanent dict.
- Called at all three `GenerateAccessToken`/`ToDto` sites: `RefreshAsync`, `IssueTokensAsync`
  (login + 2FA verify), and `GetCurrentUserAsync` (so `/api/auth/me` agrees with the JWT).
  `ToDto` signature grew an optional `effectivePermissions` param.

**`backend/ShelfGuard.Application/Features/Users/UserService.cs` + `IUserService.cs`**
- `GrantTemporaryPermissionAsync(tenantId, actingUserId, targetUserId, permissionKey, expiresAt)`
  — validates page slug, `expiresAt` in future and ≤ 90 days out (`MaxGrantDurationDays`),
  no self-grant, both users same tenant, **actingRank > targetRank** (same `RoleRank`
  dict/pattern as `UpdatePermissionsAsync`).
- `RevokeTemporaryPermissionAsync(tenantId, actingUserId, grantId)` — allowed for the
  original granter (own decision) OR actingRank > recipient rank; recipient cannot
  self-revoke. See decision note below.
- `GetActivePermissionGrantsAsync(tenantId, userId)` — resolves granter display names via
  small deduped `_users.GetByIdAsync` batch (repo queries are `AsNoTracking` without
  `Include`).
- New DTOs in `Dtos/UserDtos.cs`: `GrantTemporaryPermissionRequest`, `PermissionGrantDto`.

**`backend/ShelfGuard.Api/Controllers/UsersController.cs`** — thin, all business logic in
`UserService`:
- `POST /api/users/{id}/permission-grants`
- `GET /api/users/{id}/permission-grants`
- `DELETE /api/users/{id}/permission-grants/{grantId}`

**`backend/ShelfGuard.Application/Features/Notifications/NotificationService.cs`**
- `ValidEventTypes` += `access.temporary_expiring_soon`, `access.temporary_expired`.

**`worker/src/jobs/permission-grant-expiry.job.ts`** (new) — cron `*/15 * * * *`:
- Scan 1: `ExpiresAt` within 24h, `RevokedAt IS NULL`, `NotifiedExpiringAt IS NULL` →
  targeted outbox row (`UserId` = recipient), stamps `NotifiedExpiringAt`.
- Scan 2: `ExpiresAt < NOW()`, `RevokedAt IS NULL`, `NotifiedExpiredAt IS NULL` → targeted
  outbox row, stamps `NotifiedExpiredAt`.
- Direct SQL against Postgres (`SET app.role = 'worker'`), same convention as
  `expiry-check.job.ts`/`weekly-report.job.ts` — no C# repository call, per the TASK-341
  handoff note.

**`worker/src/jobs/notification-dispatch.job.ts`**
- `PendingIntentRow` gained `user_id`; SELECT now fetches `"UserId"`.
- New `dispatchTargeted()` branch: when `row.user_id` is set, skip the role matrix
  entirely, check that single user's `notification_settings` for the event type (fallback
  to new `TARGETED_EVENT_CHANNELS` defaults), deliver, `logNotifications`, mark
  `dispatched`. Existing `UserId IS NULL` role-broadcast path untouched.

**`worker/src/index.ts`** — registered `permission-grant-expiry` queue + cron scheduler +
`startPermissionGrantExpiryWorker()`.

## Tests updated
- `ShelfGuard.Tests/Auth/AuthServiceTests.cs`, `TwoFactorAuthTests.cs` — added
  `IUserPermissionGrantRepository` substitute (empty grants by default) to the `AuthService`
  ctor call.
- `ShelfGuard.Tests/Users/UserServicePasswordTests.cs` — added the same substitute to the
  `UserService` ctor call.
- No new dedicated tests for the grant endpoints were added in this pass (see handoff to
  security-reviewer / follow-up QA).

## Build/test status
- `dotnet build ShelfGuard.sln` — 0 errors, 0 warnings.
- `dotnet test ShelfGuard.Tests` — 701/701 passed.
- `npx tsc --noEmit` in `/worker` — clean, exit 0.

## Decisions made (no user sign-off needed per CLAUDE.md — objective/security-hardening judgment calls)
1. **Self-revoke rejected outright.** A grant recipient can never revoke their own grant
   (regardless of who granted it) — only the original granter or a higher-ranked user can.
   Prevents a user from being able to touch their own access record at all.
2. **90-day cap** on `expiresAt` for new grants (`MaxGrantDurationDays`) — ADR-019 said
   "sensible upper bound, pick one."
3. **`GetActivePermissionGrantsAsync` resolves granter names via N `GetByIdAsync` calls**
   instead of extending the frozen TASK-341 repository with `.Include()` — active-grant
   lists per user are expected to be short (single digits).
