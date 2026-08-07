# TASK-479: `pos_transaction_items` product-covering index

**Agent:** database-engineer
**Date:** 2026-08-07
**Status:** done

## Context

Plan: `C:\Users\stass\.claude\plans\iterative-purring-sifakis.md` (interactive analytics + margin
initiative). First task, no dependencies — prep index for TASK-482's per-product sales trend
endpoint (not built yet).

## Done

- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs` — `PosTransactionItem` fluent config,
  new composite covering index:
  ```csharp
  e.HasIndex(i => new { i.ProductId, i.TransactionId })
   .HasDatabaseName("idx_pos_transaction_items_product_covering")
   .IncludeProperties(i => new { i.Quantity, i.PriceFinal });
  ```
- Migration `AddPosTransactionItemProductCoveringIndex` (`20260807081156`), generated via
  `dotnet ef migrations add` (not hand-written).
- Live-applied to local dev DB via the app's own non-superuser `shelfguard_app_dev` connection.
  `pg_indexes` confirms exact shape: `("ProductId","TransactionId") INCLUDE ("Quantity",
  "PriceFinal")`.
- `.claude/docs/database-schema.md` — new `## TASK-479` section documenting the index and the
  drop decision below.

## Redundant-index decision: dropped `IX_pos_transaction_items_ProductId`

Same migration, mirroring `20260618153017_AddPerformanceIndexes`'s precedent (which dropped the
old plain `TransactionId` index once its covering replacement existed). Not hand-added — EF Core's
`ForeignKeyIndexConvention` generated the `DropIndex` automatically the moment the new composite
index was added to the model, because `ProductId` (the FK to `Item`) is already the composite
index's leading column, so EF no longer requires its own single-column index for it.

Verified this is safe, not just convenient:
- Grepped every call site touching `pos_transaction_items`/`PosTransactionItems`
  (`AnalyticsRepository.cs`, `MarketingAnalyticsRepository.cs`, `AudienceBuilderRepository.cs`,
  `PosService.cs`) — none filter by `ProductId` alone; all go through `TransactionId` or a join to
  `pos_transactions` first. No existing query used the plain index as its access path.
- `ProductId → Item` is `OnDelete(Restrict)` — the FK-check-on-delete use case is still served
  since `ProductId` remains the new index's leading column.
- `IX_pos_transaction_items_ProductStockId` untouched — different FK, out of scope.

EXPLAIN ANALYZE not run against real volume: `pos_transactions`/`items`/`pos_transaction_items`
are all 0 rows in local dev right now, so a planner would correctly pick Seq Scan regardless of
indexes present — no meaningful signal to capture today. Real plan verification deferred to
TASK-482 once the actual query exists, per the plan doc's own verification checklist.

## Build/test

- `dotnet build ShelfGuard.sln` — 0 errors (1 pre-existing unrelated warning,
  `MarketplaceServiceTests.cs`).
- `dotnet test ShelfGuard.sln` — 1314/1314 green, no regressions.

## Not in scope (per brief)

No C# application/controller/DTO code touched — that's TASK-480/481/482.

## Files

- `backend/ShelfGuard.Infrastructure/Data/AppDbContext.cs`
- `backend/ShelfGuard.Infrastructure/Migrations/20260807081156_AddPosTransactionItemProductCoveringIndex.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/20260807081156_AddPosTransactionItemProductCoveringIndex.Designer.cs` (new)
- `backend/ShelfGuard.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (regenerated, `PosTransactionItem` index metadata only)
- `.claude/docs/database-schema.md`
