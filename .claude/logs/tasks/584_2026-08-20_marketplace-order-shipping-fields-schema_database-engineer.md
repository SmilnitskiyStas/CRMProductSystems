# TASK-584 — Marketplace order shipping fields (schema layer)

**Agent:** database-engineer
**Status:** done
**Scope:** entity fields + EF migration only (part 1 of 3; backend/frontend follow in separate sessions)

## What changed

`backend/ShelfGuard.Domain/Entities/MarketplaceOrder.cs` — added 3 nullable properties, following the existing style (plain `{ get; set; }`, set by service layer not client):
- `DateTimeOffset? ShippedAt`
- `int? EstimatedDeliveryDays`
- `DateTimeOffset? DeliveredAt`

New EF Core migration: `20260820131503_AddMarketplaceOrderShippingFields`
(`backend/ShelfGuard.Infrastructure/Migrations/20260820131503_AddMarketplaceOrderShippingFields.cs` + `.Designer.cs`).
Adds exactly 3 nullable columns to `marketplace_orders`: `ShippedAt timestamptz`,
`EstimatedDeliveryDays integer`, `DeliveredAt timestamptz`. No indexes, no other schema
changes. `AppDbContextModelSnapshot.cs` diff confirmed to be exactly these 3 properties
(9 lines added) — no unrelated model drift picked up.

## RLS

No policy change made or needed. Verified directly on the dev DB after migration
(`\d marketplace_orders`): the table's existing 3 row-level policies
(`tenant_isolation` on `SupplierTenantId OR ClientTenantId`, `provider_bypass`,
`worker_bypass`) apply to the whole row — Postgres RLS has no per-column granularity,
so the 3 new columns are automatically covered by all 3 existing policies with no
migration-side action required.

## Build / migration / test verification

- `dotnet build` (full `/backend` solution): clean, 0 errors (1 pre-existing unrelated
  warning in `MarketplaceServiceTests.cs`).
- Migration applied to local dev DB (docker container `crmproductsystems-postgres-1`,
  port 5435) via `dotnet ef database update` — succeeded. Confirmed columns + policies
  via `\d marketplace_orders` in psql.
- `dotnet test`: 1750/1750 passed, 0 failed, 0 skipped.

## Gotcha for future agents

`dotnet ef` design-time tooling in this repo uses
`backend/ShelfGuard.Infrastructure/Data/AppDbContextFactory.cs` (an
`IDesignTimeDbContextFactory<AppDbContext>`), which does **not** read
`appsettings.Development.json`. It only reads env var `ConnectionStrings__DefaultConnection`,
falling back to a hardcoded `Host=localhost;Port=5432;Database=shelfguard_dev;
Username=postgres;Password=postgres` if unset — which does not match this repo's actual
dev Postgres (docker container on port 5435, user `shelfguard_app_dev`, see
`appsettings.Development.json`). To run `dotnet ef database update` (or any EF CLI
command that touches the DB) against the real dev DB, export
`ConnectionStrings__DefaultConnection` matching `appsettings.Development.json` first.

Also had to `Stop-Process` a stale `ShelfGuard.Api.exe` (PID 32912, started 15:52) that
was holding a file lock on the Infrastructure/Domain/Application DLLs and made
`dotnet build`/`dotnet ef` fail with MSB3027 copy errors — unrelated dev server left
running from an earlier session.

## Handoff

See `.claude/logs/handoffs/584-to-backend_database-engineer.md` for exact field
names/types for the next agent (backend-developer).
