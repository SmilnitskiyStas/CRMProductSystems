using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// KI-035 fix: the ONE process-wide <see cref="NpgsqlDataSource"/> (and the one
/// <c>DbContextOptions&lt;AppDbContext&gt;</c> built on it) that every non-RLS Postgres-backed
/// integration test in this assembly must use to open contexts.
///
/// Root cause this replaces: <see cref="NpgsqlDataSource"/> owns its OWN connection pool, and that
/// pool's physical Postgres connections are only closed when the data source itself is disposed
/// (or after the 300 s default <c>ConnectionIdleLifetime</c> — far longer than a full test run).
/// Fifteen integration-test classes built a brand-new
/// <c>new NpgsqlDataSourceBuilder(cs).EnableDynamicJson().Build()</c> and never disposed it:
/// thirteen cached it in an INSTANCE field (xUnit constructs a fresh class instance per <c>[Fact]</c>,
/// so that is one undisposed pool per TEST, not per class) and four rebuilt one inside
/// <c>NewContext()</c> on every single call. A full-suite run therefore accumulated ~100 live,
/// never-closed backends against a server whose <c>max_connections</c> is 100 — which is why the
/// failures were scattered across unrelated features (whichever test happened to run once the
/// budget ran out) and why serializing test collections did NOT help: the leak is cumulative over
/// the run, completely independent of how many tests execute concurrently. See KI-035 in
/// .claude/docs/known-issues.md.
///
/// Fix shape: one shared, pooled data source per distinct connection string for the whole process.
/// A pool is designed to be shared — it grows only to the actual concurrent demand and recycles
/// physical connections instead of stranding one per test. Sharing the resulting
/// <c>DbContextOptions</c> too means the assembly now creates exactly ONE EF internal service
/// provider for these tests, which also removes the cumulative
/// <c>ManyServiceProvidersCreatedWarning</c> pressure that
/// <see cref="TestDbContextOptionsExtensions.IgnoreManyServiceProvidersWarning"/> was papering over
/// (the call is kept as a belt-and-braces guard for the RLS classes that still build their own).
///
/// NOT for the RLS/tenant-isolation classes tagged <c>[Collection("TENANT_ISOLATION_TESTS")]</c>:
/// those deliberately keep a private per-test data source so <c>SET ROLE</c> / session-GUC state
/// cannot leak between tests through a shared pool, and they already dispose it in
/// <c>DisposeAsync</c> — they were never part of the leak.
/// </summary>
internal static class TestPostgres
{
    /// <summary>Mirrors docker-compose.yml's dev Postgres and the CI service container
    /// (5435:5432, crm/crm_dev_password/crm) — same default every test class hardcoded
    /// individually before this helper existed.</summary>
    public const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    /// <summary>Hard ceiling on the shared pool. Well under Postgres' default
    /// <c>max_connections = 100</c>, so even a future burst of parallel integration tests degrades
    /// into a short wait for a pooled connection instead of a <c>53300</c> from the server.</summary>
    private const int MaxPoolSize = 40;

    private static readonly ConcurrentDictionary<string, Lazy<NpgsqlDataSource>> DataSources = new();

    private static readonly ConcurrentDictionary<string, Lazy<DbContextOptions<AppDbContext>>> OptionsCache = new();

    static TestPostgres()
    {
        // Belt-and-braces: the pools are meant to live for the whole test process, but close them
        // deterministically on exit rather than relying on the OS to reap the sockets.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var entry in DataSources.Values)
            {
                if (!entry.IsValueCreated) continue;
                try { entry.Value.Dispose(); } catch { /* best-effort teardown */ }
            }
        };
    }

    /// <summary>The <c>SHELFGUARD_TEST_DB_CONNECTION</c> override every Postgres-backed test class
    /// already honoured, resolved in one place.</summary>
    public static string ResolveConnectionString() =>
        Environment.GetEnvironmentVariable("SHELFGUARD_TEST_DB_CONNECTION") ?? DefaultConnectionString;

    /// <summary>The shared pooled data source for <paramref name="connectionString"/>.
    /// <see cref="Lazy{T}"/> (not a bare <c>GetOrAdd</c> factory) so a race can never build — and
    /// then silently discard, i.e. leak — a second pool.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        DataSources.GetOrAdd(
            connectionString,
            static cs => new Lazy<NpgsqlDataSource>(
                () => new NpgsqlDataSourceBuilder(
                        new NpgsqlConnectionStringBuilder(cs) { MaxPoolSize = MaxPoolSize }.ConnectionString)
                    .EnableDynamicJson() // List<string>/JSONB mapping — required, see DependencyInjection.cs
                    .Build(),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>The shared <c>DbContextOptions</c> bound to <see cref="DataSource"/>.</summary>
    public static DbContextOptions<AppDbContext> Options(string connectionString) =>
        OptionsCache.GetOrAdd(
            connectionString,
            static cs => new Lazy<DbContextOptions<AppDbContext>>(
                () => new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(DataSource(cs))
                    .IgnoreManyServiceProvidersWarning()
                    .Options,
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>A fresh <see cref="AppDbContext"/> over the shared pool. The context still must be
    /// disposed by the caller (<c>await using</c>) — that is what returns its connection to the
    /// pool for the next test to reuse.</summary>
    public static AppDbContext NewContext(string connectionString) => new(Options(connectionString));
}
