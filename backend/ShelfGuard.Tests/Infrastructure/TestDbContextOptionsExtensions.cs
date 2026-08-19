using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// CI-fix (2026-08-19, see .claude/logs/tasks/ci-fix_2026-08-19_ef-many-service-providers_backend-developer.md):
/// single shared place to downgrade EF Core's
/// <see cref="CoreEventId.ManyServiceProvidersCreatedWarning"/> from throw to log for
/// Postgres-backed integration tests.
///
/// Root cause this addresses: every Postgres-integration test class in this assembly builds its
/// own <c>DbContextOptions&lt;AppDbContext&gt;</c> (most cache it once per class/test-method via a
/// static or instance-level field, so each class contributes ONE distinct configuration — not one
/// per test method). With 10+ such classes now running together against real Postgres for the
/// first time in CI (TASK-553 added the `postgres:16-alpine` CI service), their combined distinct
/// configurations cross EF's cumulative ~20-internal-service-provider threshold for the process,
/// which EF treats as an error by default (<c>ManyServiceProvidersCreatedWarning</c> exists to
/// catch a production DI bug — a fresh singleton injected into <c>DbContextOptionsBuilder</c> on
/// every request — which genuinely leaks memory). None of that applies here: every context in this
/// assembly is a short-lived, deliberately-scoped test fixture against the same throwaway dev
/// Postgres, and the "leaked" providers are reclaimed the moment the test process exits. Silencing
/// the diagnostic for these contexts trades a false-positive test failure for the (harmless, in
/// this context) extra provider instances.
///
/// This was previously copy-pasted as an identical <c>.ConfigureWarnings(w =>
/// w.Log(CoreEventId.ManyServiceProvidersCreatedWarning))</c> line into most-but-not-all
/// Postgres-integration test files — the ONE file that was missed
/// (<see cref="MarketingAnalyticsRepositoryIntegrationTests"/>) is exactly what broke CI, because
/// its own never-before-seen <c>DbContextOptions</c> was the configuration that happened to push
/// the process over the threshold with nothing there to downgrade it. Centralizing here means a
/// future integration-test file only needs to call <see cref="IgnoreManyServiceProvidersWarning"/>
/// once, instead of re-typing (and risking dropping) the exact lambda + <c>CoreEventId</c> import.
/// </summary>
internal static class TestDbContextOptionsExtensions
{
    public static DbContextOptionsBuilder<AppDbContext> IgnoreManyServiceProvidersWarning(
        this DbContextOptionsBuilder<AppDbContext> builder) =>
        builder.ConfigureWarnings(w => w.Log(CoreEventId.ManyServiceProvidersCreatedWarning));
}
