# TASK-608: Multi-store query params for Stock/Analytics endpoints

**Agent:** backend-developer
**Status:** done

## What changed

Widened 5 endpoints from singular `store_id`/`storeId` (`Guid?`) to repeated `storeIds` (`Guid[]?`, null/empty = all stores), matching the `MarketingAnalyticsController`/`EventRepository` precedent (`storeIds.Contains(...)` filter).

**StockController.cs**: `GetAll` (`GET /api/stock`), `GetSummary` (`/summary`), `GetZonesSummary` (`/zones-summary`) → `IStockService.GetPagedAsync/GetSummaryAsync/GetZonesSummaryAsync` → `IStockRepository.GetPagedAsync/GetStatusCountsAsync/GetStockByZoneRawAsync` (EF `Contains` filter). `GetExpiring/GetExpired/GetNeedsCheck/GetSuggestions` untouched.

**AnalyticsController.cs**: `expiry-summary/compare` and `dashboard/weekly-kpi` → `IAnalyticsService.GetExpirySummaryComparisonAsync/GetWeeklyKpiAsync`.

These two service methods are backed by `IAnalyticsRepository.GetExpirySummaryAsync/GetPosSummaryAsync/GetWriteOffAnalyticsAsync`, which are also used by other unrelated, unchanged endpoints (`expiry-summary`, `write-offs`, `pos/summary`, and their comparison variants). Rather than touch those endpoints' contracts, I widened the 3 repo methods to `Guid[]? storeIds` (Contains filter) and had every other still-singular `AnalyticsService` caller wrap its `Guid? storeId` into a one-element array via a new `AsArray()` helper before calling in — behavior-preserving, zero HTTP contract change for the 8 other analytics actions. `GetPosSummaryAsync`'s private `BuildPosTransactionQuery` helper got a second `Guid[]?` overload (the original singular overload stays for the 6 other unrelated callers). Snapshot comparison in `GetExpirySummaryComparisonAsync` now always calls `GetByTenantAndDateAsync` and filters in memory by `storeIds` when present (dropped the single-store `GetAsync` branch — store counts per tenant are small).

## Build / tests

- `dotnet build`: succeeds, 0 errors.
- Fixed compile breaks: 3 hand-written `IStockRepository` test fakes (`FiscalizationRetryTests.cs`, `PosServiceTests.cs`, `PosConcurrencySalesIntegrationTests.cs`) needed their `GetPagedAsync/GetStatusCountsAsync/GetStockByZoneRawAsync` signatures updated to `Guid[]? storeIds`.
- Fixed 1 test assertion: `PosAnalyticsServiceTests.cs` — `Arg.Any<Guid?>()` → `Arg.Any<Guid[]?>()` for the repo-level `GetPosSummaryAsync` mock.
- `dotnet test --filter "FullyQualifiedName~Stock"`: 80/80 passed.
- `dotnet test --filter "FullyQualifiedName~Analytics"`: 303/303 passed.
- Full suite (`dotnet test`): 1837/1837 passed.

## Not done (frontend, per brief)

Frontend call-site migration (`dashboard.ts`, `useStock.ts`, `stock.ts`, `ProductTrendPanel.tsx`, `locations/api/locations.ts`, and the Dashboard/Analytics pages switching off `usePrimaryStoreId()`) is owned by a parallel frontend-developer agent — not touched here.
