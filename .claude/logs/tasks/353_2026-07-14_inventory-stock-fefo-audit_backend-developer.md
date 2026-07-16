# TASK-353 — Backend: Block 3 pre-launch audit — Inventory/Stock/Locations/Stores/Catalog

**Status:** done (2026-07-14) · **Agent:** backend-developer (main session) · **Depends:** TASK-350..352

Block 3 of the pre-launch audit (`eager-pondering-tower.md`). Scope: `Features/Inventory`,
`Features/Stock`, `Features/Locations`, `Features/Stores`, `Features/Catalog`.

## FEFO review

`StockService.FefoConsumeAsync` + `StockRepository.GetFefoOrderedAsync` — correct: orders
by `ExpiryDate` ascending, `Quantity > 0` filter, consumes across batches sequentially,
marks `sold_out` on full depletion. Existing `StockServiceTests` already covered single/
multi-batch/insufficient-stock/sold-out-marking. Added:
- `StockServiceTests.FefoConsumeAsync_TiedExpiryDates_ConsumesAcrossBothBatches` — same
  expiry_date on two batches, confirms both get consumed correctly (no quantity lost).
- New `StockRepositoryFefoTests.cs` (EF InMemory, 4 tests) — pins the actual repository
  query (StockServiceTests only mocks `IStockRepository`, never exercises the real LINQ):
  zero-quantity batch excluded, archived-with-leftover-quantity excluded, ordering by
  ExpiryDate ascending, wrong store/product excluded.
- Hardening fix: `GetFefoOrderedAsync` only filtered `Quantity > 0`, relying on the
  invariant that `archived` status always implies `quantity = 0` (true today —
  `cleanup.job.ts` only archives already-`sold_out` rows). Added an explicit
  `Status != "sold_out" && Status != "archived"` filter for defense-in-depth so FEFO
  consumption doesn't silently depend on that invariant holding forever.

Stock statuses (`StockStatus.Compute`): safe/warning/critical/expired/sold_out/
needs_verification thresholds verified correct (quantity<=0 checked first, then expiry,
then perishability-class thresholds, then staleness). `archived` is a terminal
worker-only state (cron `cleanup.job.ts`), not part of `Compute`'s output space — correct
by design, already excluded from analytics via `AnalyticsRepository`.

Transfer immutability (`TransferService.CreateAsync`/`ConfirmAsync`) — already correct and
already tested (`TransferServiceTests.CreateAsync_ExpiryAndBatchCopiedFromSource`,
`ConfirmAsync_Valid_CreatesDestinationStockAndMovements`): `expiry_date`/`batch_number`
copied as-is at both the transfer-item and destination-batch stage, never recomputed. No
change needed.

## KI-008 (pagination) — already resolved, doc was stale

`ProductsLegacyController` (`api/products`) is a pure `RedirectPermanent` shim to
`api/items` for every verb — the old unauthenticated POC `Products` table endpoint no
longer exists on the live routing surface. The real catalog (`ItemsController`,
`api/items`) is `[Authorize(Policy = CanViewStock)]`, RLS-scoped, and paginated
(`GET /api/items?page=&pageSize=` → `PagedResult<ItemDto>`, default 1/50). This was fixed
by commit `206b2534` (2026-06-18, "perf(db): database optimization") — predates
`known-issues.md`'s last update, doc just wasn't updated. Marked KI-008 resolved; fixed
the stale "POC Products, no auth" section in `api-contracts.md` to describe the current
redirect-shim reality.

## DB review

- `idx_stock_expiry_active` (original FEFO index, `Quantity>0 AND Status NOT IN
  ('sold_out','archived')`) and `idx_stock_fefo_active` (`Quantity>0` only, added by the
  same perf commit, matching `GetFefoOrderedAsync`'s actual predicate) both exist and are
  applied on dev DB — verified via `pg_indexes`. Table only has 25 rows in dev so
  `EXPLAIN ANALYZE` won't show a real index-scan vs seq-scan difference at this scale;
  index presence + predicate match confirmed by inspection instead.
- **Found and fixed a real N+1 / dead-code bug**: `IStockRepository.GetDeficitStocksBulkAsync`
  existed (added by the 206b2534 perf commit, doc claimed "N+1 eliminated") but was never
  actually called — `StockService.GetSuggestionsAsync` → `BuildActionsAsync` still ran one
  `GetDeficitStocksAsync` query per action-required batch (N+1 reads on every `/stock/suggestions`
  call). Root cause the bulk method was never wired in. Fix: `GetDeficitStocksBulkAsync`
  changed from `Dictionary<Guid, ProductStock?>` (first-match-only, no store exclusion) to
  `Dictionary<Guid, List<ProductStock>>` (all deficits per product, ExpiryDate-ordered);
  `GetSuggestionsAsync` now loads it once for all distinct product IDs, `BuildActions`
  (now synchronous, no repo calls) filters out the batch's own store in-memory to preserve
  the original "exclude own store" semantics that the naive bulk shape would have lost.
  Updated two hand-written `IStockRepository` test fakes (`PosServiceTests.cs`,
  `FiscalizationRetryTests.cs`) for the new signature. New tests:
  `GetSuggestionsAsync_UsesBulkDeficitLookup_NotPerBatchQuery` (regression guard on the
  N+1),  `GetSuggestionsAsync_DeficitInOwnStore_IsExcludedFromTransferSuggestion`.
- No other N+1 found in this scope — `GetAllAsync`/`GetPagedAsync` use `.Include()` for
  Product/Store/Zone; `GetStatusCountsAsync`/`GetStockByZoneRawAsync` project two columns
  and aggregate in-memory in a single round-trip.

## Incidental findings (out of scope, flagged as follow-up tasks)

- `api-contracts.md`'s "Pending Endpoints (v1 backlog)" table is entirely stale (every row
  already implemented) — spawned as a documentation-writer follow-up.
- `StoreService`/`IStoreService`/`Store`/`StoreZone` (Features/Stores) is dead code
  superseded by `LocationService`/`Location` (v4 rename, TASK-201) — `StoresController.cs`
  is an intentionally-empty placeholder, nothing wires `IStoreService` into DI. Spawned as
  a backend/database-engineer follow-up (needs FK/table verification before dropping).

## Build/tests

`dotnet build` — 0 errors, 0 warnings. `dotnet test` — 815/815 green (was 808/808 at
start of this task; +7 new: 3 in `StockServiceTests`, 4 in new `StockRepositoryFefoTests`).

**Not deployed anywhere** — dev-only code changes (`StockService.cs`, `StockRepository.cs`,
`IStockRepository.cs`, 2 test fakes, 2 new/extended test files) + docs (`known-issues.md`,
`api-contracts.md`). No migration needed (no schema change).
