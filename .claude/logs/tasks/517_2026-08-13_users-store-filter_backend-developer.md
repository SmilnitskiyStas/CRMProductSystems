# TASK-517: Users list — storeIds header filter (backend half)

**Status:** done · **Agent:** backend-developer

Renumbered from the brief's suggested TASK-508 — that id (and 509-516) were already taken by
concurrent work (KI-033 fix, pchilka import, store-selector/analytics frontend, floor-plan). Max
in `.claude/logs/tasks/` at start of this task was 516.

Backend half only — frontend-developer implements the consumer side (header store selector →
`GET /api/users`) in parallel against this same contract.

## What changed

1. `IUserLocationRepository` / `UserLocationRepository` — new
   `GetUserIdsWithLocationInAsync(tenantId, userIds, locationIds, ct)`, batched existence check
   mirroring `GetUserIdsWithAnyLocationAsync` plus a `LocationId ∈ locationIds` constraint. Empty
   `userIds` or `locationIds` short-circuits to `Array.Empty<Guid>()`.
2. `IUserService.GetAllAsync` / `UserService.GetAllAsync` — new optional
   `Guid[]? storeIds = null` param before `ct`. Null/empty = unchanged ("all stores"). Non-empty:
   keeps a user if role is outside `LocationScopedRoles` (always visible) OR they have ≥1
   `user_locations` row in `storeIds`. `NeedsLocationAssignment` is computed from the full
   candidate set regardless of the filter — unaffected.
3. `UsersController.GetAll` — `[FromQuery] Guid[]? storeIds` repeated query param, passed
   through.
4. Call-site sweep for the now-earlier `ct` slot: `SupplierCabinetService.GetStaffAsync` used
   positional `_users.GetAllAsync(tenantId, ct)` — fixed to named `ct: ct`.
   `SupplierCabinetServiceTests` mock setup updated to the 3-arg shape. Other `IUserService`
   consumers (`UsersController`, `AuthController` — no `GetAllAsync` call) checked, no other
   fixes needed.
5. New tests: `UserServiceStoreFilterTests.cs` (5 tests) — null/empty passthrough, store-match
   inclusion/exclusion, `enterprise_admin` always visible, zero-row user excluded-when-filtered /
   included-with-`NeedsLocationAssignment=true`-when-unfiltered.
6. Docs: `.claude/docs/api-contracts.md` — `GET /api/users?storeIds=uuid` line + filter
   semantics paragraph, tagged TASK-517.

## Contract (for frontend cross-check)

```
GET /api/users?storeIds={guid}&storeIds={guid}...   (repeated, omitted/empty = all stores)
```
Backend method signature: `Task<IReadOnlyList<UserDto>> GetAllAsync(Guid tenantId, Guid[]? storeIds = null, CancellationToken ct = default)`.
`UserDto` shape unchanged — no new response fields.

## Build/tests

- `dotnet build ShelfGuard.sln` — clean, 0 errors.
- `dotnet test --filter "FullyQualifiedName~Users"` — 69 passed.
- `dotnet test --filter "FullyQualifiedName~SupplierCabinetServiceTests"` (touched call site) —
  30 passed.

