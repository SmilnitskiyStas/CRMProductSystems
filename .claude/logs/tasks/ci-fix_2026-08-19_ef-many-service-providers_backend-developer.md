# CI fix: EF Core ManyServiceProvidersCreatedWarning-as-error in backend-ci

**Date:** 2026-08-19
**Agent:** backend-developer
**Trigger:** commit `15aa519d` (TASK-553) broke GitHub Actions CI after adding a real
`postgres:16-alpine` service to `backend-ci`, surfacing a failure that could never happen before
(every Postgres-backed integration test previously soft-skipped in CI).
**CI run:** https://github.com/SmilnitskiyStas/CRMProductSystems/actions/runs/32227563660
(1685 tests, 1674 passed, 11 failed — all in `MarketingAnalyticsRepositoryIntegrationTests`)

## Root cause

All 11 failures threw the identical
`Microsoft.EntityFrameworkCore.Infrastructure.ManyServiceProvidersCreatedWarning` exception from
`MarketingAnalyticsRepositoryIntegrationTests.NewContext()`.

The same TASK-553 session had already worked around this exact EF diagnostic in **11 other**
Postgres-backed integration test files (`AudienceBuilderRepositoryIntegrationTests`,
`LoyaltyRepositoryIntegrationTests`, `LoyaltyJoinRlsIntegrationTests`,
`MobileConfigDraftServiceRlsIntegrationTests`, `MobileConfigPublishConcurrencyIntegrationTests`,
`MobileConfigPublishedReadRlsIntegrationTests`, `MobileThemeServiceRlsIntegrationTests`,
`PostCampaignRepositoryIntegrationTests`, `PriceSegmentsRepositoryIntegrationTests`,
`Pos/LoyaltyConcurrencySalesIntegrationTests`, `Pos/PosConcurrencySalesIntegrationTests`) via an
identical copy-pasted line:

```csharp
.ConfigureWarnings(w => w.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
```

`MarketingAnalyticsRepositoryIntegrationTests.cs` was the **one file that never got this line** —
it only had the (separate, also-correct) fix of caching its `DbContextOptions<AppDbContext>` in a
`static` field so it builds only one `NpgsqlDataSource`/config for its whole class. That static
config is still a *new*, never-before-seen configuration from EF's point of view. Once the test
assembly's combined distinct Postgres `DbContextOptions` configurations (one per class, from all
12 Postgres-integration files) crossed EF's cumulative ~20-internal-service-provider threshold for
the process, whichever configuration was newly constructed at that point would trigger the
diagnostic — and this was the only one of the 12 without the downgrade-to-log override, so it's
the one that threw. Every one of its own test methods that called `NewContext()` failed with the
same exception, matching the CI failure list (12 `[Fact]`/`[Theory]` methods in the file, 11 CI
failures).

Confirmed by grep: `ConfigureWarnings(w => w.Log(CoreEventId.ManyServiceProvidersCreatedWarning))`
appeared in exactly 11 files pre-fix; `MarketingAnalyticsRepositoryIntegrationTests.cs` was the
only Postgres-backed (`UseNpgsql`) integration test class missing it. The other 4 files using
`DbContextOptionsBuilder<AppDbContext>` in the assembly (`TenantRepositoryGetBySlugTests`,
`TenantRepositoryPlatformTenantTests`, `StockRepositoryFefoTests`,
`MarketplaceRepositoryPlatformTenantTests`) all use `UseInMemoryDatabase`, not Postgres — out of
scope for this diagnostic in this failure.

## Fix applied — (b) Suppress, centralized

Per EF Core's own documented mitigation for `ManyServiceProvidersCreatedWarning`
(`ConfigureWarnings(w => w.Ignore/Log(CoreEventId.ManyServiceProvidersCreatedWarning))`):
downgrading it to a log line is correct here because every internal-service-provider instance this
diagnostic is warning about belongs to a short-lived, deliberately-scoped test `AppDbContext`
against the same throwaway dev Postgres — not a production DI bug (a fresh singleton injected into
`DbContextOptionsBuilder` on every request, which is what the diagnostic exists to catch and which
really does leak memory over a long-running process). All such providers are reclaimed when the
test process exits.

Rejected full consolidation (option a — one truly shared `DbContextOptions<AppDbContext>`/
`NpgsqlDataSource` across the whole assembly, mirroring `RlsAuditRoleFixture`'s collection-fixture
pattern): several of the RLS-focused files deliberately keep their own data source scoped to one
test *class* (not the whole assembly) because they intentionally switch the underlying connection
to a throwaway `NOSUPERUSER NOBYPASSRLS` role per test — a prior session's own comment in
`LoyaltyJoinRlsIntegrationTests.cs` already documents choosing per-class sharing over full
consolidation for this reason. Redoing that as a full assembly-wide merge would be a much larger,
riskier refactor of role-switching semantics across ~10 files for a targeted CI fix, and the
existing per-class caching already keeps each class's own contribution to the global count at
exactly one.

Instead of adding a 12th copy of the identical line (repeating the exact mistake that caused this
break), created one central helper:

**New file:** `backend/ShelfGuard.Tests/Infrastructure/TestDbContextOptionsExtensions.cs`
```csharp
internal static class TestDbContextOptionsExtensions
{
    public static DbContextOptionsBuilder<AppDbContext> IgnoreManyServiceProvidersWarning(
        this DbContextOptionsBuilder<AppDbContext> builder) =>
        builder.ConfigureWarnings(w => w.Log(CoreEventId.ManyServiceProvidersCreatedWarning));
}
```

Applied `.IgnoreManyServiceProvidersWarning()` in place of the inline lambda in all 12
Postgres-backed integration test files (the 11 that already had it, now routed through the shared
helper, plus `MarketingAnalyticsRepositoryIntegrationTests` which was missing it). Removed the
now-unused `using Microsoft.EntityFrameworkCore.Diagnostics;` from each of those 12 files (verified
via grep it wasn't used for anything else in any of them); added
`using ShelfGuard.Tests.Infrastructure;` to the two `ShelfGuard.Tests.Pos` files that needed the
extension from a different namespace.

This is deliberately **not** applied via `AppDbContext.OnConfiguring` (which has no override
today) — that would also silence the diagnostic for production `AddDbContext` configuration in
`Program.cs`, removing a real safeguard against a genuine leak. The suppression only reaches test
contexts that explicitly opt in via the extension method.

Why this satisfies "won't cross the threshold again as more tests are added": every future
Postgres-integration test file that calls `.IgnoreManyServiceProvidersWarning()` (one line, one
import) is permanently immune to this diagnostic regardless of how many more such files
accumulate — the fix isn't sized to today's count, it removes the throw path entirely for any
context that opts in.

## Files changed

- `backend/ShelfGuard.Tests/Infrastructure/TestDbContextOptionsExtensions.cs` (new)
- `backend/ShelfGuard.Tests/Infrastructure/MarketingAnalyticsRepositoryIntegrationTests.cs` (the actual fix)
- `backend/ShelfGuard.Tests/Infrastructure/AudienceBuilderRepositoryIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Infrastructure/LoyaltyJoinRlsIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Infrastructure/LoyaltyRepositoryIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Infrastructure/MobileConfigDraftServiceRlsIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Infrastructure/MobileConfigPublishConcurrencyIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Infrastructure/MobileConfigPublishedReadRlsIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Infrastructure/MobileThemeServiceRlsIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Infrastructure/PostCampaignRepositoryIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Infrastructure/PriceSegmentsRepositoryIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Pos/LoyaltyConcurrencySalesIntegrationTests.cs`
- `backend/ShelfGuard.Tests/Pos/PosConcurrencySalesIntegrationTests.cs`

## Verification

- `dotnet build ShelfGuard.sln`: succeeded, 0 errors, 0 warnings.
- `dotnet test ShelfGuard.Tests/ShelfGuard.Tests.csproj` against real local dev Postgres
  (`crmproductsystems-postgres-1`, port 5435, same `crm`/`crm_dev_password` default the tests use):
  run 3 times back to back — **1685/1685 passed, 0 failed, every run** (previously 1674/1685 with
  11 failures, all in the one file this fix targets).
- Local machine has 20 logical CPUs, so xUnit's default `MaxParallelThreads = ProcessorCount`
  already ran with substantially higher collection-parallelism than a typical GitHub Actions
  runner (2-4 cores) — this is a reasonable proxy for "more concurrent pressure than CI," even
  though it isn't a guaranteed reproduction of CI's exact scheduling.

### Honest limits
- Did not manage to *reproduce* the original throwing failure locally even before applying the
  fix (consistent with what the task brief already flagged — this session didn't re-attempt
  reproducing the broken state first, since the root cause was identified with high confidence by
  direct code inspection: grep proof that exactly one Postgres-integration file lacked the
  suppression line every sibling file already had). No `xunit.runner.json` exists in this project
  to force parallelism beyond `ProcessorCount`; did not add one since the default was already high
  and the fix is a structural guarantee (suppression), not a probabilistic one — it doesn't matter
  whether this exact process happens to cross the ~20-provider threshold, because every Postgres
  integration DbContext in the assembly now has the diagnostic downgraded to a log line instead of
  a throw.
- Cannot fully guarantee CI's exact xUnit test-ordering/scheduling will never surface some
  different, as-yet-unseen EF diagnostic-as-error; this fix specifically and permanently closes
  the `ManyServiceProvidersCreatedWarning` path for every context that opts into the new helper.

## Status

Ready for review/commit by orchestrating session. Not committed or pushed (per task instructions).
