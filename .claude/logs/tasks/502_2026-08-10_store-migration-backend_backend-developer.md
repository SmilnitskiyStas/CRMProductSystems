# TASK-502: Store-migration backend (RFM dashboard, "flickering-moseying-fountain" plan)

**Agent:** backend-developer
**Date:** 2026-08-10
**Status:** done

## ID note

Brief referred to this as TASK-480 in the plan doc — renumbered to TASK-502 per orchestrator
instruction (480 already used by unrelated 2026-08-07 work; see TASK-501's log for the full
collision explanation). No 480-483 files touched.

## What was built

New, entirely additive "customer store migration" feature on the RFM marketing-analytics
dashboard: first-store→last-store detection per customer within a period, aggregated flow
matrix, per-store net gain/loss, customer drill-down, PII-masked Excel export.

**DTOs** (`Features/MarketingAnalytics/Dtos/MarketingAnalyticsDtos.cs`): `StoreMigrationOverviewDto`,
`StoreMigrationFlowDto`, `StoreNetFlowDto`, `StoreMigrationCustomerRowDto`,
`ExportStoreMigrationRequest` — exact shapes from the brief.

**Repository** (`IMarketingAnalyticsRepository.cs` + `MarketingAnalyticsRepository.cs`):
- `GetActivePeriodCustomerCountAsync` — new, not named in the brief but required to compute
  `ActiveCustomerCount`/`MigratedSharePercent` (period-scoped variant of
  `GetCustomerBaseCountsAsync`'s `EverPurchasedCount`, same shape, date-filtered).
- `GetStoreMigrationFlowsAsync` — `DISTINCT ON (cust_id) ... ORDER BY cust_id, created_at [ASC/DESC]`
  first/last-store CTE (matches TASK-501's `idx_pos_tx_customer_migration` index shape exactly),
  `WHERE first_store <> last_store`, store filter = from-OR-to, joins `locations` for names.
- `GetStoreMigrationCustomersAsync` — same CTE, per-customer rows, joins `customers` (tenant-scoped),
  `ORDER BY to_created_at DESC LIMIT {n}` (caller picks the limit — small for on-screen, large for export).

**Service** (`IMarketingAnalyticsService.cs` + `MarketingAnalyticsService.cs`):
- `GetStoreMigrationAsync` — calls flows + active-count, derives `NetFlowByStore` and
  `MigratedCustomerCount` (= sum of flow `CustomerCount`, each migrated customer is in exactly
  one cell) in C#, guards div-by-zero for `MigratedSharePercent`.
- `GetStoreMigrationCustomersAsync` (service) — always masks Phone/Email via `PiiMasking`, no
  unmask path (on-screen table is masked-by-default per plan, with no bypass).
- `ExportStoreMigrationAsync` + `BuildStoreMigrationExcel` (9 columns: Ім'я, Телефон, Email,
  Заклад/Дата першої й останньої покупки, К-сть чеків, Сума) + new `LogExportAsync` overload
  (same as the existing one, minus the `RfmSegmentKey`/`segment=` meta field — this export has no
  segment concept). Action string: `marketing_analytics.export_store_migration`.

**Controller** (`MarketingAnalyticsController.cs`):
- `GET /api/marketing-analytics/store-migration` → `StoreMigrationOverviewDto`.
- `GET /api/marketing-analytics/store-migration/customers?...&limit=` → `IReadOnlyList<StoreMigrationCustomerRowDto>`
  (**deviation from the brief — see below**).
- `POST /api/marketing-analytics/exports/store-migration` → same `File(...)` shape as the 3
  existing exports, `UnmaskPii` gated by `MarketingAnalyticsAuthorization.CanExportPii(User)`.

## Deviation from the brief (and why)

The brief's Controller section listed only 2 endpoints (GET overview, POST export). But the
Repository section explicitly requires `GetStoreMigrationCustomersAsync` to serve "both the
on-screen table and the export, with different limits," and the task's own opening goal states
a "drill-down customer list" as a first-class deliverable, not just an export input. Nothing in
the brief's Service/Controller sections wired that on-screen path to an endpoint — it would have
been dead code otherwise. Added a third GET (`store-migration/customers`) as the objective
completion of what the lower layers already expose, following this file's own existing pattern
of separate drill-down GETs (`segments/{key}/products/{productName}/affinity`, `/basket`). PII is
always masked here (no unmask option) — unmasking stays exclusively behind the audited,
capability-gated export, per the plan's "masked-by-default" description of the on-screen table.
Flagged explicitly here and in the handoff so frontend-developer (TASK-503) has the real contract.

## Build / test

- `dotnet build ShelfGuard.sln` — 0 errors, 0 warnings (final run; one pre-existing unrelated
  warning in `MarketplaceServiceTests.cs:534` shows up on a clean/full rebuild only).
- Postgres on port 5435 **was** reachable this session — ran `dotnet ef database update` to apply
  TASK-501's pending index migration, then `dotnet test --filter "FullyQualifiedName~MarketingAnalytics"`:
  **250/250 passed** (0 skipped), live-DB integration tests included, not skipped.
- New repo integration tests (in `MarketingAnalyticsRepositoryIntegrationTests.cs`, extended the
  shared fixture with 2 more locations + CustomerE (single-store, excluded) + CustomerF
  (3-store, first/last resolved ignoring the middle store)):
  - exclude single-store customer from flows/customers
  - 3-store customer resolves first/last correctly, ignoring the middle store, correct aggregate
    txn count/revenue
  - store filter matches on from-store-only and to-store-only, excludes the ignored middle store
  - `GetActivePeriodCustomerCountAsync` basic count check
- **Fixed a pre-existing test**: adding CustomerE/F to the shared fixture changed
  `GetCustomerBaseCountsAsync_splits_registered_vs_ever_purchased`'s expected counts (4→6
  registered, 3→5 ever-purchased) — updated the assertion, not the production code.
- New service unit tests (`MarketingAnalyticsServiceTests.cs`, NSubstitute): NetFlow derivation
  (3-store cycle, verified Gained/Lost/Net per store), zero-active-customers no-divide-by-zero,
  on-screen masking (no unmask path), export masked-by-default/unmasked-when-requested +
  audit-log call count.

## Not touched (per brief scope)

Frontend (`frontend/`), `.claude/docs/*` — both explicitly out of scope for this task.

## Files

- `backend/ShelfGuard.Application/Features/MarketingAnalytics/Dtos/MarketingAnalyticsDtos.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/IMarketingAnalyticsRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/MarketingAnalyticsRepository.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/IMarketingAnalyticsService.cs`
- `backend/ShelfGuard.Application/Features/MarketingAnalytics/MarketingAnalyticsService.cs`
- `backend/ShelfGuard.Api/Controllers/MarketingAnalyticsController.cs`
- `backend/ShelfGuard.Tests/Infrastructure/MarketingAnalyticsRepositoryIntegrationTests.cs`
- `backend/ShelfGuard.Tests/MarketingAnalytics/MarketingAnalyticsServiceTests.cs`

## Handoff

`.claude/logs/handoffs/502-to-503_backend-developer.md` — endpoint routes, query params, DTO
shapes for frontend-developer.
