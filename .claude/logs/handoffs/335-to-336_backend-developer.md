# Handoff: TASK-335 database → TASK-336 backend-developer (stock status snapshot comparison + worker cron)

**From:** database-engineer · **Date:** 2026-07-12
**DB state:** merged-ready on `main` working tree; migration `20260712112112_AddStockStatusSnapshots` auto-applies on API start. `dotnet build` 0 errors (full solution). No tests added — schema-only task.

## What exists now (schema layer only — no services/endpoints/worker yet)

- Table `stock_status_snapshots`: `Id, TenantId, LocationId ("StoreId" in C#), SnapshotDate (date), SafeCount, WarningCount, CriticalCount, ExpiredCount, CreatedAt`.
- One row per `(TenantId, StoreId, SnapshotDate)` — enforced by a unique index (`idx_stock_status_snapshots_tenant_store_date`), so upserts are idempotent.
- Second index `idx_stock_status_snapshots_tenant_date` on `(TenantId, SnapshotDate)` for network-wide (all-store) aggregation.
- RLS enabled + forced, standard `tenant_isolation` / `provider_bypass` policies (same pattern as every other tenant table).
- Entity: `ShelfGuard.Domain/Entities/StockStatusSnapshot.cs` — note the C# property is `StoreId` (`Guid`, nav `Location? Store`) but the DB column is `"LocationId"` — this matches the existing convention already used by `ProductStock.StoreId` and `StockMovement.FromStoreId/ToStoreId`. Don't rename it to `LocationId` in C# — would break the established pattern.
- Repository: `IStockStatusSnapshotRepository` (`ShelfGuard.Domain/Interfaces/`) + impl in `ShelfGuard.Infrastructure/Data/Repositories/StockStatusSnapshotRepository.cs`:
  - `UpsertAsync(tenantId, storeId, snapshotDate, safeCount, warningCount, criticalCount, expiredCount, ct)` — find-then-update/insert + `SaveChangesAsync`, returns the saved entity. **Not** raw SQL `ON CONFLICT` — there's no existing precedent for that pattern in this codebase (checked `DailySalesRepository`, `IntegrationRepository`); if the worker calls this from a single-threaded cron run per tenant/store, the find-then-save is safe. If you expect concurrent upserts for the same key, either serialize the worker's writes or swap this method's body for a raw `ON CONFLICT (TenantId, LocationId, SnapshotDate) DO UPDATE` — the unique index is already there to support that.
  - `GetAsync(tenantId, storeId, snapshotDate, ct)` — single store's snapshot for a date, or null.
  - `GetByTenantAndDateAsync(tenantId, snapshotDate, ct)` — all stores for a date; sum the four counters client-side for the network-wide dashboard comparison.
- DI already registered in `ShelfGuard.Infrastructure/DependencyInjection.cs`.

## What's left for you (out of database-engineer scope)

1. **Worker cron job** (`/worker/src/jobs/`) — daily snapshot writer. Needs to read `product_stock.Status` grouped by `(TenantId, StoreId)` for "today" and call `UpsertAsync` per store per tenant. Look at `expiry-check.job.ts` for the existing cron pattern (hourly status recompute) — this new job should probably run once daily, after that recompute, so the snapshot reflects the day's settled status.
2. **Application service + DTOs** (`ShelfGuard.Application/Features/Analytics/` or wherever the dashboard Safe/Warning/Critical/Expired cards are currently served from — check that controller first) — a comparison endpoint: current live counts (from `product_stock`, existing logic) vs. `GetAsync`/`GetByTenantAndDateAsync` for `today - 7 days` (or whatever date the frontend requests). Compute deltas server-side; keep the response shape simple (`{ current: {...}, previous: {...}, deltas: {...} }` or similar — your call, no UX decision pending here per CLAUDE.md's judgment-call carve-out).
3. **Endpoint** — thin controller per project convention (`[RequireModule(...)]` if this is gated by module activation — check how the existing dashboard/stock-status endpoint is guarded and match it).
4. No frontend work implied by this handoff — that's a separate downstream task once the API contract is fixed.

## Deviations / things to double check

- I used `DeleteBehavior.Cascade` on the `StoreId → locations` FK (deleting a store deletes its historical snapshots) — matches `ProductStock.Store` cascade behavior. If product wants snapshots retained after store deletion, that's a schema change, flag it back to database-engineer rather than working around it in the app layer.
- `SnapshotDate` is `DateOnly` mapped to Postgres `date` — no timezone. Decide "today" in the worker using the same convention the rest of the codebase uses for day boundaries (check `expiry-check.job.ts` / `weekly-report.job.ts` for precedent) before writing the cron job.
