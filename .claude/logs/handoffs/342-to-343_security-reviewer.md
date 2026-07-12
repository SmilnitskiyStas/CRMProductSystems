# Handoff 342 → 343 (security-reviewer)

TASK-342 implemented ADR-019's temporary permission grants on top of the TASK-341 schema.
Please audit the authorization logic below — it's the whole point of this feature.

## New endpoints (`backend/ShelfGuard.Api/Controllers/UsersController.cs`)

- `POST /api/users/{id}/permission-grants` — `[Authorize(Policy = AtLeastStoreManager)]`.
  `id` = **target/recipient** user. Body: `GrantTemporaryPermissionRequest(PermissionKey, ExpiresAt)`.
  Delegates to `UserService.GrantTemporaryPermissionAsync`.
- `GET /api/users/{id}/permission-grants` — same policy floor as the rest of the controller
  (`AtLeastStoreManager` on the controller-level attribute — no per-action override, so any
  store manager+ in the tenant can list any user's active grants; read-only, no rank check).
- `DELETE /api/users/{id}/permission-grants/{grantId}` — `[Authorize(Policy = AtLeastStoreManager)]`.
  Delegates to `UserService.RevokeTemporaryPermissionAsync`.

## Exact rank-check locations

`backend/ShelfGuard.Application/Features/Users/UserService.cs`:

- **Grant** — `GrantTemporaryPermissionAsync`, look for the comment
  `// Role hierarchy check — acting user must outrank target`. Fetches both `actingUser`
  and `target` fresh from `_users.GetByIdAsync` (NOT from JWT claims) so the check runs
  against current DB state, not a possibly-stale token. Rule:
  `RoleRank[actingUser.Role] > RoleRank[target.Role]` (strict `>`, `<=` rejects — identical
  to `UpdatePermissionsAsync`'s existing rule at line ~278). Also blocks `targetUserId ==
  actingUserId` (no self-grant) and cross-tenant grants (`actingUser.TenantId != tenantId`
  or `target.TenantId != tenantId` → 404, not 403 — avoids leaking cross-tenant user
  existence).
- **Revoke** — `RevokeTemporaryPermissionAsync`. Two paths:
  1. `grant.GrantedByUserId == actingUserId` → always allowed (revoking your own earlier
     decision), **no rank re-check** — if the granter's role was since downgraded, they can
     still revoke what they themselves granted. Flagging this explicitly: is "the granter
     can always revoke their own grant, even after a demotion" the behavior you want, or
     should it re-check rank every time? I judged this an acceptable and probably-desired
     behavior (a demoted manager retracting their own earlier grant is not privilege
     escalation) but it's a judgment call worth a second look.
  2. Otherwise: `RoleRank[actingUser.Role] > RoleRank[recipient.Role]` (recipient = the
     grant's `UserId`, fetched fresh), same strict-`>` rule.
  3. **Hard block: `grant.UserId == actingUserId` → always rejected**, before either path
     above runs — a recipient can never revoke their own grant, full stop, even if they
     also happen to be the granter of someone else's identical grant (edge case: can't
     construct anyway, since self-grant is blocked at creation).

## Other things worth checking

- `expiresAt` validation in `GrantTemporaryPermissionAsync`: must be `> DateTime.UtcNow` and
  `<= DateTime.UtcNow.AddDays(90)`. Client-supplied `DateTime` is normalized to UTC
  (`expiresAt.Kind == Utc ? expiresAt : expiresAt.ToUniversalTime()`) before the future/cap
  check — worth confirming this can't be gamed with `DateTimeKind.Unspecified` values that
  `ToUniversalTime()` treats as local-server-time rather than UTC (ASP.NET Core's JSON
  deserializer should hand back `Utc` kind for ISO-8601 `Z`-suffixed timestamps, but a
  client sending a bare `"2026-08-01T00:00:00"` with no offset would hit the
  `Unspecified`→local-time branch).
- `PermissionKey` is validated against the existing `ValidPages` set (same as
  `UpdatePermissionsRequest`) — no new granularity, per ADR-019.
- Effective-permission merge (`AuthService.BuildEffectivePermissionsAsync`) always forces a
  live grant's key to `true`, **even over an explicit permanent `false`** in
  `User.Permissions` — this is intentional per ADR-019 ("more specific and more recent
  authorization wins") but is a real widening of access, worth confirming the threat model
  is fine with a temporary grant overriding an explicit deny.
- Worker (`worker/src/jobs/permission-grant-expiry.job.ts`) runs cross-tenant queries via
  `SET app.role = 'worker'` — this matches the existing convention in every other worker
  cron job (`expiry-check.job.ts`, `weekly-report.job.ts`, etc.), but note the RLS policy
  actually created on `user_permission_grants` (`AddUserPermissionGrants` migration) only
  grants bypass to `app.role = 'provider'`, not `'worker'`. This is a pre-existing
  discrepancy across the whole worker codebase (no migration anywhere defines a `'worker'`
  bypass), not something introduced by this task — flagging in case it's worth a
  broader follow-up rather than a per-table fix here.

## Not covered by this task (left as-is)
- No dedicated unit tests for `GrantTemporaryPermissionAsync`/`RevokeTemporaryPermissionAsync`/
  `GetActivePermissionGrantsAsync` were written — existing `AuthServiceTests`/
  `TwoFactorAuthTests`/`UserServicePasswordTests` were only updated for the new ctor
  dependency. Recommend qa-tester or a follow-up backend task add coverage for the rank
  checks above before this ships.
- Frontend (`UserPermissionsEditor.tsx` per ADR-019 §6) is out of scope for TASK-342.
