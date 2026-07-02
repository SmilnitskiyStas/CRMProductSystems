# BUG-006 — Analytics 500: DateTimeKind.Unspecified vs timestamptz

**Date:** 2026-07-02
**Agent:** backend-developer
**Status:** done
**Origin:** found during QA of the `store_manager` role (confirmed on production)

## Symptom

500 Internal Server Error on production for:

- `GET /api/analytics/pos/summary` — always
- `GET /api/analytics/pos/revenue-trend` — always
- `GET /api/analytics/pos/top-products` — always
- `GET /api/analytics/pos/cashiers` — always
- `GET /api/analytics/write-offs?from=&to=` — only when date filters passed (200 without)
- `GET /api/movements?from=&to=` — only when date filters passed (200 without)

## Root cause

In `backend/ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs`, date-range
bounds were built with `DateOnly.ToDateTime(TimeOnly.MinValue/MaxValue)`, which produces a
`DateTime` with `Kind=Unspecified`. The compared columns (`pos_transactions.CreatedAt`,
`write_offs.CreatedAt`, `stock_movements.CreatedAt`, `pos_shifts.OpenedAt`) are PostgreSQL
`timestamp with time zone` (`timestamptz`). Npgsql rejects `Kind=Unspecified` `DateTime`
parameters for `timestamptz` → runtime `InvalidCastException` inside the query → 500.

The POS endpoints always failed because their date range is mandatory (defaults applied in
the service layer); write-offs/movements analytics only failed when `from`/`to` were passed,
since the conversions sit behind `HasValue` checks.

Existing tests did not catch this because `ShelfGuard.Tests` uses fake repositories — the
Npgsql parameter binding path is never exercised.

## Fix

`AnalyticsRepository.cs`: added two private static helpers in the helpers section and
replaced all 14 conversion sites with them:

```csharp
// Npgsql rejects DateTime with Kind=Unspecified as a parameter for
// timestamptz columns — date-range bounds must be explicitly UTC.
private static DateTime ToUtcStart(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
private static DateTime ToUtcEnd(DateOnly date)   => date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
```

Replaced sites:

- `GetWriteOffAnalyticsAsync` — from/to filters (2)
- `GetMovementAnalyticsAsync` — from/to filters (2)
- `GetLossesAsync` — from/to filters (2)
- `GetPosSummaryAsync` — fromDt/toDt (2)
- `GetPosRevenueTrendAsync` — fromDt/toDt (2)
- `GetPosTopProductsAsync` — fromDt/toDt (2)
- `GetPosCashierStatsAsync` — fromDt/toDt (2)

Swept the rest of `ShelfGuard.Infrastructure` for `ToDateTime(TimeOnly`:
`MovementRepository.cs` (lines 68/74) already uses the
`ToDateTime(TimeOnly..., DateTimeKind.Utc)` overload — no change needed. No other
occurrences found.

## Verification

- `dotnet build backend` — green, 0 warnings, 0 errors
- `dotnet test backend` — 459/459 passed, 0 failed, 0 skipped

## Files changed

- `backend/ShelfGuard.Infrastructure/Data/Repositories/AnalyticsRepository.cs`
- `.claude/tasks/current.md` (BUG-006 entry)

## Next

Deploy to production, then re-run the store_manager QA pass on the analytics endpoints
(POS summary/trend/top-products/cashiers + write-offs and movements with date filters).
