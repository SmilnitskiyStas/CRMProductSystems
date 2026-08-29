# TASK-639 — KI-035: fix the Npgsql connection leak behind `53300: too many clients already`

**Agent:** backend-developer
**Status:** done

## Root cause

Not parallelism (correctly ruled out earlier — serializing collections made it *worse*: ~39-41
failures, 2× slower). A genuine, cumulative `NpgsqlDataSource` leak in the test fixtures.

Every `NpgsqlDataSource` owns its **own** connection pool, and that pool's physical Postgres
backends stay open until the data source is disposed (or until the 300 s default
`ConnectionIdleLifetime` elapses — longer than a whole suite run). Fifteen integration-test classes
built `new NpgsqlDataSourceBuilder(cs).EnableDynamicJson().Build()` and never disposed it:

- **10 classes** cached it in an **instance** field (`private DbContextOptions<AppDbContext>?
  _options;` + `_options ??= …`). xUnit constructs a fresh class instance **per `[Fact]`**, so this
  was one undisposed pool per TEST, not per class — the in-file comments claiming a per-class cache
  were wrong. `AudienceBuilderRepositoryIntegrationTests`,
  `ItemRepositoryGetByAnyBarcodeIntegrationTests`,
  `ItemRepositoryGetPagedBarcodeSearchIntegrationTests`, `ItemRepositoryGetPagedSortIntegrationTests`,
  `PostCampaignRepositoryIntegrationTests`, `PriceSegmentsRepositoryIntegrationTests`,
  `ReceiptRepositoryGetPagedSearchSortIntegrationTests`,
  `StockRepositoryGetPagedSearchSortIntegrationTests`,
  `TransferRepositoryGetPagedSearchSortIntegrationTests`,
  `WriteOffRepositoryGetPagedSearchSortIntegrationTests`.
- **1 class** cached it `static` — one pool, still never disposed
  (`MarketingAnalyticsRepositoryIntegrationTests`).
- **4 classes** rebuilt a brand-new data source inside `NewContext()` on **every call**:
  `LoyaltyRepositoryIntegrationTests`, `MobileConfigPublishConcurrencyIntegrationTests`,
  `Pos/PosConcurrencySalesIntegrationTests`, `Pos/LoyaltyConcurrencySalesIntegrationTests`.

Summed over a full run that is ~100 stranded backends against a server whose `max_connections` is
100 — hence failures scattered across whichever unrelated test happened to run once the budget ran
out, and hence total immunity to how many tests run concurrently.

**Not** the cause, verified individually: the 10 RLS classes tagged
`[Collection("TENANT_ISOLATION_TESTS")]` all store `_dataSource` in a field and dispose it in
`DisposeAsync()`; their `InitializeAsync` cannot throw between building and assigning it (the
data-source build is the first statement inside the `try`, so xUnit's "DisposeAsync is skipped when
InitializeAsync throws" hazard doesn't apply). They were victims — several of the observed failures
were in those classes. `new NpgsqlConnection(cs)` probe/RLS connections are all `await using`-scoped
and go to Npgsql's shared legacy pool keyed by connection string, so they are bounded by concurrency,
not cumulative.

## Fix

**New:** `backend/ShelfGuard.Tests/Infrastructure/TestPostgres.cs` — the structural fix. ONE
process-wide pooled `NpgsqlDataSource` per distinct connection string, plus the single
`DbContextOptions<AppDbContext>` built on it and a `NewContext(connectionString)` helper.
`Lazy<T>` with `LazyThreadSafetyMode.ExecutionAndPublication` (not a bare `GetOrAdd` factory, which
can build and then silently discard — i.e. leak — a second pool under a race), `MaxPoolSize = 40`
as a ceiling well under Postgres' 100, `EnableDynamicJson()`, disposed on `ProcessExit`.

**Changed (15 files):** each leaking class's context factory is now
`private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);` and its
`_options` field is deleted. Stale comments about per-call/per-class data sources and the EF
`ManyServiceProvidersCreatedWarning` threshold were replaced with a KI-035 note.

Side effect worth recording: sharing one `DbContextOptions` means the assembly now creates exactly
one EF internal service provider for these tests, so the cumulative
`ManyServiceProvidersCreatedWarning` pressure that
`TestDbContextOptionsExtensions.IgnoreManyServiceProvidersWarning()` had been papering over since
the 2026-08-19 CI fix is gone structurally. The helper is kept (and still called from
`TestPostgres`) as a guard for the RLS classes that legitimately still build their own data sources.

RLS classes left untouched on purpose — their private per-test pool is deliberate `SET ROLE` /
session-GUC isolation, and they already dispose it.

## Verification

Environment: ephemeral `postgres:16-alpine` (`ki035_repro`, host port 5437, `crm`/`crm_dev_password`
/`crm`), migrations applied with the real `dotnet ef database update` via
`ConnectionStrings__DefaultConnection` (`dotnet ef` does not read `appsettings.Development.json`);
tests pointed at it with `SHELFGUARD_TEST_DB_CONNECTION`.

- `dotnet build` — clean; 1 warning, the pre-existing CS8602 in
  `ShelfGuard.Tests/Marketplace/MarketplaceServiceTests.cs:534`. No new warnings.
- **Baseline (before the fix), fresh container:** 1999 tests, **1983 passed, 16 failed**, 32 `53300`
  occurrences — PriceSegments ×1, AudienceBuilder ×5, MobileConfigDraftService ×2,
  MobileConfigPublishedRead ×4, MobileTheme ×2, SupplierAgreementMarkSigned ×2. Reproduced the CI
  signature exactly.
- **After the fix, 3 consecutive runs**, each against a **newly created and freshly migrated**
  container (container dropped + recreated + re-migrated between runs, matching CI exactly):
  run 1 / run 2 / run 3 all **1999 total, 1999 passed, 0 failed, 0 `53300`**, exit code 0.
- Nothing regressed or silently skipped: total count unchanged (1999 → 1999), `grep "DB not
  available"` → 0 hits, and the previously-failing integration tests report real execution times
  (e.g. `PosConcurrencySalesIntegrationTests` 2 s, `MarketingAnalyticsRepositoryIntegrationTests`
  ~300-500 ms per test).
- Peak concurrent backends sampled from `pg_stat_activity` every 2 s during a full run: **24**
  (previously pinned at the 100 ceiling).
- `ki035_repro` container removed.

Nothing left unaccounted for: `grep NpgsqlDataSourceBuilder` over `ShelfGuard.Tests` now returns
only `TestPostgres.cs` and the 7 RLS classes that dispose theirs.

## Notes

- `xunit.runner.json` / `parallelizeTestCollections: false` was **not** reintroduced, and must not
  be — it is documented in KI-035 as making this worse.
- No git commit/push from this task, per instructions.
- `.claude/docs/known-issues.md` KI-035 updated to ✅ resolved with root cause, fix and the numbers
  above.
