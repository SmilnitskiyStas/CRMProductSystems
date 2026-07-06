# TASK-305 — Orphan supplier → tenant migration + supplier roles/tasks schema

**Status:** done · **Agent:** database-engineer
**Plan:** `calm-singing-marble.md` — Частина 2 (data migration) + схема з Частини 3-4 (roles, tasks)

## Part 2 — Orphan supplier data migration

New migration `20260705171004_MigrateOrphanSuppliersToTenants` (SQL-only, no model
changes). For each `Supplier` whose `TenantId` = the `platform-marketplace` system
tenant's id:
- Creates a new real `Tenant` (`BusinessType='supplier'`, `Modules='["marketplace_supplier"]'`,
  `Plan='basic'`, `IsActive=true`, `Name` = supplier's name, `Slug` = slugified name with
  Cyrillic transliteration + numeric suffix on collision).
- Re-points `suppliers.TenantId` → new tenant.
- Re-points `supplier_profiles.TenantId` → new tenant and sets `IsOwnerManaged = true`.
- Does **not** touch `supplier_reviews` (their `TenantId` is the reviewing client, not the owner).

Idempotent: guarded — if `platform-marketplace` tenant doesn't exist, or has zero
suppliers left pointing at it, the `DO $mig$` block is a no-op. Implemented as a
PL/pgSQL loop (`FOR sup IN ... LOOP`) since this is a one-off functional data
migration, not a reusable slug generator. `Down()` is intentionally a no-op
(irreversible by design — reverting would silently re-break cabinet access).

**Not applied to any database** (Docker/local dev DB was not running this session —
verified by static review + build only, per instructions not to touch prod).

## Part 3-4 — Schema for supplier roles + task board

New migration `20260705170902_AddSupplierRolesAndTasks`:

- **`supplier_roles`** table: `Id`, `TenantId`, `DisplayName`, `BaseRole`, `Permissions text[]`,
  `IsSystem`, `CreatedAt`. RLS `tenant_isolation` (`TenantId = NULLIF(current_setting('app.tenant_id', true), '')::uuid`)
  + `provider_bypass`, `FORCE ROW LEVEL SECURITY`. Index on `TenantId`.
  Unlike `ProviderRole` (global), this is tenant-scoped — each supplier tenant owns its roles.
- **`users.SupplierRoleId`** (nullable uuid, FK → `supplier_roles(Id)`, `ON DELETE SET NULL`),
  mirrors the existing `ProviderRoleId` pattern.
- **`supplier_tasks`** table: `Id`, `SupplierId`, `TenantId`, `ClientTenantId?`,
  `AssignedToUserId?`, `Title`, `Description?`, `Status` (default `"pending"`), `DueDate?`,
  `CreatedByUserId?`, `CreatedAt`, `CompletedAt?`. FKs: `SupplierId`→`suppliers` (Cascade),
  `TenantId`→`tenants` (Restrict), `ClientTenantId`→`tenants` (SetNull),
  `AssignedToUserId`/`CreatedByUserId`→`users` (SetNull). RLS `tenant_isolation` + `provider_bypass`,
  `FORCE ROW LEVEL SECURITY`. Indexes on `TenantId`, `SupplierId`, `AssignedToUserId`,
  `ClientTenantId`, `Status`.

### Domain / Infrastructure changes

- `backend/ShelfGuard.Domain/Entities/SupplierRole.cs` (new) — copy of `ProviderRole.cs`
  pattern + `TenantId`, `Create(tenantId, displayName, baseRole, permissions)`, `Update(...)`.
- `backend/ShelfGuard.Domain/Constants/SupplierPermissions.cs` (new) — `CatalogManagement`,
  `ClientReviews`, `TaskBoard`, `StaffManagement`, `ProfileManagement`, `All[]`.
- `backend/ShelfGuard.Domain/Entities/SupplierTask.cs` (new) — plain entity per plan shape,
  navigation props `Supplier`, `Tenant`, `ClientTenant`, `AssignedToUser`, `CreatedByUser`.
- `backend/ShelfGuard.Domain/Entities/User.cs` — added `SupplierRoleId` (private setter) +
  `SetSupplierRole(Guid? roleId)` method, mirrors `ProviderRoleId`/`SetProviderRole`.
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` — `DbSet<SupplierRole>`,
  `DbSet<SupplierTask>`, User FK config for `SupplierRoleId`, full entity configs for both
  new tables (see EF config blocks near `ProviderRole`/`ChatSession`).

## Verify

- `dotnet build`: green, 0 errors (1 pre-existing unrelated warning in `MarketplaceServiceTests.cs`).
- `dotnet test`: 555/555 green, no regressions.
- `dotnet ef migrations list`: both new migrations appear last, in correct order.
- `AppDbContextModelSnapshot.cs` regenerated automatically by `dotnet ef migrations add`,
  confirmed `SupplierRole`/`SupplierTask` entries present.
- No migration applied to any database (local Docker Postgres was not running this session).

## Files touched / created

- `backend/ShelfGuard.Domain/Entities/SupplierRole.cs` (new)
- `backend/ShelfGuard.Domain/Entities/SupplierTask.cs` (new)
- `backend/ShelfGuard.Domain/Constants/SupplierPermissions.cs` (new)
- `backend/ShelfGuard.Domain/Entities/User.cs`
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260705170902_AddSupplierRolesAndTasks.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260705170902_AddSupplierRolesAndTasks.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260705171004_MigrateOrphanSuppliersToTenants.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260705171004_MigrateOrphanSuppliersToTenants.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`

## Next

Handoff to `backend-developer` — see `.claude/logs/handoffs/305_database-engineer_to_backend-developer.md`.
