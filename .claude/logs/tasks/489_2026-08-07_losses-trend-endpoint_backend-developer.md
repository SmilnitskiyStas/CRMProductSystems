# TASK-489: Losses/write-offs trend-over-time endpoint

**Agent:** backend-developer
**Date:** 2026-08-07
**Status:** done

## Context

First of a small follow-up batch (TASK-488..495) requested after the user reviewed the shipped
interactive-analytics initiative (TASK-479..487, commit 99bbde97) live. Losses/write-offs
trend-over-time chart, mirroring the existing POS revenue trend
(`GET /api/analytics/pos/revenue-trend`). No dependency on TASK-479/480's margin-authorization
work — this endpoint carries no margin data at all.

## Done

**`GET /api/analytics/losses/trend`** on `AnalyticsController.cs`, same class-level
`AnalyticsViewOrCapability` policy, same `ResolveTenantId()`/`IsProvider()`/`ResolveDateRange()`
conventions as every other action. Params match `pos/revenue-trend` exactly: `store_id` (optional
`Guid?`), `from`/`to` (optional `DateOnly?`, resolved via `ResolveDateRange` — defaults to last 30
days), `group_by` (`"day"` default, `"day"|"week"`). No compare-mode variant (not requested for
this endpoint).

New DTOs in `AnalyticsDtos.cs` (where the other losses DTOs live, not `PosAnalyticsDtos.cs`):
`LossesTrendDto(Points, GroupBy)` / `LossesTrendPointDto(Date, TotalLoss, Count)` — exact shapes
from the brief. Thin `IAnalyticsService`/`AnalyticsService.cs` pass-through.

New repo method `GetLossesTrendAsync` in `AnalyticsRepository.cs`: same `WriteOffs` filter shape as
`GetLossesAsync`/`GetLossesByProductAsync` (`Status == "approved"`, tenant/store/date range), but
groups **in SQL before `ToListAsync`** — deliberately not `GetLossesAsync`'s/
`GetWriteOffAnalyticsAsync`'s in-memory-materialize-then-`GroupBy` pattern (both accepted for
those lower-cardinality aggregates, not to be copied here per the brief). Mirrors
`GetProductSalesTrendAsync`'s (TASK-482) two-step shape instead: aggregate into an anonymous type
in SQL (`Date`/`TotalLoss`/`Count`), terminate with `ToListAsync`, then map the already-collapsed
(≤366-row) list into `LossesTrendPointDto` in a cheap second pass. Reused TASK-482's already-
verified day/week bucketing exactly (not reimplemented from scratch): "day" via the provider's
built-in `DateTime.Date` translation, "week" via the same inlined Monday-anchored ISO-offset
arithmetic (`dow == 0 ? -6 : 1 - dow`) as `IsoWeekStart()` uses — `EF.Functions.DateTrunc` still
doesn't exist in this repo's installed Npgsql EF Core provider (TASK-482's finding), and EF still
can't translate a call to an arbitrary private C# method, so the arithmetic has to be inlined
again at this call site rather than calling `IsoWeekStart()` directly. `TotalLossAmount` is
`decimal?` on the `WriteOff` entity, summed as `g.Sum(w => w.TotalLossAmount ?? 0m)` — same
null-coalesce-in-SQL shape `GetLossesAsync`/`GetWriteOffAnalyticsAsync` already use.

No margin gate, no `includeMargin` parameter anywhere in this call path — matches
`losses/by-product`'s (TASK-481) precedent: `LossAmount`/`TotalLoss` are already shown in
aggregate to every store_manager+ caller today (ADR-027 §1), this is the same data re-sliced by
day/week instead of by store/reason/product.

## Tests

Added 4 facts to `PosAnalyticsServiceTests.cs`: day `groupBy` pass-through + result-shape check,
week `groupBy` pass-through, store-filter forwarding, and empty-range handling (repository returns
an empty `Points` list, not null/throw — same shape as the existing
`GetPosTopProductsAsync_empty_period_returns_empty_list` test).

## Build/test

- `dotnet build` — 0 errors, 0 new warnings (1 pre-existing unrelated warning in
  `MarketplaceServiceTests.cs`).
- `dotnet test` — 1337/1337 green (1333 baseline + 4 new), no regressions.

## Not in scope (per brief)

No changes to `AnalyticsAuthorization.cs`/`TenantRoleCapabilities.cs`, `PosAnalyticsDtos.cs`,
`pos/top-products`, or anything under `frontend/`.

## Files

- `backend/ShelfGuard.Api/Controllers/AnalyticsController.cs`
- `backend/ShelfGuard.Application/Features/Analytics/Dtos/AnalyticsDtos.cs`
- `backend/ShelfGuard.Application/Features/Analytics/IAnalyticsRepository.cs`
- `backend/ShelfGuard.Application/Features/Analytics/IAnalyticsService.cs`
- `backend/ShelfGuard.Application/Features/Analytics/AnalyticsService.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs`
- `backend/ShelfGuard.Tests/Analytics/PosAnalyticsServiceTests.cs`

## Route contract (for TASK-492 frontend)

`GET /api/analytics/losses/trend?store_id&from&to&group_by=day|week` (all query params optional).
200 body (camelCase, System.Text.Json default):
```json
{
  "points": [
    { "date": "2026-07-26", "totalLoss": 513.80, "count": 4 }
  ],
  "groupBy": "day"
}
```
`points` is sorted ascending by `date`, not zero-filled for gap days (same convention as
`pos/revenue-trend`'s `PosRevenueTrendDto`). No margin fields anywhere in this DTO — none were
ever in scope for losses data.
