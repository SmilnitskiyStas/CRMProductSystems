# Handoff 345 → 346 (backend-developer)

Schema for ADR-020 (custom TenantRole capability templates) is done (TASK-345). Exact
names to build the rest of ADR-020 points 3–9 against (`TenantRoleCapabilities`,
`RoleOrCapabilityRequirement`/Handler, `AppPolicies` per-action split on the 8 named
controllers, `TenantRoleAuthorization.HasCapability`, `TenantRolesController`,
`POST /api/users/{id}/tenant-role`, JWT `capabilities` claim).

## Table
`tenant_roles` (EF entity `TenantRole`, `backend/ShelfGuard.Domain/Entities/TenantRole.cs`)

Columns (PascalCase in DB, EF Core default): `Id`, `TenantId` (NOT NULL, **no DB FK** to
`tenants` — same as `SupplierRole`, isolation via RLS only), `Name` (varchar 200),
`Capabilities` (**`text[]`, not jsonb** — see note below), `IsActive` (default true),
`CreatedByUserId` (nullable, FK → `users` SetNull), `CreatedAt` (default NOW()),
`UpdatedAt` (nullable).

Entity methods (private setters, mutate only through these):
- `TenantRole.Create(tenantId, name, capabilities, createdByUserId)`
- `role.Update(name, capabilities)` — full replace, also stamps `UpdatedAt`
- `role.Deactivate()` — soft-delete, stamps `UpdatedAt`. No `Reactivate()` exists (not in
  ADR-020's scope — add it yourself if the UI ends up needing "restore an archived role").

## `User` entity
- `User.TenantRoleId: Guid?`, FK → `tenant_roles` **ON DELETE SET NULL** (deleting/
  archiving a template does not touch the user's base `Role`).
- `User.SetTenantRole(Guid? roleId)` — only mutator, mirrors `SetSupplierRole`.
- `Role` (string hierarchy) is completely untouched by TenantRole — the two mechanisms
  compose additively, exactly per ADR-020 consequence #2 ("two role-hierarchy mechanisms
  now compose").

## `Capabilities` is `text[]`, not jsonb — deviates from the brief/ADR-020 text
Both the task brief and ADR-020 point 2 say "jsonb List<string>, same as ProviderRole/
SupplierRole" — that description of the precedent is wrong. I checked the actual code:
`ProviderRole.Permissions` and `SupplierRole.Permissions` are both `.HasColumnType
("text[]")`, a native Postgres array, not jsonb — no converter, no `EnableDynamicJson()`
involved. I matched the *real* precedent instead of the label. **This changes nothing for
you**: `TenantRole.Capabilities` is a plain `List<string>` in C#, assign/read it like any
other list. If you were about to reach for `System.Text.Json`/`EnableDynamicJson()` for
this column specifically — you don't need to, Npgsql handles `text[]` natively.

## Uniqueness
`uq_tenant_roles_tenant_name_active` — **partial** unique index on `("TenantId", "Name")
WHERE "IsActive"`. Case-sensitive (no case-insensitive-name convention exists anywhere
else in this codebase — checked). Practical effect: an archived ("HR" deactivated) and a
new active "HR" **can** coexist — the DB will not reject it. Use
`ITenantRoleRepository.GetByNameAsync` (active-only, see below) as your pre-check before
insert so you can return a friendly 400 instead of catching a raw unique-violation.

## RLS
`tenant_isolation` (strict NULLIF pattern, no permissive fallback) + `provider_bypass` +
`worker_bypass` — all three from this table's first migration (worker will probably never
touch `tenant_roles`, added per the mandatory convention anyway).

## Repository — `ITenantRoleRepository` (`Domain/Interfaces/`), implementation
`TenantRoleRepository` (`Infrastructure/Data/Repositories/`), registered in
`DependencyInjection.cs`:

```csharp
Task<TenantRole?> GetByIdAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);
Task<IReadOnlyList<TenantRole>> GetAllForTenantAsync(Guid tenantId, bool includeInactive = false, CancellationToken ct = default);
Task<TenantRole?> GetByNameAsync(Guid tenantId, string name, CancellationToken ct = default); // ACTIVE rows only
Task AddAsync(TenantRole role, CancellationToken ct = default);
Task UpdateAsync(TenantRole role, CancellationToken ct = default); // call role.Update(...) first, then this, then SaveChangesAsync
Task<bool> DeactivateAsync(Guid tenantId, Guid roleId, CancellationToken ct = default); // false if not found/already inactive
Task SaveChangesAsync(CancellationToken ct = default);
```

None of the mutating methods call `SaveChangesAsync` for you (same convention as
`IUserPermissionGrantRepository`/`ISupplierRolesRepository`) — call it explicitly once per
unit of work, e.g. after `AddAsync` + an activity-log write, same pattern as
`UserService.InviteAsync`.

**Not implemented** (wasn't in my task's method list, unlike `ISupplierRolesRepository`
which has one): `IsAssignedToAnyUserAsync`. If your `TenantRolesController`'s
delete/archive endpoint wants to warn "N users still have this template assigned," either
add that method to the repository or query `_db.Users.CountAsync(u => u.TenantRoleId ==
roleId)` directly — your call.

## `AppRoles.Staff` / rank / role-set changes already done
- `AppRoles.Staff = "staff"` exists, in `AppRoles.All`, rank **0** in `UserService.
  RoleRank`, in `UserService.ValidRoles` (so `POST /api/users` invite accepts it) and in
  `TenantConnectionInterceptor.ValidRoles` (so `app.role = 'staff'` gets set in the DB
  session for RLS — grants no bypass, purely a tenant-scoped role like `cashier`).
- `AppRoles.Staff` is **not** in any `AppPolicies` role array yet — per ADR-020 point 1,
  it should grant nothing beyond bare auth by itself. That's your wiring
  (`RoleOrCapabilityRequirement`/`AppPolicies.Configure`).
- `RoleRank` exists in exactly one C# place (`UserService.cs`) — no second backend copy to
  keep in sync. `frontend/features/users/types.ts` has its own `ROLE_RANK` mirror
  (per ADR-019's note) that still needs `staff: 0` added — frontend scope, not done here.

## Notes for your implementation
- `TenantRoleCapabilities` constants class (ADR-020 point 3, `ShelfGuard.Domain.Constants`
  namespace per the ADR, mirrors `SupplierItemCategories`/`TenantUserPermissions`
  placement) does not exist yet — you're creating it.
- Judgment calls made at the schema layer are logged in
  `.claude/logs/tasks/345_2026-07-13_tenant-roles-schema_database-engineer.md` if you need
  the full reasoning (text[] vs jsonb, no Tenant FK, case-sensitive uniqueness,
  CreatedByUserId delete behavior).
- Build clean, 702/702 tests green, migration verified via scoped `dotnet ef migrations
  script` (table, partial unique index, both FKs, all 3 RLS policies confirmed present).
