# TASK-691 — Bulk write-off approval hang (39+ items)

**Reported by:** user — approving a write-off with 39 pending items hung for 5+ minutes;
approving a write-off with few items was instant.
**Done by:** main session (localized backend fix, no spawn — single feature, single layer).
**Status:** done, not pushed to remote yet.

## Root cause

`WriteOffService.ApproveAsync` (`backend/ShelfGuard.Application/Features/WriteOffs/WriteOffService.cs`)
issued one DB round trip **per line item** inside its `foreach` loop:
- `GetStockByIdAsync` for items with an explicit `ProductStockId`.
- `GetFefoOrderedAsync` for items without one (FEFO consumption path — the only shape the
  mobile "quick write-off" flow sends).

For 1–2 items this is unnoticeable. For a 39-item write-off it's 39+ sequential queries in the
same `DbContext`, and — worse — every one of those mid-loop queries forces EF Core to run
change detection / relationship fixup over an ever-growing set of already-tracked entities
(each prior batch + its movement rows), so cost grows with item count instead of staying flat.
That combination is what turned "many items" into a multi-minute hang while "few items" stayed
fast.

## Fix

Batch-load everything the loop needs **before** mutating anything, instead of querying inside
the loop:
- `IWriteOffRepository.GetStockByIdAsync` / `GetFefoOrderedAsync` (single-id) replaced with
  `GetStocksByIdsAsync` / `GetFefoOrderedForProductsAsync` (batch, `IReadOnlyCollection<Guid>` —
  matches the existing batch-param convention used by `IStockRepository`, `IBufferRepository`, etc.).
- `ApproveAsync` now does at most 3 queries total regardless of item count: one `GetByIdAsync`
  for the write-off, one batch stock lookup for all explicit-`ProductStockId` items, one batch
  FEFO lookup (grouped by `ProductId` in memory afterward) for all no-batch items.
- Same FEFO-carries-over-between-items behavior preserved: batches are the same in-memory
  objects reused across items sharing a product, so item #2 sees item #1's deduction exactly
  like the old identity-map-based behavior did.
- Same error messages / control flow (`"Stock batch {id} not found."`,
  `"Insufficient quantity in batch {id}..."`, `"Insufficient stock for product {id}..."`)
  — purely a data-access restructuring, no business-logic change.

## Files changed

- `backend/ShelfGuard.Domain/Interfaces/IWriteOffRepository.cs` — swapped 2 single-id methods for 2 batch methods.
- `backend/ShelfGuard.Infrastructure/Data/Repositories/WriteOffRepository.cs` — batch query implementations.
- `backend/ShelfGuard.Application/Features/WriteOffs/WriteOffService.cs` — `ApproveAsync` batch-loads before the loop.
- `backend/ShelfGuard.Tests/WriteOffs/WriteOffServiceTests.cs` — updated 4 existing `ApproveAsync` tests to stub the batch methods; added `ApproveAsync_ManyItemsWithoutStockRef_BatchLoadsFefoBatchesOnce` asserting the FEFO batch query is called **exactly once** for a 5-item write-off (regression guard against reintroducing the per-item query).

## Verification

- No `dotnet` SDK available in this session's environment — could not run `dotnet build` /
  `dotnet test` directly. Reviewed the diff manually against the codebase's existing batch-query
  conventions (`StockRepository.GetDeficitStocksForProductsAsync`-style `.Contains()` pattern) and
  traced every call site of the two removed interface methods (`IWriteOffRepository` is only
  referenced by `WriteOffService.cs` and its test file — confirmed via grep, no other callers to
  break).
- **Follow-up needed:** run `dotnet build` and `dotnet test --filter WriteOffs` in an environment
  with the .NET SDK before merging, to confirm this compiles and the updated/new tests pass.
