# TASK-321 — Legal Entities schema (юридичні особи)

**Agent:** database-engineer
**Date:** 2026-07-08
**Status:** done (Domain/EF/migration layer only — see handoff)

## What was done

New tenant-scoped entity `LegalEntity` (0..N per tenant), modeled on
`SupplierContractSettings` field shape, plus optional `LegalEntityId` FK
wired onto `Location`, `Store`, and `User` per the approved plan
(`abstract-juggling-shannon.md`).

### Files changed/created
- `backend/ShelfGuard.Domain/Entities/LegalEntity.cs` — new entity
- `backend/ShelfGuard.Domain/Entities/Location.cs` — added `LegalEntityId`
- `backend/ShelfGuard.Domain/Entities/Store.cs` — added `LegalEntityId`
- `backend/ShelfGuard.Domain/Entities/User.cs` — added `LegalEntityId` + `SetLegalEntity()`
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` — `DbSet<LegalEntity>`, entity config, FK config on Location/User
- `backend/ShelfGuard.Infrastructure/Migrations/20260708200121_AddLegalEntities.cs` (+ Designer, + snapshot)

### Migration
`AddLegalEntities` (20260708200121) — creates `legal_entities` table, adds
nullable `LegalEntityId` to `locations` and `users`, indexes, FKs
(`SetNull` on delete), RLS (`tenant_isolation` + `provider_bypass`, NULLIF
guard pattern). `Down()` reverses RLS before dropping FKs/table/columns.

### Build status
`dotnet build` — succeeds, 0 errors (1 pre-existing unrelated warning in
`MarketplaceServiceTests.cs`).

`dotnet ef database update` **not run** — no confirmed safe local/dev
connection string in this session. Migration SQL reviewed manually and
looks correct; needs to be applied to target DB before use.

## Decisions / things to double-check

- **`Store` entity has no EF mapping.** `Store.cs`/`StoreZone` are not
  registered in `AppDbContext` (no `DbSet<Store>`, no `Entity<Store>`
  config) — only `Location`/`LocationZone` are the live v4 entities. Per
  plan wording ("Store/Location — legacy дублікат, той самий патерн") I
  added `LegalEntityId` to `Store.cs` for domain-model parity as
  instructed, but there is **no migration/FK/index for Store** since it
  isn't part of the EF model. If `Store` is truly dead code, consider
  flagging for removal in a separate cleanup task.
- **RLS pattern:** single-tenant `tenant_isolation` policy (like
  `supplier_contract_settings`), not the two-tenant pattern — correct
  since `LegalEntity` only ever belongs to one tenant.
- **FK on delete:** `LegalEntity.TenantId → tenants` uses `Restrict`
  (matches Location/Store/Tenant pattern in this file, not Cascade like
  `SupplierContractSettings`). `Location.LegalEntityId` /
  `User.LegalEntityId → legal_entities` both use `SetNull`, matching the
  plan's "SetNull, IsRequired(false)" instruction.
- **Index naming:** used `idx_legal_entities_tenant` (explicit name, per
  plan) for the TenantId index; `locations`/`users` FK indexes use EF's
  default `IX_*` naming (consistent with how other nullable FK indexes in
  this file are named — no explicit `HasDatabaseName` used elsewhere for
  those).

## Handoff

Next: `backend-developer` for `LegalEntityService`, DTOs, permission key
(`legal_entities.manage`), and `LegalEntitiesController` (see plan sections
4–6). Also need `dotnet ef database update` run against target DB before
any of this is usable end-to-end.
