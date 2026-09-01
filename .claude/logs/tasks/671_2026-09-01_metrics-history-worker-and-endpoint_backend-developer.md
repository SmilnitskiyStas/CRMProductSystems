# TASK-671: nightly metric-snapshot write + buyer-facing metrics-history endpoint

**Agent:** backend-developer · **Date:** 2026-09-01 · **Status:** done (committed to main, not pushed)
Builds on TASK-670 (`supplier_metrics_snapshots` table + entity + DbSet, migration
`20260901193439_AddSupplierMetricsHistory`, already on main / dev DB).

## Part 1 — worker snapshot write

`worker/src/jobs/supplier-metrics-recompute.job.ts`:
- After the existing per-supplier `supplier_metrics` upsert, the loop now ALSO writes one
  append-only row per supplier per day into `supplier_metrics_snapshots` — a FULL point-in-time
  copy of the metric set, **Rating and QualityScore included**.
- `SNAPSHOT_RATING_QUALITY_SQL` — `SELECT "Rating","QualityScore" FROM supplier_metrics WHERE
  "SupplierId"=$1`, run right after `UPSERT_METRICS_SQL` (so the row is guaranteed to exist). The
  job computes neither column, so it reads them back rather than re-deriving.
- `SNAPSHOT_UPSERT_SQL` — `INSERT ... VALUES (gen_random_uuid(), ..., CURRENT_DATE, ..., NOW())
  ON CONFLICT ("SupplierId","SnapshotDate") DO UPDATE SET` the 8 metric columns (not `Id` /
  `SnapshotDate` / `TenantId` / `CreatedAt`). Idempotent — same-day re-run overwrites, never
  duplicates, `CreatedAt` stays at first-insert time.
- Recompute-column values reuse the already-computed JS values (`avgDeliveryDays.toFixed(2)`,
  `orderAccuracy.toFixed(4)`, `cancellationRate.toFixed(4)`, `responseTimeHours.toFixed(2)`,
  `deliverySampleSize`, `responseSampleSize`); `Rating`/`QualityScore` passed through from the
  SELECT (`rq?.Rating ?? null`).
- Runs inside the same `SET app.role = 'worker'` connection scope — `worker_bypass` RLS applies.
- Header comment box extended with a TASK-671 paragraph: the write-boundary rule governs only the
  live shared `supplier_metrics` row (no concurrency token, separate synchronous Rating writer);
  the snapshot table is a distinct append-only table nothing else writes, keyed UNIQUE
  (SupplierId, SnapshotDate) → no clobber risk, copying Rating/QualityScore is safe.
- Log line gains `snapshots written: N`.
- `worker/src/index.ts` — unchanged (same job, same 02:00 cron).
- `npx tsc --noEmit` clean.

### Dev-DB dry-run (as `shelfguard_app_dev`, `SET app.role='worker'`)
- Run #1: 1 row lands for `CURRENT_DATE` (`AvgDeliveryDays=2.40`).
- Run #2 with different values: still exactly 1 row (`count=1`), `AvgDeliveryDays` updated to
  `3.10`, `CreatedAt` unchanged → idempotent, no duplicate.
- `Rating`/`QualityScore` read back as NULL for the test supplier (its `supplier_metrics` row has
  them null) and passed through as NULL correctly.
- Dry-run row deleted afterwards.

## Part 2 — GET /api/marketplace/suppliers/{id}/metrics-history

- **DTO** `MarketplaceDtos.cs` — `SupplierMetricsHistoryPointDto(DateOnly Date, decimal? Rating,
  decimal? AvgDeliveryDays, decimal? OrderAccuracy, decimal? QualityScore, decimal?
  CancellationRate, decimal? ResponseTimeHours, int? DeliverySampleSize, int? ResponseSampleSize)`.
- **Controller** `MarketplaceController.GetSupplierMetricsHistory` — `[HttpGet("suppliers/{id:guid}/
  metrics-history")]`, `[Authorize]` + `[RequireModule("marketplace")]` (matches the `/coverage`
  action), `[FromQuery] int days = 90`. Returns `IReadOnlyList<SupplierMetricsHistoryPointDto>`;
  `result is null ? NotFound() : Ok(result)`.
- **Service** `MarketplaceService.GetSupplierMetricsHistoryAsync` — resolves the supplier via
  `_repo.GetSupplierByIdAsync` (cross-tenant provider-bypass, same as the coverage path), returns
  `null` on missing / `!IsPublic` (BUG-010 parity → 404). Clamps `days` to `[7, 365]`
  (`Math.Clamp`), delegates, maps rows → DTOs preserving repo order.
- **Repository** `MarketplaceRepository.GetMetricsHistoryAsync` — inside
  `_providerRlsOverride.ExecuteAsync`, `AsNoTracking`, `Where(SupplierId == id && SnapshotDate >=
  cutoff)` where `cutoff = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-days)`,
  `OrderBy(SnapshotDate)` ascending. Pure LINQ / EF — no `GetDbConnection()` / raw SQL /
  session-level SET, KI-036 (ADR-035) rule intact; `ProviderRlsOverrideContainmentTests` still
  green (bypass stays inside `MarketplaceRepository`).

### Response JSON shape
```json
[
  { "date": "2026-06-03", "rating": 4.50, "avgDeliveryDays": 2.40, "orderAccuracy": 0.9800,
    "qualityScore": null, "cancellationRate": 0.0100, "responseTimeHours": 5.50,
    "deliverySampleSize": 12, "responseSampleSize": 4 }
]
```
Oldest → newest. `days` clamp range `[7, 365]` (default 90). 404 for missing/unpublished supplier.

## Tests
- `MarketplaceServiceTests` (+13): unknown supplier → null (no history query), unpublished → null,
  `days` clamp Theory (0→7, -5→7, 3→7, 7→7, 30→30, 90→90, 365→365, 999→365, 100000→365),
  published-no-snapshots → empty list, field mapping + repo-order preservation.
- `MarketplaceRepositoryMetricsHistoryIntegrationTests` (new, live Postgres, 3 tests): seeds 3
  in-window rows out of order + 1 outside the 90d window + 1 for another supplier → asserts exactly
  the 3 in-window rows, ascending; narrow 15d window excludes the -40d row; unknown supplier →
  empty.

## Build / test
- `dotnet build ShelfGuard.sln` — 0 errors, 1 pre-existing unrelated warning
  (`MarketplaceServiceTests.cs` CS8602, line 1006 — was 895 before this task's inserts).
- `dotnet test --filter "FullyQualifiedName~Marketplace"` — 325/325 passed, 0 skipped.
- Full `dotnet test` — **2174/2174 passed, 0 skipped** (TASK-670 baseline 2158 + 16 new).

## Notes
- `backend/openapi.json` NOT regenerated — it is already stale by months (missing `/coverage`
  TASK-651, `awaiting-receipt` TASK-586, etc.; last touched commit `15aa519d`). Regenerating would
  pull unrelated API surface into this commit, violating "stage only your files". Left as-is,
  matching current repo practice.

## Commit
`feat(marketplace): nightly metric snapshots + GET suppliers/{id}/metrics-history (TASK-671)` on
`main` (not pushed).
