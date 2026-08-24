# TASK-610: Multi-store query params for Analytics/Sales/AI-Orders endpoints

**Agent:** backend-developer
**Status:** done

## What changed

Follow-up to TASK-608 (Stock/Dashboard-Analytics). Widened `store_id`/`storeId` (`Guid?`) →
repeated `storeIds` (`Guid[]?`, null/empty = all stores, `.Contains()` filter) on the real
Analytics page, Sales page, and AI-Orders page endpoints.

**AnalyticsController.cs — 8 endpoints**: `expiry-summary`, `write-offs` (incl. `compare=true`),
`by-zone`, `by-category`, `by-category/products`, `losses` (incl. `compare=true`),
`losses/by-product`, `losses/trend`. Threaded through `IAnalyticsService`/`AnalyticsService`
and `IAnalyticsRepository`/`AnalyticsRepository`. `GetLossesAsync`, `GetByZoneAsync`,
`GetByCategoryAsync`, `GetLossesByProductAsync`, `GetLossesTrendAsync`,
`GetCategoryProductBreakdownAsync` were not yet array-capable at the repo level (unlike
`GetExpirySummaryAsync`/`GetPosSummaryAsync`/`GetWriteOffAnalyticsAsync`, widened by TASK-608) —
widened all 6 following `EventRepository.GetAsync`'s `storeIds.Contains(...)` pattern.
`GetWriteOffAnalyticsComparisonAsync`/`GetLossesComparisonAsync` (the `compare=true` service
methods) also widened; `AnalyticsService.AsArray()` helper now only wraps for
`GetPosSummaryAsync`/`GetPosSummaryComparisonAsync` (pos/summary — out of scope, unchanged).

`GetCategoryProductBreakdownAsync`'s per-row `DaysOfStockRemaining` (ADU-based) field stays
populated only when `storeIds is { Length: 1 }` — a 2+-store selection now gets the same
"no single meaningful ADU to divide by" null treatment the pre-existing no-store-scope case
already had. No cross-store ADU aggregation invented (per brief).

Untouched, per brief: `movements`, `pos/*`, `losses/by-product`'s reason handling,
`pos/products/{id}/trend` — all still singular `store_id` (unchanged HTTP contracts).

**DailySalesController.cs**: `GET /api/daily-sales` (`Get` list action) only, through
`IDailySalesService`/`DailySalesService` → `IDailySalesRepository`/`DailySalesRepository`.
`upsert`/`import` untouched (single-store writes, unchanged).

**AiOrdersController.cs**: `GET /api/ai-orders` (`GetList`) only, through `IAiOrderService`
→ `IAiOrderRepository`/`AiOrderRepository`. `generate`/`updateItem`/`accept`/`reject`
untouched (single-store writes/actions, unchanged).

**Orders page**: no backend changes — confirmed no GET/list endpoint exists (mutation-only,
inherently single-store), per brief.

## Build / tests

- `dotnet build`: succeeds, 0 errors.
- Fixed compile breaks in `PosAnalyticsServiceTests.cs`: 4 hand-written `_repo.GetXxxAsync(...)`
  call sites in `GetCategoryProductBreakdownAsync`/`GetLossesByProductAsync`/
  `GetLossesTrendAsync` store-filter tests passed a raw `Guid` where the mock now expects
  `Guid[]?`. Switched those call sites to `Arg.Is<Guid[]>(a => a.Length == 1 && a[0] == storeId)`
  for both the `.Returns()` setup and `Received()` verification — a raw `new[] { storeId }`
  array literal compiles but silently fails to match under NSubstitute's default (by-reference)
  array equality, which produced 4 `NullReferenceException` failures on first test run before
  the fix (mock returned null instead of the stubbed DTO). Matches the existing
  `AiOrderServiceTests.cs` precedent for `Guid[]` argument matching.
- `dotnet test --filter "FullyQualifiedName~Analytics"`: 303/303 passed.
- `dotnet test --filter "FullyQualifiedName~DailySales"`: 5/5 passed.
- `dotnet test --filter "FullyQualifiedName~AiOrder"`: 10/10 passed.
- Full suite (`dotnet test`): 1837/1837 passed (same total as TASK-608 — no new tests added,
  only existing signatures widened).

## Not done (frontend, per brief)

Frontend call-site updates for the Analytics/Sales/AI-Orders pages are a separate, already-
planned follow-up task — not touched here.

## Note

Working tree also had pre-existing uncommitted changes from TASK-608
(StockController/StockService/StockRepository/IStockRepository, 3 test fakes) and an unrelated
barcode-search task (`ItemRepository.cs`,
`ItemRepositoryGetPagedBarcodeSearchIntegrationTests.cs`) present before this session started —
left untouched, not part of this task's diff.
