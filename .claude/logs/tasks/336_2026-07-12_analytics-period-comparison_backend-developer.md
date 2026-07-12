# TASK-336: Analytics period comparison (backend) — DONE

**Agent:** backend-developer · **Date:** 2026-07-12

## Scope
Backend for dashboard/analytics period comparison, per handoff `335-to-336_backend-developer.md` (stock_status_snapshots schema from database-engineer).

## What was built

1. **Weekly KPI** — `GET /api/analytics/dashboard/weekly-kpi`. New `AnalyticsService.GetWeeklyKpiAsync`, reuses existing `IAnalyticsRepository.GetPosSummaryAsync` + `GetWriteOffAnalyticsAsync` for two 7-day windows (today-6..today vs today-13..today-7). No new repo methods needed.
   - Decision: "Sales" = POS `TransactionCount`, "Revenue" = POS `TotalRevenue`, "WriteOffLoss" = `WriteOffAnalyticsDto.TotalLoss`. Chose POS over the manual `DailySales` feature because POS transaction data is populated for every tenant using the module, while `DailySales` is optional forecasting input.

2. **Expiry summary comparison** — `GET /api/analytics/expiry-summary/compare`. New `AnalyticsService.GetExpirySummaryComparisonAsync`, live current counts (reuses `GetExpirySummaryAsync`) vs. a snapshot row `compareWeeksAgo` weeks back via `IStockStatusSnapshotRepository`. `Previous = null` when no snapshot exists yet, or when called cross-tenant (`tenantId == null`, provider view — snapshots are per-tenant).
   - `NeedsVerification` is always `0` on the `Previous` side (not tracked by the 4-bucket snapshot table).
   - Added `.Include(s => s.Store)` to `StockStatusSnapshotRepository.GetAsync`/`GetByTenantAndDateAsync` so the store name can be populated on the comparison's `Stores` list.

3. **Generic compare on 4 existing endpoints** (`write-offs`, `losses`, `pos/summary`, `pos/revenue-trend`): added optional `compare` (bool, default `false`), `compareFrom`, `compareTo` query params.
   - Decision on backward compatibility: response stays the **old unwrapped shape** unless `compare=true` is explicitly passed — chosen over "wrap whenever compareFrom/compareTo present" because it's unambiguous and needs no inference. Existing frontend callers are unaffected.
   - `write-offs`/`losses` previously accepted nullable `from`/`to` (null = unbounded, no default). That's preserved for `compare=false`. When `compare=true`, both endpoints now resolve `from`/`to` via the existing `ResolveDateRange` (default: last 30 days) before computing the comparison range — an unbounded "all time" range has no sensible "previous period."
   - `ResolveCompareRange(from, to, compareFrom?, compareTo?)` added next to `ResolveDateRange` in `AnalyticsController`: if both `compareFrom`/`compareTo` given, use them as-is; otherwise auto = `[from - len - 1, from - 1]` where `len = to - from` (same-length window immediately preceding `from`).
   - New service methods: `GetWriteOffAnalyticsComparisonAsync`, `GetLossesComparisonAsync`, `GetPosSummaryComparisonAsync`, `GetPosRevenueTrendComparisonAsync` — each fetches current + comparison via the existing repo methods and computes percent-change server-side.
   - `PosRevenueTrendComparisonDto` carries `From/To/CompareFrom/CompareTo` alongside the two point lists (both are sparse — no zero-fill for days without transactions, matching existing `PosRevenueTrendDto` behavior) so the frontend can align series by day-offset from each range's own start rather than assuming dense/equal-length arrays.

4. **Worker cron** — `worker/src/jobs/stock-snapshot.job.ts`, registered in `worker/src/index.ts` as queue `stock-snapshot`, cron `10 0 * * *` (00:10 daily, after the hourly `expiry-check` cycle). Groups `product_stock` by `(TenantId, StoreId, Status)` where `Quantity > 0`, upserts into `stock_status_snapshots` via raw `INSERT ... ON CONFLICT ("TenantId","LocationId","SnapshotDate") DO UPDATE` — resolves the race-condition caveat the database-engineer flagged for the C# repo's find-then-save `UpsertAsync` (the worker bypasses that method entirely and uses an atomic upsert). Same `SET app.role = 'worker'` convention as `expiry-check.job.ts`.

## Build / test status
- `dotnet build ShelfGuard.sln` — 0 errors (had to update `PosAnalyticsServiceTests.cs` constructor call for the new `IStockStatusSnapshotRepository` dependency on `AnalyticsService`).
- `dotnet test --filter "FullyQualifiedName~Analytics"` — 15/15 passed, no regressions.
- `worker`: `npx tsc --noEmit` and `npm run build` both clean, no errors.
- No new unit tests added for the new service methods (out of time budget for this task; straightforward delegation + arithmetic, low risk — flag for qa-tester / a follow-up if coverage is wanted).

## Files changed
- `backend/ShelfGuard.Application/Features/Analytics/Dtos/AnalyticsDtos.cs` — `PeriodMetricDto`, `WeeklyKpiDto`, `ExpirySummaryComparisonDto`, `WriteOffsComparisonDto`, `LossesComparisonDto`
- `backend/ShelfGuard.Application/Features/Analytics/Dtos/PosAnalyticsDtos.cs` — `PosSummaryComparisonDto`, `PosRevenueTrendComparisonDto`
- `backend/ShelfGuard.Application/Features/Analytics/IAnalyticsService.cs` / `AnalyticsService.cs` — new methods, `IStockStatusSnapshotRepository` injected
- `backend/ShelfGuard.Infrastructure/Data/Repositories/StockStatusSnapshotRepository.cs` — `.Include(s => s.Store)`
- `backend/ShelfGuard.Api/Controllers/AnalyticsController.cs` — 2 new endpoints, `compare`/`compareFrom`/`compareTo` on 4 existing, `ResolveCompareRange`
- `backend/ShelfGuard.Tests/Analytics/PosAnalyticsServiceTests.cs` — constructor fix
- `worker/src/jobs/stock-snapshot.job.ts` (new), `worker/src/index.ts` — cron registration

## Next
Handoff to frontend-developer at `.claude/logs/handoffs/336-to-337_frontend-developer.md` with exact endpoint signatures.
