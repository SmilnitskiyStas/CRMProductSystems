# TASK-306: Supplier roles + task board (backend) + marketplace admin cleanup

**Agent:** backend-developer
**Date:** 2026-07-05
**Plan:** `calm-singing-marble.md` (Parts 1, 3, 4 — backend only)
**Depends on:** TASK-305 (database-engineer, schema ready, not yet applied to any DB)

## Done

### Part 1 — removed duplicate supplier-creation path
- Deleted `MarketplaceAdminController.CreateSupplier` (`POST /api/admin/marketplace/suppliers`).
- Deleted `MarketplaceService.AdminCreateSupplierAsync` + `IMarketplaceService` signature.
- Deleted `AdminCreateSupplierDto` (`MarketplaceDtos.cs`).
- Deleted the two obsolete `MarketplaceServiceTests` covering `AdminCreateSupplierAsync`.
- **NOT deleted** (per handoff instruction, verified by grep): `GetOrCreatePlatformTenantIdAsync`,
  `PlatformTenantSlug`, `PlatformTenantName` in `MarketplaceRepository.cs`/`IMarketplaceRepository.cs`.
  Still referenced by:
  - `TenantRepository.GetAllAsync` (filters `platform-marketplace` out of the provider tenant list).
  - `MarketplaceRepositoryPlatformTenantTests.cs` (dedicated coverage of the get-or-create/race behavior).
  - `ProviderService.cs` (BUG-014 comment only, no code dependency).
  Removal is deferred until confirmed (across all environments) that no `suppliers`/`supplier_profiles`
  row still points at `platform-marketplace` post-migration (TASK-305's
  `MigrateOrphanSuppliersToTenants`). Documented as an ADR-016 amendment in `decisions.md`.

### Part 3 — supplier staff roles (TASK-306)
- `ISupplierRolesRepository`/`SupplierRolesRepository` (Domain/Infrastructure) — tenant-scoped CRUD
  against `supplier_roles`.
- `ISupplierRolesService`/`SupplierRolesService` (Application/Marketplace) — validates `BaseRole`
  (only `AppRoles.SupplierAdmin` supported — no other supplier base role exists), permissions against
  `SupplierPermissions.All`, unique `DisplayName` per tenant, blocks delete/edit of `IsSystem` roles and
  delete of roles currently assigned to staff.
- Endpoints on `SupplierCabinetController`: `GET/POST/PUT/DELETE /api/supplier-cabinet/roles[/{id}]`.
- `SupplierCabinetService.InviteStaffAsync` now accepts optional `CabinetInviteStaffDto.SupplierRoleId`.
  When set: resolves `SupplierRole.Permissions` (List<string>) → `Dictionary<string,bool>` (all `true`),
  loads the just-created user via `IUserRepository` (added as a new constructor dependency, same pattern
  as `ProviderTeamService`), calls `user.SetPermissions(...)` + `user.SetSupplierRole(roleId)`. When
  omitted: unchanged behavior (full access, `Permissions = null`).

### Part 4 — supplier task board (TASK-306)
- `ISupplierTaskRepository`/`SupplierTaskRepository` (Domain/Infrastructure) — tenant-scoped CRUD +
  filtered list (assignee/client tenant/status) against `supplier_tasks`, with joined display names.
- `ISupplierTaskService`/`SupplierTaskService` (Application/Marketplace) — resolves the caller's
  owner-managed `Supplier` (reuses `IMarketplaceRepository.GetOwnerManagedProfileAsync`, same helper
  contract as `SupplierCabinetService`), validates `Title`, validates assignee belongs to the tenant,
  validates `Status` against `pending|in_progress|completed|cancelled`, sets/clears `CompletedAt` on
  status transitions.
- Endpoints on `SupplierCabinetController`: `GET /api/supplier-cabinet/tasks` (query: `assignedToMe`,
  `clientTenantId`, `status`), `POST`, `PUT /{id}`, `PUT /{id}/status`.

### DI
- `ShelfGuard.Application/DependencyInjection.cs`: `ISupplierRolesService`, `ISupplierTaskService`.
- `ShelfGuard.Infrastructure/DependencyInjection.cs`: `ISupplierRolesRepository`, `ISupplierTaskRepository`.

### Tests
- `SupplierRolesServiceTests.cs` (new) — 10 tests: create/update/delete happy path + validation
  (missing name, invalid base role, unknown permission, duplicate name, system-role guard,
  assigned-role delete guard).
- `SupplierTaskServiceTests.cs` (new) — 10 tests: create/list/status-update happy path + validation
  (missing title, no owner-managed supplier, assignee not in tenant, invalid status, task not found).
- `SupplierCabinetServiceTests.cs` — updated constructor (added `IUserRepository`,
  `ISupplierRolesRepository`) + 3 new tests for `InviteStaffAsync` role-resolution paths (no role /
  unknown role id / role resolves permissions+assigns).

## Build / test status
- `dotnet build`: 0 errors, 0 warnings (one pre-existing unrelated nullable-warning in
  `MarketplaceServiceTests.cs:534` confirmed present before this task via `git stash`).
- `dotnet test`: **575/575 passing** (553 baseline − 2 removed AdminCreateSupplier tests + 24 new).

## Migrations — NOT applied
No dev DB was reachable in this environment: Docker Desktop is not running (project's Postgres runs via
`docker compose`, port 5435 per `appsettings.Development.json`), and the only local Postgres service
found (`postgresql-x64-17`, native Windows install) listens on the default port 5432 — a different,
unrelated instance; no `psql` client was available either to safely verify/use it. `dotnet ef migrations
list` resolves cleanly and shows all TASK-305 migrations (`AddSupplierRolesAndTasks`,
`MigrateOrphanSuppliersToTenants`) as present but pending-status is unknown without a live connection.
**Action for whoever has DB access:** run `dotnet ef database update --project ShelfGuard.Infrastructure
--startup-project ShelfGuard.Api` before manual/integration testing against these tables.

## Docs updated
- `.claude/docs/decisions.md` — ADR-016 amendment (TASK-306) describing what was deleted vs.
  intentionally kept, and the new services/endpoints.
- `.claude/docs/api-contracts.md` — not updated: supplier-cabinet endpoints were not documented there
  before this task (verified by grep), so no existing content needed changes.

## Handoff
See `.claude/logs/handoffs/306_backend-developer_to_frontend-developer.md` for the exact new endpoint
shapes (roles + tasks) and what was removed from the marketplace admin API.
