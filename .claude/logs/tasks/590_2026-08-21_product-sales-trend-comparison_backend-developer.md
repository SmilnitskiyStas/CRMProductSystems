# TASK-590 — Product sales trend vs. baseline comparison (Events calendar)

**Status:** done · **Agent:** backend-developer

## Context
Events calendar page needs, for a product linked to a demand event (e.g. Easter → paska
bread), a sales comparison: event date window vs. equal-length baseline period immediately
preceding it, sourced from live POS transactions via the existing Analytics feature (not
`DailySale`). Backend-only; frontend team builds against this contract separately.

## What changed
Extended the existing `GET /api/analytics/pos/products/{productId}/trend` action (TASK-482) —
no new route. Reused the existing `ResolveCompareRange` helper (TASK-336 baseline convention)
and `PercentChange` helper unchanged.

**New query params** on `GetProductSalesTrend`: `compare` (bool, default `false`), `compareFrom`,
`compareTo` (optional `DateOnly`). `compare=false` behavior is byte-for-byte unchanged from
before. `compare=true` → 200 with `ProductSalesTrendComparisonDto`; 404 unchanged (product not
found in tenant scope).

**New DTO** `ProductSalesTrendComparisonDto` (`backend/ShelfGuard.Application/Features/Analytics/Dtos/PosAnalyticsDtos.cs`):
```csharp
public sealed record ProductSalesTrendComparisonDto(
    Guid ProductId, string ProductName,
    IReadOnlyList<ProductSalesTrendPointDto> Current,
    IReadOnlyList<ProductSalesTrendPointDto> Comparison,
    string GroupBy,
    DateOnly From, DateOnly To, DateOnly CompareFrom, DateOnly CompareTo,
    decimal CurrentTotalRevenue, decimal ComparisonTotalRevenue, decimal? RevenuePercentChange,
    decimal CurrentTotalQuantity, decimal ComparisonTotalQuantity, decimal? QuantityPercentChange);
```
`Comparison` is never null — an empty list when the baseline window had zero sales (routine,
not an error). `*PercentChange` is null when the corresponding baseline total is zero (existing
`PercentChange` helper convention, unchanged).

**New service method** `IAnalyticsService`/`AnalyticsService`:
```csharp
Task<ProductSalesTrendComparisonDto?> GetProductSalesTrendComparisonAsync(
    Guid? tenantId, Guid? storeId, Guid productId, DateOnly from, DateOnly to, string groupBy, bool includeMargin,
    DateOnly compareFrom, DateOnly compareTo, CancellationToken ct = default);
```
Calls `IAnalyticsRepository.GetProductSalesTrendAsync` twice (current + baseline range) —
same "call the repo twice and zip" pattern as `GetPosRevenueTrendComparisonAsync`. No repository
or SQL changes. Returns `null` only if the current-window call returns null; a null/empty
comparison-window result is treated as zero totals, never throws.

Deliberately no batched multi-product endpoint — stays single-product; frontend calls it once
per linked product via parallel hooks.

## Files
- `backend/ShelfGuard.Application/Features/Analytics/Dtos/PosAnalyticsDtos.cs`
- `backend/ShelfGuard.Application/Features/Analytics/IAnalyticsService.cs`
- `backend/ShelfGuard.Application/Features/Analytics/AnalyticsService.cs`
- `backend/ShelfGuard.Api/Controllers/AnalyticsController.cs`
- `backend/ShelfGuard.Tests/Analytics/PosAnalyticsServiceTests.cs` (+5 tests)

## Build/Test
- `dotnet build ShelfGuard.sln` — clean, 0 errors (1 pre-existing unrelated warning in
  MarketplaceServiceTests.cs).
- `dotnet test --filter "FullyQualifiedName~Analytics"` — 303/303 passing.
- `dotnet test` (full suite) — 1790/1790 passing.

## Notes for frontend
Call `GET /api/analytics/pos/products/{productId}/trend?compare=true&from=...&to=...` (optionally
`compareFrom`/`compareTo` to override the auto baseline). Sum totals and percent-change are
pre-computed server-side — no need to sum `Current`/`Comparison` points client-side. `Current`/
`Comparison` point arrays are not zero-filled for gap days and not equal length — align by
day-offset from `From`/`CompareFrom`, not raw array index (same convention as
`PosRevenueTrendComparisonDto`).

## Note on task numbering
Originally logged as TASK-588 by the agent (this task ran in an isolated worktree in parallel
with another backend agent doing TASK-588 — Events coefficient removal — and both checked the
task-number max before either had committed, causing a collision). Renumbered to TASK-590 when
merging the worktree's changes into the main tree (588 and 589 were already taken by the other
two Wave 1 agents). Code comments and this log were updated to reference TASK-590 consistently.
