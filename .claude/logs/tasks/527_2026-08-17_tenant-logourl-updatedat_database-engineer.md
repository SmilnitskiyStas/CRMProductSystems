# TASK-527 — Add Tenant.LogoUrl/UpdatedAt columns

**Agent:** database-engineer
**Status:** done

## What was done

- `backend/ShelfGuard.Domain/Entities/Tenant.cs`: added `LogoUrl` (nullable `string`, private
  setter) and `UpdatedAt` (`DateTime`, private setter). Added `UpdateLogoUrl(string? logoUrl)`
  mutator following the existing `Update*` house style.
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`: configured `LogoUrl` as `text`
  (nullable, same as `Banner.ImageUrl`) and `UpdatedAt` with `HasDefaultValueSql("NOW()")` (same
  pattern as `CreatedAt`) so existing rows backfill on migration.
- Migration `20260817084551_AddTenantLogoUrlUpdatedAt` (+ Designer + model snapshot).

## UpdatedAt auto-touch decision

Touched manually inside every mutator, not via a `SaveChanges` interceptor. Reasons:
- No `ISaveChangesInterceptor`/`SavingChanges` override exists anywhere in the codebase
  (checked — zero hits).
- `Banner.cs` (most recently added entity, same private-setter/factory style as `Tenant`)
  already sets `UpdatedAt = DateTime.UtcNow;` inline in each of its 4 mutators (`Update`,
  `SetImageUrl`, `SetActive`, `Publish`). This is the established house convention (8 total
  inline `UpdatedAt = DateTime.UtcNow` assignments across entities).

Updated: `UpdatePlan`, `UpdateModules`, `UpdateBusinessType`, `Activate`, `Deactivate`, and the
new `UpdateLogoUrl` all now touch `UpdatedAt`. `Create()` sets both `CreatedAt` and `UpdatedAt`
to the same `now` value.

## Verification

- `dotnet build ShelfGuard.sln` — succeeded, 0 errors (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet ef database update` on dev DB (docker `crmproductsystems-postgres-1`, port 5435) —
  applied cleanly; confirmed `LogoUrl text` (nullable) and `UpdatedAt timestamptz not null
  default now()` columns present via `\d tenants`.
- Rollback: `dotnet ef database update AddBannerPublishedAt` — reverted cleanly, columns
  confirmed removed. Re-applied forward afterward to leave dev DB up to date.
- `dotnet test` — 1411/1411 passed, 0 failed.
- Note: `dotnet ef` design-time tooling ignores `appsettings.Development.json` and falls back to
  `AppDbContextFactory`'s hardcoded `postgres@localhost:5432/shelfguard_dev` connection string
  unless `ConnectionStrings__DefaultConnection` env var is set — had to export it pointing at
  the dev DB (`shelfguard_app_dev@localhost:5435/crm`) for `database update` to reach the right
  instance. Pre-existing tooling quirk, unrelated to this task, not fixed here.

## Files changed

- `backend/ShelfGuard.Domain/Entities/Tenant.cs`
- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260817084551_AddTenantLogoUrlUpdatedAt.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260817084551_AddTenantLogoUrlUpdatedAt.Designer.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- `.claude/docs/domain-model.md`
