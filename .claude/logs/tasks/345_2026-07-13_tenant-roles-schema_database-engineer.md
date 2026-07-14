# TASK-345 — tenant_roles schema (ADR-020)

**Agent:** database-engineer
**Date:** 2026-07-13
**Status:** done

## What was done
- `backend/ShelfGuard.Domain/Entities/TenantRole.cs` — new entity, rich-behavior style
  (private setters, `Create`, `Update`, `Deactivate`), mirrors `SupplierRole` exactly
  (closest precedent: tenant-scoped custom role template).
- `User.cs` — `TenantRoleId: Guid?` + `SetTenantRole(Guid?)`, same shape as
  `ProviderRoleId`/`SupplierRoleId`. `Role` (string hierarchy) untouched.
- `AppRoles.cs` — new `Staff = "staff"` const (rank 0, below Cashier), added to `All` and
  the hierarchy doc comment. **Not** added to any `AppPolicies` role array (out of my
  scope — backend-developer, ADR-020 point 4).
- `UserService.cs` — `"staff"` added to `ValidRoles` (invite whitelist) and
  `RoleRank["staff"] = 0`. Confirmed `RoleRank` is defined **only** here — grepped the
  whole backend, no second C# copy (`AppRoles.cs` has no rank dict). A third copy
  (`ROLE_RANK` in `frontend/features/users/types.ts`) exists but is frontend scope, not
  touched.
- `TenantConnectionInterceptor.cs` — `"staff"` added to the tenant-scoped `ValidRoles`
  whitelist (same list as `cashier`/`store_manager`, not the provider/worker path). Added
  `[InlineData("staff")]` to the existing `BuildSetSql_accepts_all_valid_roles` theory in
  `TenantConnectionInterceptorTests.cs`.
- `AppDbContext.cs` — `DbSet<TenantRole>`; `User` config gets
  `TenantRoleId` → SetNull FK; new `TenantRole` entity block (table `tenant_roles`,
  partial unique index, `CreatedByUserId` → `users` SetNull FK).
- Migration `20260713152826_AddTenantRoles` — EF-generated table/index/FKs, hand-added RLS
  block (all three policies from day one, see below). Symmetric `Down()`.
- `ITenantRoleRepository` / `TenantRoleRepository` — see handoff for exact method list.
- DI registration in `Infrastructure/DependencyInjection.cs`.

## Judgment calls (flagged per CLAUDE.md gate — objective/convention-driven, no user sign-off needed)

1. **`Capabilities` is `text[]`, not `jsonb`.** The task brief and ADR-020 both say "jsonb
   List<string>, за зразком ProviderRole.Permissions/SupplierRole.Permissions" — but I
   read the actual code: both `ProviderRole.Permissions` and `SupplierRole.Permissions`
   are `.HasColumnType("text[]")`, a native Postgres array, **not** jsonb. There is no
   jsonb+`List<string>` converter anywhere on those two entities. Since the explicit
   instruction was to copy the *same approach* as those two entities, I followed the real
   code over the (incorrect) "jsonb" label in the brief/ADR — `text[]` also sidesteps the
   `EnableDynamicJson()` footgun already burned once in this project (per project memory:
   missing it on a jsonb `List<string>` column → 500 in prod). Net effect for
   backend-developer: none — `TenantRole.Capabilities` is a plain `List<string>` in C#
   either way, no serialization code needed on their side.
2. **No `HasOne<Tenant>()` FK on `tenant_roles.TenantId`.** Matches `SupplierRole` exactly
   (also has no Tenant FK, just an indexed column + RLS) — the closest same-feature-family
   precedent. `UserPermissionGrant` (ADR-019, the most recent sibling governance table)
   makes the same choice for the same reason. Isolation is enforced by RLS, not a DB FK.
3. **Partial unique index is case-sensitive.** Checked for a case-insensitive-unique-name
   convention elsewhere (citext, `LOWER()` in any migration/index) — found none.
   `SupplierRole.DisplayNameExistsAsync` (the closest precedent) does a plain `==`
   comparison. Implemented `(TenantId, Name)` as a plain (case-sensitive) partial unique
   index, `WHERE "IsActive"` — matches ADR-020 point 2 verbatim.
4. **`CreatedByUserId` is `Guid?` + SetNull**, not required. Matches the dominant recent
   "creator reference" convention (`MarketplaceOrder`, `SupplierAgreement`,
   `SupplierSupportTicket`, `SupplierTask` — all `Guid? CreatedByUserId` + SetNull), not
   `UserPermissionGrant.GrantedByUserId`'s stricter required+Restrict (that field is core
   to the row's meaning; "who created this template" is not).
5. **RLS pattern source**: `AddWorkerBypassRlsPolicy` (20260712175141) is the newer of the
   two candidate migrations named in the brief. Took the strict `tenant_isolation` +
   `provider_bypass` SQL text verbatim from `AddUserPermissionGrants` (freshest
   *table-creation* RLS block) and added `worker_bypass` in the exact predicate form used
   by `AddWorkerBypassRlsPolicy`'s dynamic SQL — all three present from this table's first
   migration, per the mandatory rule (2026-07-12 worker_bypass retrofit incident).

## Build/migration/test status
- `dotnet build` (full solution): succeeded, 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs:534`, not touched by this task).
- `dotnet ef migrations add AddTenantRoles`: succeeded.
- `dotnet ef migrations script AddWorkerBypassRlsPolicy AddTenantRoles` (isolated to this
  migration): verified — `tenant_roles` table (8 columns), `PK_tenant_roles`,
  `FK_tenant_roles_users_CreatedByUserId` (SetNull), `FK_users_tenant_roles_TenantRoleId`
  (SetNull), partial unique index `uq_tenant_roles_tenant_name_active` on
  `("TenantId", "Name") WHERE "IsActive"`, and all three RLS policies
  (`tenant_isolation` strict NULLIF pattern / `provider_bypass` / `worker_bypass`) all
  present.
- `dotnet test` (full suite): **702/702 passed** (701 pre-existing + 1 new
  `[InlineData("staff")]` case).
- Not applied to any live database in this task (schema-only, per role scope).

## Out of scope (left for backend-developer, TASK-346, per ADR-020 points 3–9)
`TenantRoleCapabilities` constants class, `RoleOrCapabilityRequirement`/Handler,
`AppPolicies` per-action policy split on the 8 named controllers, `TenantRoleAuthorization.
HasCapability`, `TenantRolesController` + `POST /api/users/{id}/tenant-role`, JWT
`capabilities` claim (`AuthService`/`JwtService`), frontend. `AppRoles.Staff` is not wired
into any `AppPolicies` array — it currently grants nothing beyond bare auth, exactly as
specified.
