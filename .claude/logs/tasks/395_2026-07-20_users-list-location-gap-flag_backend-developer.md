# TASK-395: Users list — needsLocationAssignment coverage-gap flag

**Agent:** backend-developer
**Date:** 2026-07-20
**Status:** done

## Зроблено

1. **`UserDto.NeedsLocationAssignment`** (`Features/Users/Dtos/UserDtos.cs`) — new `bool`
   field (default `false`), serializes as `needsLocationAssignment` (camelCase policy). `true`
   only when the user's role ∈ `LocationScopedRoles` (network_manager, store_manager,
   merchandiser, storekeeper, cashier, staff — same 6 roles as
   `store-scope-rollout-checklist.md`'s coverage-gap SQL) AND the user has zero `user_locations`
   rows. Always `false` for enterprise_admin/provider/provider_admin/worker/supplier_admin.

2. **`IUserLocationRepository`/`UserLocationRepository`** — two new read methods:
   `HasAnyLocationAsync(tenantId, userId, ct)` (single-user existence check via `AnyAsync`) for
   GetById/Invite/Update/UpdateMyProfile/UpdatePermissions; `GetUserIdsWithAnyLocationAsync
   (tenantId, userIds, ct)` (one batched `IN`-style existence query) for `GetAllAsync` — avoids
   the N+1 the brief called out explicitly.

3. **`UserService`** — new `LocationScopedRoles` set (superset of the existing
   `SingleLocationRoles`, adds network_manager) + private `NeedsLocationAssignmentAsync` helper
   (short-circuits to `false` without a query when the role alone settles it, or tenantId is
   null). Wired into all 6 `ToDto` call sites: `GetAllAsync` (batched — filters to candidate
   ids first, then one `GetUserIdsWithAnyLocationAsync` call, empty-set fast path when no
   candidates); `GetByIdAsync`/`InviteAsync`/`UpdateAsync`/`UpdateMyProfileAsync`/
   `UpdatePermissionsAsync` (single-user helper). `ToDto`'s new parameter has no default —
   forces every call site to decide the value explicitly instead of silently defaulting wrong.
   Invite/Update call the helper *after* their own `SaveChangesAsync` — `SyncSingleLocationAsync`
   writes the row on the same tracked `AppDbContext` but a fresh query can't see it until that
   SaveChanges commits.

## Тести

`UserServiceLocationsTests.cs` — 7 new tests: GetById with/without a `user_locations` row for a
restricted role, plus enterprise_admin with zero rows (asserts the short-circuit via
`DidNotReceive().HasAnyLocationAsync`); Invite with/without a store for store_manager; GetAllAsync
mixed-roles/mixed-coverage (asserts the batch query fires exactly once via `Received(1)`) and
all-non-scoped-roles (asserts it's skipped entirely via `DidNotReceive`).

## Верифікація

- `dotnet build` (full solution, `--no-incremental`) — 0 errors, 1 pre-existing unrelated
  warning (`MarketplaceServiceTests.cs:534`, same one noted in TASK-392b's log).
- `dotnet test` — **899/899 passed** (892 baseline + 7 new).
- Git: local commit only, no push (per task brief — product owner reviews/pushes).

## Не в скоупі (свідомо)

- Frontend (Users list UI badge/filter surfacing the new field) — separate task, not requested
  here.
- `.claude/docs/api-contracts.md` — Users section already stale pre-TASK-392b (documented there
  as out of scope for backend-developer); not touched here either.
- Stage 3 RESTRICTIVE RLS enforcement itself — unaffected, this task is purely additive/read-only
  as instructed.
