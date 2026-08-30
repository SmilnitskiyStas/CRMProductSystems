# TASK-640 — Server-side range/category filters for 5 paginated GET endpoints

**Agent:** backend-developer · **Date:** 2026-08-30 · **Status:** done

## What changed

Added new optional query params to the `GetPagedAsync` chain (Controller → Service →
Repository) on 5 endpoints. Pure additive filtering on existing columns, no migration.

New params are appended at the very end of each method signature (still before `ct`),
all defaulted to `null`, so no pre-existing parameter's positional index shifts for any
existing caller/test — this mattered because several NSubstitute test mocks and fake
repository classes call/implement these methods positionally.

| Endpoint | New query params |
|---|---|
| `GET /api/stock` | `category_id` (Guid?), `min_quantity` (decimal?), `max_quantity` (decimal?) |
| `GET /api/items` | `min_price` (decimal?), `max_price` (decimal?) — filters `Item.PriceRetail` |
| `GET /api/receipts` | `category_id` (Guid?), `min_items` (int?), `max_items` (int?) |
| `GET /api/transfers` | `category_id` (Guid?), `min_items` (int?), `max_items` (int?) |
| `GET /api/write-offs` | `category_id` (Guid?), `min_loss_amount` (decimal?), `max_loss_amount` (decimal?) — filters `WriteOff.TotalLossAmount` |

`category_id` on Receipts/Transfers/WriteOffs matches when at least one line item's
`Product.CategoryId` (Item, via the `Product` navigation on `StockReceiptItem`/
`StockTransferItem`/`WriteOffItem`) equals the filter. All range filters use `.HasValue`
checks, never truthy/non-zero (0 is a valid bound).

## Files changed

Domain interfaces: `IStockRepository.cs`, `IItemRepository.cs`, `IReceiptRepository.cs`,
`ITransferRepository.cs`, `IWriteOffRepository.cs`.

Infrastructure repositories: `StockRepository.cs`, `ItemRepository.cs`,
`ReceiptRepository.cs`, `TransferRepository.cs`, `WriteOffRepository.cs` (only
`GetPagedAsync` touched — `GetAllAsync` untouched).

Application layer: `IStockService.cs`/`StockService.cs`, `IItemService.cs`/`ItemService.cs`,
`IReceiptService.cs`/`ReceiptService.cs`, `ITransferService.cs`/`TransferService.cs`,
`IWriteOffService.cs`/`WriteOffService.cs`.

Controllers: `StockController.cs`, `ItemsController.cs`, `ReceiptsController.cs`,
`TransfersController.cs`, `WriteOffsController.cs`.

Test fixes required by the new interface params (mechanical — matching signatures, no
logic changes): `ShelfGuard.Tests/Pos/FiscalizationRetryTests.cs`,
`ShelfGuard.Tests/Pos/PosServiceTests.cs`,
`ShelfGuard.Tests/Pos/PosConcurrencySalesIntegrationTests.cs` (fake `IStockRepository`/
`IItemRepository` implementations), `ShelfGuard.Tests/Catalog/ItemServiceTests.cs`,
`ShelfGuard.Tests/Catalog/ItemsControllerTests.cs`, `ShelfGuard.Tests/WriteOffs/WriteOffServiceTests.cs`
(NSubstitute positional calls needed 2 more `null`s before the trailing `Arg.Any<CancellationToken>()`).

New tests added (one happy-path per new filter):
`ShelfGuard.Tests/Infrastructure/ItemRepositoryGetPagedSortIntegrationTests.cs` (+2),
`StockRepositoryGetPagedSearchSortIntegrationTests.cs` (+3),
`ReceiptRepositoryGetPagedSearchSortIntegrationTests.cs` (+3),
`TransferRepositoryGetPagedSearchSortIntegrationTests.cs` (+3),
`WriteOffRepositoryGetPagedSearchSortIntegrationTests.cs` (+3).

Docs: `.claude/docs/api-contracts.md` — added full query-param lists under the 5
"Paginated endpoints" bullets.

## Build / test status

- `dotnet build` — clean, 0 errors (1 pre-existing unrelated warning).
- Filtered test run (the 5 integration test files + related unit test files): all green,
  52/52 then 184/184.
- Full `dotnet test` suite: **2014/2014 passed**, 0 failed, 0 skipped.

## Notes for frontend integration

Final query param names (verify against these): Stock `category_id`/`min_quantity`/
`max_quantity`; Items `min_price`/`max_price`; Receipts & Transfers `category_id`/
`min_items`/`max_items`; Write-offs `category_id`/`min_loss_amount`/`max_loss_amount`.
All snake_case, all optional, omitting any of them is a no-op (backward compatible).
