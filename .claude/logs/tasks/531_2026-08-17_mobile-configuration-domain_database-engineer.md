# TASK-531 — MobileConfiguration/MobileConfigurationVersion/MobileTheme entities + migration + RLS

**Agent:** database-engineer
**Status:** done

## What was done

Created three new entities for the Stage B "Mobile Configuration domain" (CLAUDE CODE SPEC ЕТАП 3):

- `backend/ShelfGuard.Domain/Entities/MobileConfiguration.cs` — root/pointer, one row per tenant.
  `PublishedVersionId`/`DraftVersionId` (both nullable FK → `MobileConfigurationVersion`).
- `backend/ShelfGuard.Domain/Entities/MobileConfigurationVersion.cs` — immutable-once-published
  snapshot. `MobileConfigurationId` (FK → parent), denormalized `TenantId`, `Version` (int,
  unique per config), `SchemaVersion`, `Status` (draft/published/archived), `ConfigurationJson`
  (jsonb), `CreatedBy?`, `CreatedAt`, `PublishedAt?`.
- `backend/ShelfGuard.Domain/Entities/MobileTheme.cs` — one row per `MobileConfiguration` (per
  tenant, not per version), unique-constrained. See design decision below.
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` — 3 new `DbSet`s + fluent config.
- Migration `20260817090727_AddMobileConfigurationDomain` (+ Designer + model snapshot).

## MobileTheme scoping decision

CLAUDE CODE SPEC ЕТАП 3 lists `MobileTheme` as its own entity separate from the generic
`ConfigurationJson` blob, even though MASTER SPEC §11's example API response nests a `theme`
object inside the same document. Resolved: `MobileTheme` is scoped **per `MobileConfiguration`
(one row per tenant), not per version**, enforced by a real unique constraint
(`uq_mobile_themes_config` on `MobileConfigurationId`). It's the directly-editable working record
the future Theme Editor (TASK-537) reads/writes; `MobileConfigurationVersion.ConfigurationJson`
stays the serialized, immutable snapshot produced from it at publish time — same relationship a
future `MobilePage`/`MobileNavigationItem` table would have. Documented in both
`.claude/docs/database-schema.md` and `.claude/docs/domain-model.md`.

## Circular FK

`MobileConfiguration.PublishedVersionId`/`DraftVersionId` → `MobileConfigurationVersion` (both
`Restrict`), and `MobileConfigurationVersion.MobileConfigurationId` → `MobileConfiguration`
(`Cascade`, the owning direction) — a genuine table-level cycle, safely broken because only one
direction cascades. `dotnet ef migrations add` resolved the creation order itself with no
hand-editing: `CREATE TABLE mobile_configuration_versions` first (no FK yet), then `CREATE TABLE
mobile_configurations` (both Restrict FKs), then a trailing `ALTER TABLE ... ADD CONSTRAINT`
for the Cascade FK once both tables exist. Confirmed this is a SQL-Server-only restriction, not a
PostgreSQL one — the migration applied and rolled back cleanly against the real dev Postgres.

## RLS

Canonical triad on all three tables (`tenant_isolation` with `NULLIF` guard, `provider_bypass` as
`IN ('provider','provider_admin')`, `worker_bypass`), same shape as the most recent precedent
(`AddBannersSchema`). `MobileConfigurationVersion`/`MobileTheme` denormalize `TenantId` directly
(no EXISTS-join needed) — same treatment as `LoyaltyLedgerEntry.TenantId`.

Verified live via `\d+` on all three tables (not just by reading the migration): all three
policies present, `FORCE ROW LEVEL SECURITY` set, exact FK/Restrict/Cascade shape matches the
entity design. Table ownership confirmed `shelfguard_app_dev` (the real app role) on all three —
no `crm`-superuser grant-fix needed, since these are brand-new empty tables (no
FK-validation-under-RLS false positive).

## Verification

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning).
- `dotnet ef database update` on dev DB (docker `crmproductsystems-postgres-1`, port 5435,
  `shelfguard_app_dev` role) — applied cleanly.
- Rollback: `dotnet ef database update AddTenantLogoUrlUpdatedAt` — all 3 tables + policies
  removed cleanly (`pg_tables` confirmed 0 rows). Re-applied forward — all 3 tables + policies
  back, confirmed via `\d+`.
- `dotnet test` — 1411/1411 passed (same count as the TASK-527/528 baseline already in this
  working tree; includes the dynamic `RlsCrossTenantIntegrationTests` suite that enumerates every
  `FORCE ROW LEVEL SECURITY` table at query time — no new failures).
- Same `dotnet ef` tooling quirk as TASK-527: design-time factory ignores
  `appsettings.Development.json`, needs `ConnectionStrings__DefaultConnection` env var exported
  pointing at `shelfguard_app_dev@localhost:5435/crm`.

## Files changed

- `backend/ShelfGuard.Domain/Entities/MobileConfiguration.cs` (new)
- `backend/ShelfGuard.Domain/Entities/MobileConfigurationVersion.cs` (new)
- `backend/ShelfGuard.Domain/Entities/MobileTheme.cs` (new)
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260817090727_AddMobileConfigurationDomain.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260817090727_AddMobileConfigurationDomain.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `.claude/docs/database-schema.md`
- `.claude/docs/domain-model.md`
