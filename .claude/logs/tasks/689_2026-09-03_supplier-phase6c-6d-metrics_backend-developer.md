# TASK-689 — Supplier portal expansion Phase 6c + 6d (metrics history + composite score)

Plan `1-partitioned-book.md` Phase 6 → 6c/6d + "п.10 формула". Agent: backend-developer.
HEAD before/after: `6a217b27`. Status: review — NOT committed.

## 6d — composite quality score + on-time delivery rate

**Migration `20260903131322_AddSupplierCompositeScore`** (EF-generated)
- `supplier_metrics` + `supplier_metrics_snapshots` each get `CompositeScore numeric(4,3) NULL`,
  `OnTimeDeliveryRate numeric(5,4) NULL`. No RLS change (columns inherit triad; no new table).
- **Applied to dev DB `:5435/crm`** via `dotnet ef migrations script AddSupplierItemPlatformCategory
  AddSupplierCompositeScore --idempotent` → `docker exec -i crmproductsystems-postgres-1 psql`.
  Verified: all 4 columns present on both tables with correct precision/scale. NOT prod.
- `AppDbContextModelSnapshot.cs` regenerated (diff = only the 4 new `b.Property` lines).

**Entities** — `SupplierMetrics` / `SupplierMetricsSnapshot` += `CompositeScore`, `OnTimeDeliveryRate`
(`decimal?`, XML-doc'd). EF config in `AppDbContext.cs` for both blocks (`numeric(4,3)` / `(5,4)`).

**Worker `worker/src/jobs/supplier-metrics-recompute.job.ts`** (write-boundary change)
- ASCII write-boundary box updated: set now lists `OnTimeDeliveryRate` + `CompositeScore`; disjoint /
  separate-statement / no-xmin invariant restated; note that `CompositeScore` only *reads* `Rating`.
- `computeOnTimeDeliveryRate(rows)` — pure fn: on-time / (on-time+late) over the SAME 365-day
  delivered sample as `AvgDeliveryDays`, counting only rows with a non-null `ExpectedDeliveryDate`
  (`DeliveredAt::date <= ExpectedDeliveryDate`); null-ExpectedDeliveryDate excluded from num & denom;
  denom 0 → null. `DELIVERY_SAMPLES_SQL` extended with a nullable `on_time` column.
- `computeCompositeScore({rating,orderAccuracy,onTimeDeliveryRate,responseTimeHours})` — pure fn:
  equal-weight mean of available (non-null) `{ Rating/5, OrderAccuracy, OnTimeDeliveryRate,
  clamp(1−ResponseTimeHours/48,0,1) }`, round 3dp; all-null → null.
- **Rating-read ordering: option (a)** — a cheap `RATING_PRE_READ_SQL` (`SELECT "Rating"`) BEFORE the
  upsert; `CompositeScore` computed in JS and written in the ONE `UPSERT_METRICS_SQL` statement
  alongside `OnTimeDeliveryRate` (no second UPDATE, no whole-row upsert). No metrics row yet ⇒
  rating null (identical to post-upsert since the upsert never touches `Rating`).
- Snapshot INSERT (`SNAPSHOT_UPSERT_SQL`) + upsert list extended with both columns, fed from the
  same computed values.
- `npx tsc --noEmit` clean. (worker/ has no test runner — pure fns exported for a future harness,
  per the file's existing convention.)

**DTO plumbing** (`MarketplaceDtos.cs`)
- `SupplierMetricsDto` += `CompositeScore`, `OnTimeDeliveryRate` (trailing optional).
- `SupplierListItemDto` += `CompositeScore` (trailing optional) — mapped in `MarketplaceService.ToListItemDto`.
- `SupplierMetricsHistoryPointDto` += `CompositeScore`, `OnTimeDeliveryRate` (trailing optional).
- `MarketplaceService.ToMetricsDto` maps both; new shared `MarketplaceService.ToHistoryPointDto`
  (used by buyer history + cabinet history).

**ADR-036 amendment** — `.claude/docs/decisions.md`: dated 2026-09-03 under ADR-036; index line
amended. Write-boundary set grows by the 2 columns, same disjointness/safety argument; `QualityScore`
stays dead; composite formula documented.

## 6c — supplier self-stats history + period deltas

- `ISupplierCabinetService.GetMetricsHistoryAsync(supplierTenantId, days, ct)` →
  `SupplierMetricsHistoryResponseDto?` (null ⇒ 404 when tenant has no owner-managed supplier).
- `SupplierCabinetService` impl: `ResolveAsync` (same as `GetMetricsAsync`) → `_repo.GetMetricsHistoryAsync`
  (reused; rows oldest-first, windowed). `points` via `MarketplaceService.ToHistoryPointDto`.
  `deltas` = `PeriodMetricDto.Of(latest, oldest-in-window)` per metric (compositeScore, avgDeliveryDays,
  orderAccuracy, onTimeDeliveryRate, rating, responseTimeHours); each null when either endpoint null;
  empty points ⇒ all-null deltas. `days` clamped 7..365.
- `SupplierCabinetController`: `GET /api/supplier-cabinet/metrics-history?days=` — class gate
  `SupplierCabinet` + `marketplace_supplier`; per-action `client_reviews` permission (matches `GET /metrics`).
- New DTOs: `SupplierMetricsHistoryResponseDto { points, deltas }`, `SupplierMetricsHistoryDeltasDto`.

## Verification

- `dotnet build -c Release` — clean (1 pre-existing nullable warning in MarketplaceServiceTests, unrelated).
- `dotnet test -c Release --filter "SupplierMetrics|SupplierCabinet|Marketplace|RlsCrossTenant"` —
  **417 passed, 0 failed** (integration vs dev Postgres). RLS-audit test green.
- Worker `npx tsc --noEmit` — clean.
- New tests: `SupplierCabinetServiceTests` (6 — oldest-first, delta latest-vs-oldest, missing-endpoint
  null delta, empty, clamp Theory, no-profile→null); `MarketplaceServiceTests` (compositeScore on
  listing card; composite/onTime mapped through history).

## Not done / follow-ups
- openapi.json regen — shared deferred debt (TASK-670..674, +689).
- Frontend (6c page + 6d compositeScore rendering) — separate agent.
- Prod DB migration — separate deploy.
- No `mobile/` / `frontend/` / `SupplierItem` / `SupplierAnalytics/` changes.
