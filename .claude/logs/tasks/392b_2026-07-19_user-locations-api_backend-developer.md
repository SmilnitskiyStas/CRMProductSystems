# TASK-392b: Store-scoped user↔location assignment — API/service layer (Stage 1)

**Agent:** backend-developer
**Date:** 2026-07-19
**Status:** done (schema/plumbing only, per brief — Stage 3 RESTRICTIVE RLS store_scope
policies are a separate later task, not this one)

## Зроблено

1. **`ILocationService.BelongsToTenantAsync`** (`Locations/ILocationService.cs`,
   `LocationService.cs`) — mirrors `ILegalEntityService.BelongsToTenantAsync` exactly:
   `_repo.GetByIdAsync(locationId, ct)` + `location.TenantId == tenantId`. Closes the
   pre-existing gap where `UserService.InviteAsync`/`UpdateAsync` accepted any GUID as
   `StoreId` with zero tenant-ownership check.

2. **`UserService.InviteAsync`/`UpdateAsync`** — added the same StoreId-tenant check
   (Ukrainian message matching the adjacent LegalEntityId check's phrasing:
   "Вказана локація не належить цьому тенанту."), plus a new private helper
   `SyncSingleLocationAsync(tenantId, userId, role, storeId, actingUserId, ct)` called
   from both, right after `User.Create`/`target.SetStore(...)`:
   - New `SingleLocationRoles` set = `store_manager, merchandiser, storekeeper, cashier,
     staff`. For these, writes exactly one `user_locations` row via
     `IUserLocationRepository.ReplaceForUserAsync(tenantId, userId, [storeId] or [],
     actingUserId, ct)` — same shared `AppDbContext`/single `_users.SaveChangesAsync(ct)`
     transaction as the User row update (repo method doesn't call SaveChanges itself).
   - `network_manager`/`enterprise_admin`/`supplier_admin` (and anything else outside the
     set): no-op, existing rows untouched. network_manager's list only ever changes via
     the new dedicated endpoint (item 3); enterprise_admin's bypass is unconditional
     regardless of what (if anything) lingers in the table for that user.

3. **New endpoints on `UsersController`** (both `[Authorize(Policy =
   AppPolicies.AtLeastEnterpriseAdmin)]`, no capability-OR bypass — same anti-escalation
   gate as `AssignTenantRole`):
   - `PUT /api/users/{id}/locations` — body `{ "locationIds": ["uuid", ...] }` (record
     `UpdateUserLocationsRequest`), full-replace semantics. Returns `200
     UserLocationsDto` (`{ "locationIds": [...] }`) on success, `404 { error }` if the
     target user isn't found/cross-tenant, `400 { error }` if any id doesn't belong to
     the tenant (message never reveals whether the id exists in another tenant).
   - `GET /api/users/{id}/locations` — returns `200 UserLocationsDto` or `404 { error }`.
   - Backing service methods `IUserService.SetLocationsAsync`/`GetLocationsAsync`:
     `SetLocationsAsync` dedupes input ids, validates each via
     `ILocationService.BelongsToTenantAsync` (fails closed on the first bad id, writes
     nothing), then calls the same `ReplaceForUserAsync` used by
     `SyncSingleLocationAsync` — one unified repository mechanism for both the
     single-row and full-list paths, per the architecture note in the brief.

4. **`IUserLocationRepository`/`UserLocationRepository`** (new,
   `Domain/Interfaces/IUserLocationRepository.cs` +
   `Infrastructure/Data/Repositories/UserLocationRepository.cs`, registered in
   `DependencyInjection.cs`) — `GetLocationIdsForUserAsync` (AsNoTracking) and
   `ReplaceForUserAsync` (literal delete-all-existing + insert-distinct-new, matching
   the brief's explicit "full delete+insert" instruction for the PUT endpoint; reused
   as-is for the single-row Invite/Update path too since it's the simpler of the two
   options the brief allowed there). Neither method calls `SaveChangesAsync` itself —
   caller controls the transaction boundary, consistent with `ITenantRoleRepository`/
   `IUserPermissionGrantRepository`'s existing convention in this codebase.
   `UserService` depends on it directly (constructor-injected alongside
   `_tenantRoles`/`_permissionGrants`), not through another Application service — same
   reasoning as those two: it's a join-table repository conceptually owned by user
   management, not by the Locations feature.

5. **`app.user_id` session variable** (`TenantConnectionInterceptor.cs`) —
   `BuildSetSql` gained a third optional `userId` parameter, read from
   `ClaimTypes.NameIdentifier` in `GetSetSql()` (same claim `UsersController` already
   reads for `actingUserId` elsewhere — confirmed live/working, not a guess: JWT stores
   the id under the standard `sub` claim, and the default inbound-claim-type mapping
   resolves it to `ClaimTypes.NameIdentifier` on read, exactly like the pre-existing
   `ClaimTypes.Role` round-trip). Same **always-set, null-UUID-fallback** discipline as
   `app.tenant_id` (deliberately NOT the "leave unset if invalid" behavior `app.role`
   has) — a stale real user_id must never survive on a pooled connection into the next
   request, since Stage 3's future EXISTS-subquery policy will key off it directly.
   Unauthenticated branch now also emits `RESET app.user_id;`. No RESTRICTIVE policy
   reads this yet — Stage 3 only.

6. **INVITE_ROLES check (item 5 of brief)** — confirmed, no backend action needed.
   `UserService.ValidRoles` is a plain "is this a real role name" whitelist (already
   includes `network_manager`/`enterprise_admin`); the actual "who can invite/assign
   which role" gate is 100% the `RoleRank` dictionary comparison (TASK-347). No separate
   hardcoded invite-permission list exists on the backend. `grep -r "ValidRoles"` found
   only this one plus the interceptor's unrelated role whitelist (already complete too).

## Тести

- New: `UserServiceLocationsTests.cs` (19 tests) — StoreId tenant-ownership validation
  in Invite/Update, `SyncSingleLocationAsync` behavior across all role-transition edge
  cases (single-row write/clear/change, network_manager/enterprise_admin no-op,
  role-change collapsing N→1), `SetLocationsAsync`/`GetLocationsAsync` (not-found,
  cross-tenant, bad-location, dedup, empty-list-clears, happy path).
- New: `LocationServiceTests.cs` (3 tests) — `BelongsToTenantAsync` same/different
  tenant/not-found, first direct test of this method shape in the codebase (mirrors
  `ILegalEntityService`'s sibling, which also had no direct test before now).
- New: 4 tests in `TenantConnectionInterceptorTests.cs` for `app.user_id`
  (valid guid / omitted / non-guid / claim-absent — all fall back to null-UUID except
  the valid case).
- Updated constructor call sites in the 5 pre-existing `UserService` test files
  (`UserServiceTenantRoleTests`, `UserServicePasswordTests`, `UserServiceCrossTenantTests`,
  `UserServiceEscalationTests`, `UserServicePreferredLocaleTests`) for the two new
  constructor params — all pre-existing StoreId usages in those files were already
  `StoreId: null`, so no behavior/stubbing changes were needed beyond the constructor
  signature.

## Верифікація

- `dotnet build` (full solution, `--no-incremental`) — 0 errors, 0 warnings.
- `dotnet build` (Tests project, `--no-incremental`) — 0 errors, 1 pre-existing warning
  (`MarketplaceServiceTests.cs:534`, not touched by this task, already noted in TASK-392's log).
- `dotnet test` — **892/892 passed** (0 failed). Baseline before this task was 858
  (TASK-392); some of the +34 delta predates this session (unrelated concurrent work),
  this task's own share is 26 new tests (19 + 3 + 4) plus the 5 constructor-signature
  updates to already-passing tests.
- Git: local commit only, **no push** (product owner requested a deploy pause —
  respected per explicit instruction in the task brief).

## Не в скоупі (свідомо)

- Stage 3 RESTRICTIVE RLS store_scope policies on product_stock/daily_sales/pos_shifts/etc.
  `app.user_id` is set and available but nothing reads it yet.
- Frontend (InviteUserModal.tsx, UserDetailPanel.tsx, UserLocationsEditor.tsx,
  INVITE_ROLES list widening on the frontend) — separate parallel task per the brief.
- `.claude/docs/api-contracts.md` — not updated; that file's Users section is already
  stale from before this task (still lists `GET /api/users` as "future" backlog), so
  reconciling it holistically is out of scope here and better suited to
  documentation-writer.
