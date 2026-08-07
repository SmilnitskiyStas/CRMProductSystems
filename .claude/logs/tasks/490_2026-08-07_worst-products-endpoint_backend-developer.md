# TASK-490: Worst-performing products / dead-stock endpoint

**Agent:** backend-developer
**Date:** 2026-08-07
**Status:** done

## Context

Part of the small follow-up batch (TASK-488..495) after the interactive-analytics initiative
(TASK-479..487, commit 99bbde97). TASK-489 (losses/trend) touched the same shared
controller/service/repository files immediately before this task — read all of them fresh, built
on top of TASK-489's additions, nothing of theirs modified.

## Done

**`GET /api/analytics/pos/worst-products`** on `AnalyticsController.cs`, placed right after
`pos/top-products` (its closest sibling). Same class-level `AnalyticsViewOrCapability` policy,
same `ResolveTenantId()`/`IsProvider()`/`ResolveDateRange()` conventions as every other action.
Params: `store_id` (optional `Guid?`), `from`/`to` (optional `DateOnly?`, via `ResolveDateRange`),
`limit` (`int`, default 10, clamp `if (limit is < 1 or > 100) limit = 10;` — byte-for-byte copy of
`pos/top-products`' clamp).

**Design (the actual point of this task):** this is not `pos/top-products` sorted ascending. That
query groups `PosTransactionItems`, so a product with zero sales in the period never appears in
the result at all — no rows to group. "Dead stock" needs exactly those zero-sale products
surfaced, since a product with real (if low) sales isn't the same signal as one sitting fully
unsold. So `GetWorstProductsAsync` starts from the opposite side of the join: active `Item`s
(`IsActive == true`, tenant-scoped) that currently have on-hand `ProductStock` (`Quantity > 0`,
summed per product at the scoped store(s)), then merges in a sales rollup for the period,
COALESCEing missing sales to 0m/0/0.

New DTOs in `PosAnalyticsDtos.cs` (POS-specific, same file as `PosTopProductsDto`):
`WorstProductsDto(Products)` / `WorstProductRowDto(ProductId, ProductName, SalesRevenue,
UnitsSold, TransactionCount, CurrentStock)` — exact shapes from the brief. Thin
`IAnalyticsService`/`AnalyticsService.cs` pass-through.

New repo method `GetWorstProductsAsync` in `AnalyticsRepository.cs`, two-query shape rather than
one LINQ query with LEFT JOIN + GroupBy (per the brief's explicit fallback guidance, given this
file's already-documented EF/Npgsql GroupBy-translation limits — see `GetProductSalesTrendAsync`/
`GetLossesTrendAsync`'s comments):

1. **Stock aggregate** (SQL-side, scalar-only `GroupBy(ProductId).Select(Sum(Quantity))` — the
   same translatable shape those two methods already established): `ProductStocks` filtered to
   `Quantity > 0 && Status != "sold_out" && Status != "archived"` (reused `GetByCategoryAsync`'s
   own on-hand-quantity convention verbatim, per the brief's instruction to read that method — a
   sold-out/archived batch isn't meaningfully "on the shelf" even if its `Quantity` column hasn't
   been zeroed), restricted to active items via a `Contains`-subquery against tenant-scoped
   `Items.IsActive` (`IsActive` lives on `Item`, not `ProductStock`; reused the
   "Subquery-Contains join" shape `GetLossesByProductAsync`/`GetPosTopProductsAsync` already use).
2. **Sales aggregate**, pre-filtered to just that stock aggregate's product ids before
   materializing — unlike `GetPosTopProductsAsync` (which pulls every product sold in the whole
   period into memory before grouping), the `productIds.Contains(...)` filter here bounds the
   materialized row count by the stock candidate pool (catalog size), not by sales volume, so the
   in-memory `GroupBy` (needed for `TransactionCount`'s distinct-transaction dedup, same as
   `GetPosTopProductsAsync`) stays cheap — not a repeat of that method's accepted-but-larger
   anti-pattern.
3. Merge via `Dictionary<Guid, …>` lookup in C#, `OrderBy(SalesRevenue)` ascending (zero-revenue
   products sort first), `Take(limit)`.

No margin gate/`includeMargin` anywhere in this call path — same sensitivity class as
`pos/top-products` (already ungated for store_manager+); the DTO carries no `PricePurchase`-derived
field at all.

## Tests

Added 4 facts to `PosAnalyticsServiceTests.cs`: zero-sales product round-trips with
`SalesRevenue: 0`/`TransactionCount: 0` while `CurrentStock` stays populated; ordering ascending by
revenue is preserved through the pass-through; `limit` is forwarded unchanged (see note below);
store-filter forwarding + `CurrentStock` round-trip.

**Note on "limit clamping" coverage:** the actual 1-100 clamp is controller-only logic (mirrors
`pos/top-products`, which itself has no clamp test). This codebase has zero `*ControllerTests.cs`
files anywhere (established precedent, called out explicitly in TASK-482's own comments re: that
endpoint's 404 ternary) — controllers aren't unit-tested directly in this repo. Since
`GetWorstProductsAsync` at the service layer takes whatever `limit` the controller already
resolved and forwards it untouched, the added test instead pins that pass-through (`limit: 5`
in, `limit: 5` out to the mocked repo) rather than fabricating a controller test file that would
break with every other endpoint's convention in this file. Applied per CLAUDE.md's "judgment calls
with an objective best-practice answer... implement per project convention, note the decision" —
this is a consistency call, not a product/UX one, so no user sign-off gate applied.

## Build/test

- `dotnet build` — 0 errors, 0 new warnings (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test` — 1341/1341 green (1337 baseline + 4 new), no regressions.

## Not in scope (per brief)

No changes to `AnalyticsDtos.cs`, the `losses/trend` endpoint (TASK-489), margin/authorization
files, or anything under `frontend/`.

## Files

- `backend/ShelfGuard.Api/Controllers/AnalyticsController.cs`
- `backend/ShelfGuard.Application/Features/Analytics/Dtos/PosAnalyticsDtos.cs`
- `backend/ShelfGuard.Application/Features/Analytics/IAnalyticsRepository.cs`
- `backend/ShelfGuard.Application/Features/Analytics/IAnalyticsService.cs`
- `backend/ShelfGuard.Application/Features/Analytics/AnalyticsService.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs`
- `backend/ShelfGuard.Tests/Analytics/PosAnalyticsServiceTests.cs`

## Route contract (for TASK-491 backend, TASK-493 frontend)

`GET /api/analytics/pos/worst-products?store_id&from&to&limit=10` (all query params optional,
`limit` clamped server-side to 1-100, default 10). 200 body (camelCase, System.Text.Json default):
```json
{
  "products": [
    {
      "productId": "3f2a1c9e-....",
      "productName": "Stale Bread",
      "salesRevenue": 0.00,
      "unitsSold": 0.00,
      "transactionCount": 0,
      "currentStock": 20.00
    }
  ]
}
```
`products` is sorted ascending by `salesRevenue` (true zero-revenue products first) and capped at
`limit`. A product only appears if it is an active item with on-hand stock (`currentStock > 0`) in
the caller's tenant/store scope — a fully sold-out or inactive product never appears, regardless of
its historical sales. `salesRevenue`/`unitsSold`/`transactionCount` are 0 (not null, not omitted)
when the product had no sales in the `from`..`to` window. No margin fields anywhere in this DTO.
