# Handoff: Supplier tenant migration + roles/tasks schema → Backend

**From:** database-engineer (TASK-305)
**To:** backend-developer
**Date:** 2026-07-05
**Plan:** `calm-singing-marble.md`

## What's ready

### 1. Orphan supplier data migration (Part 2 of the plan)
`20260705171004_MigrateOrphanSuppliersToTenants` re-points every `Supplier` (+ its
`SupplierProfile`, with `IsOwnerManaged = true`) that was attached to the
`platform-marketplace` system tenant onto a brand-new, active, real tenant
(`BusinessType='supplier'`). **Not yet applied to any database** — apply it as part
of your deploy/migration step, then verify:
```sql
select s."Name", t."Slug", t."IsActive", sp."IsOwnerManaged"
from suppliers s join tenants t on t."Id" = s."TenantId"
join supplier_profiles sp on sp."SupplierId" = s."Id";
```
After confirming no supplier still points at `platform-marketplace`, Part 1 cleanup
(deleting `MarketplaceAdminController.CreateSupplier`,
`MarketplaceService.AdminCreateSupplierAsync`, `GetOrCreatePlatformTenantIdAsync`,
`PlatformTenantSlug`/`PlatformTenantName`) can proceed — that's your job per the plan,
not done here.

### 2. `SupplierRole` (tenant-scoped custom roles)
- Entity: `backend/ShelfGuard.Domain/Entities/SupplierRole.cs` — `Id`, `TenantId`,
  `DisplayName`, `BaseRole`, `Permissions` (`List<string>`), `IsSystem`, `CreatedAt`.
  `SupplierRole.Create(tenantId, displayName, baseRole, permissions)`, `.Update(...)`.
- Constants: `backend/ShelfGuard.Domain/Constants/SupplierPermissions.cs` —
  `CatalogManagement`, `ClientReviews`, `TaskBoard`, `StaffManagement`,
  `ProfileManagement`, `SupplierPermissions.All`. (No `SystemRoleDefaults` dictionary
  was added — `ProviderPermissions` has one keyed by `AppRoles.*`; supplier roles don't
  have fixed system base roles the same way, so it was left out. Add one if you want a
  default "full access" role name string, but note `SupplierCabinetService` currently
  treats `Permissions == null` on the user as "all access" for the owner/admin case —
  follow that same convention rather than inventing a new one.)
- `AppDbContext.SupplierRoles` DbSet, table `supplier_roles`, RLS (`tenant_isolation` +
  `provider_bypass`, `FORCE ROW LEVEL SECURITY`, `NULLIF` guard already applied).
- `User.SupplierRoleId` (nullable Guid) + `User.SetSupplierRole(Guid? roleId)`, FK
  `ON DELETE SET NULL`, mirrors `ProviderRoleId`/`SetProviderRole` exactly.

Build `ISupplierRolesService`/`SupplierRolesService` scoped by the **current user's
tenant** (`ITenantContext`), not globally like `ProviderRolesService`. Wire
`GET/POST/PUT/DELETE /api/supplier-cabinet/roles` on `SupplierCabinetController`
(same `[Authorize(Policy = AppPolicies.SupplierCabinet)]`).

Update `SupplierCabinetService.InviteStaffAsync` (currently hardcodes
`AppRoles.SupplierAdmin`, see `SupplierCabinetService.cs:241-250`): accept optional
`SupplierRoleId`, resolve `SupplierRole.Permissions` → `Dictionary<string,bool>`, call
`user.SetPermissions(...)` + `user.SetSupplierRole(roleId)`. No role given → current
"full access" behavior unchanged.

### 3. `SupplierTask` (task board, new standalone entity)
- Entity: `backend/ShelfGuard.Domain/Entities/SupplierTask.cs` — `Id`, `SupplierId`,
  `TenantId` (owner/supplier, RLS scope), `ClientTenantId?` (the B2B client tenant the
  task concerns), `AssignedToUserId?` (supplier staff), `Title`, `Description?`,
  `Status` (`"pending"` default — no enum, plain string: recommend
  `pending`/`in_progress`/`completed`/`cancelled` per the plan), `DueDate?`,
  `CreatedByUserId?`, `CreatedAt`, `CompletedAt?`. It's a plain mutable class (no
  factory method) — construct with object initializer like `WriteOff.cs`.
- `AppDbContext.SupplierTasks` DbSet, table `supplier_tasks`, FKs + RLS already
  configured (see task log 305 for exact FK delete-behaviors). Indexes on `TenantId`,
  `SupplierId`, `AssignedToUserId`, `ClientTenantId`, `Status` already created.

Build `ISupplierTaskService`/`SupplierTaskService` + endpoints on
`SupplierCabinetController`: `GET /api/supplier-cabinet/tasks` (query:
`assignedToMe: bool`, `clientTenantId?: guid`, `status?`), `POST`, `PUT /{id}`,
`PUT /{id}/status`. Gate via `SupplierPermissions.TaskBoard` (UI/service-level check,
same convention as provider permissions — base auth is still `AppPolicies.SupplierCabinet`).

## Verify before you start
- `dotnet build` / `dotnet test` are green as of this handoff (555 tests).
- Migrations not yet applied anywhere — run `dotnet ef database update` against your
  dev DB before writing/testing services against these tables.
