# TASK-519 — Users list: close storeIds authorization gap (backend half)

**Status:** done · **Agent:** security-reviewer
Follow-up to TASK-517. Renumbered from the brief's suggested TASK-518 — that id was claimed
concurrently by the parallel frontend-developer agent
(`518_2026-08-13_hide-redundant-all-stores-toggle_frontend-developer.md`, UX-only cleanup on
`StoreSelector.tsx`, not touched here). Max in `.claude/logs/tasks/` at start of this task
was 518.

## Vulnerability

`GET /api/users?storeIds=...` (TASK-517) trusted the caller-supplied `storeIds` at face value
with no check that the JWT-authenticated caller was authorized to see those stores. Any
`AtLeastStoreManager`-gated user — including `store_manager`/`network_manager`/`merchandiser`/
`storekeeper`/`cashier`/`staff`, contractually bound to specific stores via `user_locations` —
could select "All stores" (`storeIds` omitted) and see every employee in the tenant (PII: name,
email, phone, role, invited-by), or request an arbitrary `storeIds` for a store they have no
assignment to. `users` was deliberately excluded from ADR-022 Stage 3's RLS rollout, so nothing
at the database layer compensated for this — `UserService.GetAllAsync` did no authorization at
all on the acting caller.

## Fix

New signature: `Task<IReadOnlyList<UserDto>> GetAllAsync(Guid tenantId, Guid[]? storeIds = null, Guid? actingUserId = null, CancellationToken ct = default)`.

1. `UserService.GetAllAsync` — when `actingUserId` is supplied and that user's own role is in
   `LocationScopedRoles`, resolve their own `user_locations` via the already-registered
   `IUserLocationRepository.GetLocationIdsForUserAsync` (no new repo method) and clamp the
   effective filter to it: explicit `storeIds` gets intersected with their own stores; an
   omitted/empty request ("all stores") becomes "my own stores"; an empty resulting set (zero
   assignment, or the whole request falling outside their scope) fails closed — zero
   location-scoped-role users returned, always-visible non-scoped roles (`enterprise_admin`
   etc.) unaffected. `actingUserId = null` or an unscoped acting role (`enterprise_admin`) keeps
   today's unrestricted TASK-517 behavior exactly. `NeedsLocationAssignment` untouched — still
   computed from each target's full, unfiltered assignment.
2. `IUserService.GetAllAsync` — interface signature + doc comment updated to match.
3. `UsersController.GetAll` — resolves `actingUserId` from the JWT (`ClaimTypes.NameIdentifier`,
   same pattern as `Invite`) and passes it through. Doc comment updated.
4. `SupplierCabinetService.GetStaffAsync` — untouched, still calls `_users.GetAllAsync(tenantId, ct: ct)`;
   `actingUserId` defaults to `null` so behavior is unchanged (verified via build + its test suite).
5. Call-site sweep for the new positional slot: `SupplierCabinetServiceTests.GetStaffAsync_DelegatesToUserServiceWithTenantId`
   used positional `_userService.GetAllAsync(_tenantId, null, Arg.Any<CancellationToken>())` —
   fixed to the 4-arg shape (mechanical only, no behavior change).
6. New tests in `UserServiceStoreFilterTests.cs` (6 cases): `actingUserId` null regression guard;
   single-store scoped caller clamped on "all stores"; scoped caller requesting a store outside
   their scope gets zero location-scoped users; multi-store scoped caller sees both their stores;
   zero-assignment scoped caller fails closed; unscoped (`enterprise_admin`) acting caller
   unrestricted.
7. Docs: `.claude/docs/api-contracts.md` — added a "Caller-scoping clamp (TASK-519)" paragraph
   under the existing `GET /api/users` `storeIds` section.

## Build/tests

- `dotnet build ShelfGuard.sln` — clean, 0 errors (after killing a stale locally-running
  `ShelfGuard.Api.exe` process that was locking `bin/` DLLs from an earlier session).
- `dotnet test --filter "FullyQualifiedName~Users"` — 75 passed (69 existing + 6 new).
- `dotnet test` (full suite) — 1411 passed, 0 failed (up from 1405 at TASK-517's baseline).
