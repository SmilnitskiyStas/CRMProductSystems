# TASK-491: DaysOfStockRemaining field on by-category/products

**Agent:** backend-developer
**Date:** 2026-08-07
**Status:** done

## Context

Follow-up batch (TASK-488..495). Extends TASK-481's `GET /api/analytics/by-category/products`
(`CategoryProductRowDto`) with a days-of-stock-remaining field. TASK-490 (worst-products) had just
landed on the same shared files — read all fresh; nothing of TASK-489/490's work modified (see
Scope below).

## Verified before implementing

- `backend/ShelfGuard.Domain/Entities/ProductAdu.cs` — field is exactly `AduEffective`
  (`decimal?`), plus `Adu30d`/`Adu60d`/`Adu90d`/`ProductGroup`/`ValidDays30d`/`ValidDays60d`.
  DbSet: `_db.ProductAdus` (`AppDbContext.cs:60`).
- `AduEffective`'s meaning confirmed via `AduService.cs`/`AduCalculator.Compute`: it's the
  resolved 30/60/90-day window picked by data density (group 3/2/1 → ADU30/60/90; group null →
  `AduEffective = null`, "insufficient data"). Confirmed **can legitimately be exactly `0m`**
  (valid days with `QuantitySold == 0` but `QuantityEndOfDay > 0` still count as valid per
  `IsValidDay`) — so the null-vs-zero guard in the brief is a real case, not defensive-only.
  `AduController.cs`/`AduService.GetAsync` is the single-product precedent: returns an error
  string (controller 404s) when no `ProductAdu` row exists — same "no data yet, not an error"
  posture I followed here, just returning `null` instead of an error since this is a bulk list.
- `AduRepository.GetByStoreAsync` is the existing bulk-by-store-and-Dictionary precedent
  (`_db.ProductAdus.Where(StoreId==).ToDictionaryAsync(ProductId)`) — mirrored for the new query.

## Done

No controller/service/interface signature changes needed — `storeId` already flows all the way
into `GetCategoryProductBreakdownAsync` since TASK-481.

1. **`AnalyticsDtos.cs`** — `CategoryProductRowDto` gained `decimal? DaysOfStockRemaining` as the
   last positional field, with a doc comment stating the two independent null cases.
2. **`AnalyticsRepository.cs`**, inside `GetCategoryProductBreakdownAsync`:
   - New bulk fetch right after the existing `purchasePrices` lookup: `_db.ProductAdus.Where(a =>
     a.StoreId == storeId.Value && productIds.Contains(a.ProductId))` (+ `TenantId` filter when
     `tenantId.HasValue`, matching this file's belt-and-suspenders-on-top-of-RLS convention used
     everywhere else in it) → `ToDictionaryAsync(ProductId, AduEffective)`. Only runs when
     `storeId.HasValue && productIds.Count > 0`; otherwise `aduByProduct` stays an empty dict — one
     extra query total per request, not per product.
   - Per-row: `daysOfStockRemaining` stays `null` unless `aduByProduct` has an entry for the
     product **and** `AduEffective.HasValue && AduEffective.Value != 0m`; when both hold,
     `Math.Round(TotalQuantity / AduEffective.Value, 1)`.

## Exact contract (for TASK-494 frontend)

`CategoryProductRowDto` → JSON (camelCase, `System.Text.Json` default), new field:

```
"daysOfStockRemaining": number | null
```

- Type: `decimal?` server-side → JSON number with 1 decimal place, or `null`.
- `null` when **either**: (a) the request had no `store_id` (network/multi-store rollup — ADU is
  per-product-per-store, no single value to use), or (b) the product has no `ProductAdu` row for
  that store, or its `AduEffective` is `null`/exactly `0`.
- Never `0` as a stand-in for "unknown", never omitted, never a huge/infinite sentinel.
- When populated: `TotalQuantity / AduEffective`, rounded to 1 decimal — e.g. `12.5` means ~12.5
  days of on-hand stock left at the current sell rate.
- Field name used for the source value: **`ProductAdu.AduEffective`** — confirmed correct, matches
  what was described secondhand.

## Tests

3 new facts in `PosAnalyticsServiceTests.cs` (`GetCategoryProductBreakdownAsync_days_of_stock_
remaining_*`: populated_when_store_scoped / null_without_store_scope / null_when_adu_zero_or_
missing), plus updated the 3 pre-existing `CategoryProductRowDto` construction call sites (repo
mocks) to supply the new field. Same file-established boundary as TASK-490's own note: the actual
division/guard logic lives in the repository and isn't independently unit-tested anywhere in this
codebase (no EF-InMemory harness wired for `AnalyticsRepository` specifically); these three pin
the DTO shape/pass-through at the service layer, matching every other `GetXxxAsync` test in this
file.

## Build/test

- `dotnet build` — 0 errors, 1 pre-existing unrelated warning (`MarketplaceServiceTests.cs`).
- `dotnet test` — **1344/1344 green** (1341 baseline + 3 new). Analytics-filtered run (289 tests)
  also independently green.

## Scope check

`git diff` confirms only 3 files touched by me this session: `AnalyticsDtos.cs`,
`AnalyticsRepository.cs`, `PosAnalyticsServiceTests.cs`. `AnalyticsController.cs`/
`AnalyticsService.cs`/`IAnalyticsService.cs`/`IAnalyticsRepository.cs`/`PosAnalyticsDtos.cs` show
as modified in `git status` only because TASK-490's own edits there are still uncommitted —
verified `GetWorstProductsAsync`/`GetLossesTrendAsync` bodies are byte-identical to what TASK-490
left them (untouched). No `losses/by-product`, `losses/trend`, `pos/worst-products`,
`AnalyticsAuthorization.cs`, `TenantRoleCapabilities.cs`, or `frontend/` touched.

## Files

- `backend/ShelfGuard.Application/Features/Analytics/Dtos/AnalyticsDtos.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs`
- `backend/ShelfGuard.Tests/Analytics/PosAnalyticsServiceTests.cs`
