# TASK-649 (T2) — `AddSupplierPerformanceData` migration + entity/DbContext edits

**Agent:** database-engineer · **Date:** 2026-08-31 · **Status:** done
**Branch:** `worktree-agent-aeb2e452339feffde` (not pushed, not merged — orchestrator merges)
**Scope:** pure additive DDL. NO new tables, NO RLS policy changes. No service/DTO/repository/
frontend/worker changes (owned by T3–T14).

## Migration

`backend/ShelfGuard.Infrastructure/Migrations/20260831090731_AddSupplierPerformanceData.cs`
(+ `.Designer.cs`, + snapshot). Previous migration: `20260831060145_AddCustomerMessageDeliveryLifecycle`.

Class-level XML-doc states explicitly: no new tables, no RLS policy changes — the 4 target
tables already carry `tenant_isolation` + `provider_bypass` + `worker_bypass` and new columns
inherit them.

### Columns added (all nullable, no FKs, no defaults)

| Table | Column | Type |
|---|---|---|
| `locations` | `RegionCode` | `varchar(20)` |
| `marketplace_orders` | `DestinationRegionCode` | `varchar(20)` |
| `supplier_profiles` | `DeliveryCoverage` | `jsonb` |
| `supplier_metrics` | `DeliveryByRegion` | `jsonb` |
| `supplier_metrics` | `DeliverySampleSize` | `integer` |
| `supplier_metrics` | `ResponseSampleSize` | `integer` |
| `supplier_metrics` | `AggregatesComputedAt` | `timestamp with time zone` |

### Indexes

- `IX_supplier_chat_messages_SessionId_SenderTenantId_CreatedAt` — EF-tracked, plain composite
  (existing `HasIndex(SessionId)` + `HasIndex(CreatedAt)` left in place; only the composite was
  missing).
- `ix_marketplace_orders_metrics` — **partial**, hand-written via `migrationBuilder.Sql(...)`
  because EF does not emit the `WHERE` filter here; not tracked in the model snapshot (same
  treatment as the project's other raw-SQL indexes/policies).
  `CREATE INDEX ix_marketplace_orders_metrics ON marketplace_orders ("SupplierTenantId","DeliveredAt") WHERE "Status" = 'delivered';`

`Down()` reverses everything symmetrically (partial index dropped via
`DROP INDEX IF EXISTS`, then EF `DropIndex` + 7 `DropColumn`).

## Deviations from the plan

- **`RegionCode` / `DestinationRegionCode` = `varchar(20)`, not the plan's `varchar(12)`.**
  Per the task brief: `varchar(12)` is too short for city codes (`UA-XX-LONGTRANSLIT`, e.g.
  `UA-12-KRYVYI-RIH` ≈ 15 chars). AppDbContext uses `HasMaxLength(20)` on both.

## Entity edits

- `Location.RegionCode` (`string?`).
- `MarketplaceOrder.DestinationRegionCode` (`string?`, snapshot — set by T3's order service).
- `SupplierProfile.DeliveryCoverage` (`string?`, raw jsonb like `Categories`).
  `SupplierProfile.DeliveryRegions` marked `[Obsolete("Superseded by DeliveryCoverage
  (TASK-649). Read-only for backfill; drop in a later migration.")]` — column KEPT, still mapped.
- `SupplierMetrics`: `DeliveryByRegion` (`string?` jsonb), `DeliverySampleSize` (`int?`),
  `ResponseSampleSize` (`int?`), `AggregatesComputedAt` (`DateTimeOffset?`).

AppDbContext: jsonb `HasColumnType` on `DeliveryCoverage` / `DeliveryByRegion`; `HasMaxLength(20)`
on the two region codes; the `DeliveryRegions` mapping line wrapped in `#pragma warning
disable/restore CS0618`.

## Build / test

- `dotnet build ShelfGuard.sln` — **0 errors**. 4 new `CS0618` warnings in
  `MarketplaceService.cs` / `SupplierCabinetService.cs` (they still read/write the now-`[Obsolete]`
  `DeliveryRegions`) — expected; T3 migrates those call sites and the warnings clear then.
  Pre-existing `CS8602` in `MarketplaceServiceTests.cs:550` unrelated.
- `dotnet ef migrations script 20260831060145_... 20260831090731_AddSupplierPerformanceData` — generates cleanly,
  `ProductVersion` `8.0.11`.
- `dotnet test --filter "…Rls|…ForceRls"` — **61 passed, 0 failed, 0 skipped** (live dev
  Postgres reachable; `AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass`
  ran and passed — no new tables, unaffected).
- `dotnet test --filter "…Marketplace|…SupplierMetrics|…SupplierProfile|…SupplierAgreement"` —
  **250 passed, 0 failed**.

## Dev DB — applied

Applied to `crmproductsystems-postgres-1` (port 5435, db `crm`) via the non-superuser
`shelfguard_app_dev` role (`dotnet ef database update --connection ...` — pure `ADD COLUMN` /
`CREATE INDEX`, no FK-under-RLS false-positive risk, so no `crm` superuser escape hatch needed).
`Down()` round-trip verified: rolled back → 0 leftover columns / 0 leftover indexes → re-applied.

Post-apply checks (all pass):
- 7 columns present with the exact types above; `ix_marketplace_orders_metrics` has the
  `WHERE ("Status")::text = 'delivered'` filter; composite chat index present.
- `pg_policies` on the 4 tables: identical before/after — 3 policies each
  (`tenant_isolation` / `provider_bypass` / `worker_bypass`).
- `relrowsecurity` / `relforcerowsecurity` = `t/t` on all 4.
- `tableowner` = `shelfguard_app_dev` on all 5 touched tables — no companion grant migration
  needed.

## Not done (out of scope, handoff)

- `MarketplaceService` / `SupplierCabinetService` / DTOs / `MarketplaceRepository` /
  `CreateOrderAsync` region snapshot — T3/T4.
- `UkraineRegions.cs`, `GeoService` — T1.
- Worker recompute job — T6.
- Docs (ADR, `database-schema.md`, `domain-model.md`, `api-contracts.md`) — T15.
