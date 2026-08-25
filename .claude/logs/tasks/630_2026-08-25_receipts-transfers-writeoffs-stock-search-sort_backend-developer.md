# TASK-630 — Server-side search + sort for Receipts/Transfers/WriteOffs/Stock (backend)

**Status:** done · **Agent:** backend-developer

## What changed

Added `search` (string?) and `sortBy`/`sortDescending` (string?/bool?) query params to the 4
paginated list endpoints, threaded through Controller → Service → Repository, without changing
`PagedResult<T>` or any existing param (`store_id`, `status`, `zone_id`, `product_id`, `page`,
`pageSize`).

New allowlist classes (same shape as `PostCampaignSortKeys`): `ReceiptSortKeys`,
`TransferSortKeys`, `WriteOffSortKeys`, `StockSortKeys` — each a `Default` const + `HashSet<string>`
+ `Normalize(string?)`. Repositories switch on the normalized key to build `OrderBy`/
`OrderByDescending` — the raw string is never used to build an expression.

Contract actually implemented (for cross-check against the frontend agent's work):

| Feature | search matches | sortBy keys (default first) |
|---|---|---|
| Receipts | supplier name, destination store name | `createdat`, `status`, `supplier`, `destination`, `expectedat` |
| Transfers | from-store name, to-store name, transfer type | `createdat`, `status`, `from`, `to` |
| WriteOffs | store name, reason | `createdat`, `status`, `reason`, `netloss` |
| Stock | product name, product barcode (exact, via `EF.Functions.JsonContains`, same as TASK-601), batch number | `expirydate`, `productname`, `quantity`, `status` |

All search matches use `EF.Functions.ILike` (case-insensitive substring), OR'd across fields,
applied before sort/paging.

**`sortDescending` default (deviation worth flagging):** for Receipts/Transfers/WriteOffs,
`sortDescending ?? true` — matches the brief's "newest-first" default. For **Stock**, the brief's
blanket "null → true" would have flipped the default view from nearest-expiry-first (the
pre-existing implicit order, and the FEFO-relevant one) to farthest-expiry-first for every
existing caller that never touches the new params — a real behavior regression, and in tension
with CLAUDE.md's "FEFO is sacred". Implemented instead: `sortDescending ?? (normalizedSortBy !=
StockSortKeys.Default)` — i.e. ascending only when using the default `expirydate` key with no
explicit direction (preserves current behavior exactly); any other explicit `sortBy` still
defaults to descending like the other 3 features. Documented in `StockSortKeys`' and
`StockRepository.ApplySort`'s doc comments. Frontend should treat Stock's default list view as
unchanged (ascending by expiry) unless it explicitly passes `sortDescending`.

Files touched: `Domain/Interfaces/I{Receipt,Transfer,WriteOff,Stock}Repository.cs`,
`Infrastructure/Data/Repositories/{Receipt,Transfer,WriteOff,Stock}Repository.cs`,
`Application/Features/{Receipts,Transfers,WriteOffs,Stock}/{I,}{...}Service.cs`,
`Application/Features/{Receipts,Transfers,WriteOffs,Stock}/{...}SortKeys.cs` (new),
`Api/Controllers/{Receipts,Transfers,WriteOffs,Stock}Controller.cs`. Also fixed 3 hand-written
`IStockRepository` test fakes (`PosServiceTests.cs`, `PosConcurrencySalesIntegrationTests.cs`,
`FiscalizationRetryTests.cs`) whose `GetPagedAsync` signature needed the 3 new params to keep
implementing the interface.

## New tests

4 new live-Postgres integration test files under `ShelfGuard.Tests/Infrastructure/`
(`{Receipt,Transfer,WriteOff,Stock}RepositoryGetPagedSearchSortIntegrationTests.cs`, 27 tests
total), following TASK-601's `ItemRepositoryGetPagedBarcodeSearchIntegrationTests.cs` template.
Cover: search narrows per-field (case-insensitive substring), search-miss returns empty,
unrecognized `sortBy` falls back to default without throwing, sort actually changes row order for
a non-default key, Stock's default-key omitted-direction case stays ascending, and existing
`store_id`/`status` filters are unaffected. All scoped via a `search: _run` (per-test unique GUID
embedded in every fixture row's searchable text) since the shared dev DB already has real data —
an earlier unscoped-total-count version of these tests was flaky against it (also caught two
`varchar(50)` overflows in `TransferType`/`Reason` test fixture data during this pass, fixed by
shortening the generated needle strings).

## Verification

- `dotnet build` — clean (0 errors).
- New 27 integration tests — run against real local Postgres (`docker compose` `postgres` on
  port 5435), all pass.
- `dotnet test` (full suite) — 1978/1980 passing. The 2 failures
  (`MobileThemeServiceRlsIntegrationTests`, unrelated to this task) are pre-existing Postgres
  connection-pool exhaustion (`53300: sorry, too many clients already`) from running the full
  parallel suite of many real-DB integration tests — both pass cleanly when run in isolation,
  confirmed not a regression from this change.

## Issues found

- Killed a stale `ShelfGuard.Api` dev-server process (PID 37468, started earlier the same day)
  that was locking `bin/Debug` DLLs and blocking `dotnet build` — unrelated leftover from a prior
  session, not caused by this task.
