# TASK-603: Write-off reimbursement + purchase-price loss — Application layer

**Status:** done
**Agent:** backend-developer
**Scope:** Application layer only (`WriteOffDtos.cs`, `WriteOffService.cs`, `ItemDto`/`ItemService`, `StockDtos.cs`/`StockService`, `WriteOffServiceTests.cs`). Domain/migration done by database-engineer (TASK-602). Web/mobile out of scope — next agent: frontend-developer.

## Changes

- `backend/ShelfGuard.Application/Features/WriteOffs/Dtos/WriteOffDtos.cs`: `WriteOffItemDto` +6 fields (`UnitPricePurchase`, `LossAmountPurchase`, `IsReturnedToSupplier`, `ReimbursementType`, `ReimbursementValue`, `ReimbursementAmount`). `WriteOffDto` +3 (`TotalLossAmountPurchase`, `TotalReimbursementAmount`, computed `NetLossAmount`). `CreateWriteOffItemRequest` +3 optional trailing params (`IsReturnedToSupplier = false, ReimbursementType = null, ReimbursementValue = null`) — kept positional-call compatibility.
- `backend/ShelfGuard.Application/Features/WriteOffs/WriteOffService.cs`:
  - Constructor now takes `IItemRepository _items` alongside `IWriteOffRepository _repo`.
  - `CreateAsync`: added `ValidReimbursementTypes` validation pass (mirrors existing `ValidReasons` pattern), bulk item lookup via `IItemRepository.GetPagedAsync(ids: distinctProductIds, page: 1, pageSize: distinctProductIds.Count, ...)`, per-item auto-fill (`unitPrice = itemReq.UnitPrice ?? item.PriceRetail`, `unitPricePurchase = item.PricePurchase` always system-snapshot), both loss amounts, reimbursement resolution (fixed/percent, falling back to `item.Default*` when not supplied), and upsert-back-to-item-default when the client sends an explicit type+value that differs from the current default (mutates the tracked `Item`, calls `_items.Update(item)`, no extra `SaveChangesAsync`).
  - `ToDto`: threads all new fields through; `NetLossAmount` computed as `TotalLossAmountPurchase - TotalReimbursementAmount`, null only when both are null.
  - `ApproveAsync`: untouched, as specified — still retail-basis (`item.UnitPrice`/`item.LossAmount`) for `StockMovement`.
- `backend/ShelfGuard.Application/Features/Catalog/Dtos/ItemDto.cs` + `ItemService.cs`: `ItemDto` +2 fields (`DefaultReimbursementType`, `DefaultReimbursementValue`) as siblings of `PricePurchase`/`PriceRetail`; `ToDto` mapping updated. `CreateProductRequest`/`UpdateProductRequest` and `CreateAsync`/`UpdateAsync` untouched (write path stays the write-off upsert-back only).
- `backend/ShelfGuard.Application/Features/Stock/Dtos/StockDtos.cs` + `StockService.cs`: `ProductStockDto` +4 fields (`PricePurchase`, `PriceRetail`, `DefaultReimbursementType`, `DefaultReimbursementValue`), mapped from `s.Product?...` in `ToDto` — no repository changes (existing `.Include(Product)` already covers it).
- `backend/ShelfGuard.Tests/WriteOffs/WriteOffServiceTests.cs`: fixed broken constructor (`new WriteOffService(_repo, _items)`), added `Substitute.For<IItemRepository>()` with a default stub (bare `Item` per requested id) plus a `StubItem(...)` helper for tests needing real prices/defaults. Added 9 new cases: unit-price auto-fill, client override, purchase-loss computation, fixed reimbursement, percent reimbursement, default-fallback resolution, `IsReturnedToSupplier=false` nulls out all 4 reimbursement fields, invalid type rejected without creating, upsert-back mutates the item's default.
- `backend/ShelfGuard.Tests/Marketplace/MarketplaceOrderServiceTests.cs`: `FakeItemDto` used a positional `ItemDto(...)` call — fixed by inserting the 2 new `null` args at the correct position (this was the only other positional-`ItemDto` construction in the test suite; found by running the full solution test build, not by grep).

## Verification

- `dotnet build` (full solution): 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs:534`).
- `dotnet test --filter "FullyQualifiedName~WriteOffs"`: **28/28 passed** (19 pre-existing + 9 new).
- `dotnet test` (full solution): **1837/1837 passed**, 0 failures.

## Deviations from brief / confirmations

- **`IItemRepository` bulk-fetch method**: used `GetPagedAsync(categoryId: null, segmentId: null, managementType: null, search: null, ids: distinctProductIds, page: 1, pageSize: distinctProductIds.Count, ct: ct)` — the plan's guess matched the real signature exactly (`GetPagedAsync(Guid?, Guid?, string?, string?, IReadOnlyList<Guid>?, int, int, CancellationToken)`), no adjustment needed. No separate "get by ids" method exists on the interface.
- **Shared-`SaveChangesAsync` assumption**: confirmed and held. Both `ItemRepository` and `WriteOffRepository` take `AppDbContext` via constructor injection (no separate contexts), and both `IItemRepository`/`IWriteOffRepository` are registered `AddScoped` in `ShelfGuard.Infrastructure/DependencyInjection.cs` against a `AddDbContext<AppDbContext>` (default scoped lifetime). Within one request scope they resolve the same `AppDbContext` instance, so the single `_repo.SaveChangesAsync(ct)` at the end of `CreateAsync` flushes both the new `WriteOff` graph and any `Item.DefaultReimbursement*` upsert together. No second `SaveChangesAsync` call added.
- No other deviations from the brief.

## Handoff

Not created per instructions — orchestrating session chains directly to `frontend-developer` for web UI work (`CreateWriteOffForm.tsx`, `types.ts`, `write-offs/page.tsx`), consuming the DTO contracts above.
