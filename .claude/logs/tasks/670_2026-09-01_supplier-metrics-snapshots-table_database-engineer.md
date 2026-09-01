# TASK-670: `supplier_metrics_snapshots` table for supplier-metric history

**Agent:** database-engineer · **Date:** 2026-09-01 · **Status:** done (committed to main)
DB schema only. Feeds a planned buyer-facing metric trend-chart detail page; the nightly
supplier-metrics worker job will upsert one row per supplier per day.

## Зроблено

### Entity
- `backend/ShelfGuard.Domain/Entities/SupplierMetricsSnapshot.cs` — new. Column set mirrors
  `SupplierMetrics` (same doc-comment style): `Id`, `SupplierId`, `TenantId`, `SnapshotDate`
  (`DateOnly`), `AvgDeliveryDays`, `OrderAccuracy`, `QualityScore`, `Rating`, `CancellationRate`,
  `ResponseTimeHours`, `DeliverySampleSize`, `ResponseSampleSize`, `CreatedAt` (`DateTimeOffset`).
  Metric fields `set` (worker upsert overwrites same-day re-runs); identity/date/CreatedAt `init`.
  `Supplier?` / `Tenant?` nav props, `.WithMany()` (no inverse collection) — same as `SupplierMetrics`.

### AppDbContext
- `DbSet<SupplierMetricsSnapshot> SupplierMetricsSnapshots` added next to `SupplierMetrics`.
- Config block placed immediately after the `SupplierMetrics` block. Column types copied verbatim
  (`numeric(5,2)` / `numeric(5,4)` / `numeric(3,2)` / `numeric(6,2)` / `integer`), `SnapshotDate`
  `date`, `CreatedAt` `NOW()` default. Unique index `(SupplierId, SnapshotDate)` named
  `idx_supplier_metrics_snapshots_supplier_date`; non-unique `(TenantId)`. FK: SupplierId CASCADE,
  TenantId RESTRICT.

### Migration
`20260901193439_AddSupplierMetricsHistory` (prev: `20260831090731_AddSupplierPerformanceData`).
`CreateTable` + 2 indexes + raw-SQL RLS block. Class-level XML doc notes the explicit-triad
requirement (`feedback-rls-worker-bypass-missing`) and that the policy SQL is verbatim from the
live `supplier_metrics` policies (no `WITH CHECK`). `Down()` = drop 3 policies + `DISABLE ROW
LEVEL SECURITY` + `DropTable`.

## Decisions
- **No dedicated `(SupplierId, SnapshotDate DESC)` index.** The unique ascending index fully serves
  the history query `WHERE SupplierId = ? ORDER BY SnapshotDate DESC` via a backward b-tree scan.
- **UNIQUE `(SupplierId, SnapshotDate)`**, not `(TenantId, SupplierId, SnapshotDate)` — `SupplierId`
  is globally unique (PK of `suppliers`), so the tenant prefix adds nothing. Matches the task spec.
- **`CreatedAt` = `DateTimeOffset` / `timestamp with time zone`** — mirrors `SupplierMetrics.UpdatedAt`
  rather than `StockStatusSnapshot.CreatedAt` (`DateTime`), per "mirror the SupplierMetrics style".
- FK on `SupplierId` is `CASCADE` (matches `supplier_metrics`); `TenantId` `RESTRICT`.

## Verification
- `dotnet build ShelfGuard.sln` — 0 errors, 1 pre-existing warning (`MarketplaceServiceTests.cs:895`,
  unrelated).
- `dotnet ef migrations script 20260831090731_AddSupplierPerformanceData AddSupplierMetricsHistory`
  — generates cleanly.
- Applied to dev DB (`crmproductsystems-postgres-1` :5435, `crm` DB) via the non-superuser
  `shelfguard_app_dev` connection (`dotnet ef database update --connection ...`). Brand-new empty FK
  columns don't trip the FK-validation-under-RLS false positive, so no `crm` superuser escape hatch
  needed; table owned by `shelfguard_app_dev`.
- `SELECT * FROM pg_policies WHERE tablename='supplier_metrics_snapshots'` → 3 rows
  (`tenant_isolation` w/ NULLIF, `provider_bypass` IN ('provider','provider_admin'), `worker_bypass`),
  all `cmd=ALL`, no `with_check`. `pg_class`: `relrowsecurity=t`, `relforcerowsecurity=t`.
- `Down()` round-trip via the same app role: reverted → `to_regclass` NULL + 0 policies → re-applied
  → 3 policies restored. Dev DB left **migrated** (at `AddSupplierMetricsHistory`).
- `dotnet test --filter "FullyQualifiedName~Rls|FullyQualifiedName~ForceRls"` — **61/61 passed, 0
  skipped**. `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass`
  passes with the new FORCE-RLS table present (test is dynamic — enumerates `pg_class` FORCE-RLS
  tables, no allow-list to edit).
- Full `dotnet test` — **2158/2158 passed, 0 skipped**.

## Commit
`feat(db): supplier_metrics_snapshots table for metric history (TASK-670)` on `main` (not pushed).
