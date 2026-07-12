# TASK-335 — Stock status daily snapshots table (database-engineer, 2026-07-12)

**Status:** done (schema + repository only, per ADR-016 scope — no services/endpoints/worker)

## What
`stock_status_snapshots` table: one row per (tenant, store, day) with FEFO status
counters (Safe/Warning/Critical/Expired), so the dashboard can diff current counts
against a prior date (e.g. "vs. a week ago"). Network-wide view = SUM over rows for
a (TenantId, SnapshotDate) at query time — no separate rollup row stored.

## Files
- `ShelfGuard.Domain/Entities/StockStatusSnapshot.cs` — new entity (Id, TenantId, StoreId, SnapshotDate `DateOnly`, SafeCount/WarningCount/CriticalCount/ExpiredCount `int`, CreatedAt, nav `Store` → `Location`)
- `ShelfGuard.Domain/Interfaces/IStockStatusSnapshotRepository.cs` — `UpsertAsync`, `GetAsync` (single store+date), `GetByTenantAndDateAsync` (all stores for a date → network view)
- `ShelfGuard.Infrastructure/Data/Repositories/StockStatusSnapshotRepository.cs` — impl; `UpsertAsync` is find-then-update/insert + `SaveChangesAsync` (same convention as `DailySalesRepository`/`DailySalesService`, not raw `ON CONFLICT` SQL — no existing precedent for that in this codebase)
- `ShelfGuard.Infrastructure/Data/AppDbContext.cs` — `DbSet<StockStatusSnapshot> StockStatusSnapshots`; entity config: table `stock_status_snapshots`, `StoreId` mapped to column `LocationId` (matches existing convention on `product_stock`/`stock_movements`), FK → `locations` (Cascade)
- `ShelfGuard.Infrastructure/DependencyInjection.cs` — registered `IStockStatusSnapshotRepository`

## Store/Location naming (for backend-developer)
Domain has **both** `Store` and `Location` entities but they map to the **same**
`locations` table — `ProductStock.StoreId` already uses this pattern
(`e.Property(s => s.StoreId).HasColumnName("LocationId")`, nav type `Location`).
I followed it: C# property is `StoreId` (type `Guid`, nav `Location? Store`), DB
column is `"LocationId"`, FK target table is `locations`.

## Indexes
- Unique: `idx_stock_status_snapshots_tenant_store_date` on `(TenantId, LocationId, SnapshotDate)` — upsert idempotency key
- `idx_stock_status_snapshots_tenant_date` on `(TenantId, SnapshotDate)` — network-wide aggregation
- `IX_stock_status_snapshots_LocationId` — EF-generated FK index

## RLS
Copied the exact pattern from `AddLegalEntities` (20260708200121), which is the
project's current canonical `tenant_isolation` + `provider_bypass` + `NULLIF` guard:
```sql
ALTER TABLE stock_status_snapshots ENABLE ROW LEVEL SECURITY;
ALTER TABLE stock_status_snapshots FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON stock_status_snapshots
  USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
CREATE POLICY provider_bypass ON stock_status_snapshots
  USING (current_setting('app.role', true) = 'provider');
```
`Down()` drops both policies + disables RLS before dropping the table.

## Migration
`20260712112112_AddStockStatusSnapshots` — additive only (CreateTable + 3 indexes + RLS SQL).
Verified via `dotnet ef migrations script` that generated SQL includes `CREATE TABLE`,
both custom indexes, the FK, and both RLS policies.

## Verification
- `dotnet build` (Infrastructure, then full solution) — 0 errors, 0 new warnings
- `dotnet ef migrations script` — confirmed RLS + indexes present in generated SQL
- No `dotnet test` run — no new tests added, out of scope (schema-only task); did not touch existing test files
