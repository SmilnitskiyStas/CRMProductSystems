using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-544 — real-Postgres concurrency test for two simultaneous
/// <see cref="MobileConfigPublishService.PublishAsync"/> calls on the SAME tenant's draft. Same
/// pattern as <c>PosConcurrencySalesIntegrationTests</c> (ProductStock, TASK-356) and
/// <c>LoyaltyConcurrencySalesIntegrationTests</c> (LoyaltyMembership, TASK-414): two independent
/// <see cref="MobileConfigPublishService"/> instances, each backed by its own
/// <see cref="AppDbContext"/>, against the local dev Postgres, with a deterministic rendezvous (not
/// timing luck) forcing both to read the exact same pre-publish <see cref="MobileConfiguration"/>/
/// draft <see cref="MobileConfigurationVersion"/> rows before either writes.
///
/// Without the xmin tokens this task adds (see <c>AppDbContext</c>'s <c>MobileConfiguration"</c>/
/// <c>MobileConfigurationVersion</c> remarks), both publishes would succeed with a last-write-wins
/// UPDATE — e.g. the loser's <c>MobileConfiguration.DraftVersionId</c> write could silently clobber
/// the winner's, leaving <c>DraftVersionId</c> pointing at an orphaned or already-published row.
/// After: the loser's <c>SaveChangesAsync</c> throws on the xmin mismatch,
/// <c>MobileConfigurationRepository</c> translates that into <c>ConcurrencyConflictException</c>,
/// and <see cref="MobileConfigPublishService.PublishAsync"/> turns it into a clean, safe-to-retry
/// <c>ConcurrentPublish</c> error instead of corrupting state. Skips (soft-pass) when no reachable
/// Postgres is configured. Override via env var <c>SHELFGUARD_TEST_DB_CONNECTION</c>.
/// </summary>
public sealed class MobileConfigPublishConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private const string ValidDraftJson = """
    {
      "schemaVersion": 1,
      "features": { "loyalty": true },
      "navigation": [
        { "type": "home", "label": "Home", "icon": "home" },
        { "type": "profile", "label": "Profile", "icon": "user" }
      ],
      "pages": {}
    }
    """;

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private Guid _configId;
    private Guid _draftId;

    public MobileConfigPublishConcurrencyIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("SHELFGUARD_TEST_DB_CONNECTION") ?? DefaultConnectionString;

        try
        {
            await using var probe = new NpgsqlConnection(_connectionString);
            await probe.OpenAsync();
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping MobileConfig publish concurrency integration test — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var tenant = Tenant.Create(
            $"MobileConfig Publish Concurrency Test {Guid.NewGuid():N}", $"mobileconfig-pub-conc-{Guid.NewGuid():N}");
        var config = MobileConfiguration.Create(tenant.Id);
        var draft = MobileConfigurationVersion.Create(
            config.Id, tenant.Id, version: 1, schemaVersion: 1, configurationJson: ValidDraftJson);

        db.Tenants.Add(tenant);
        // Two SaveChanges calls, deliberately: MobileConfiguration.DraftVersionId and
        // MobileConfigurationVersion.MobileConfigurationId form a genuine row-level insert cycle
        // when both rows are brand new — same shape MobileConfigDraftServiceRlsIntegrationTests'
        // seeding requires (TASK-534b's circular-dependency fix).
        db.MobileConfigurations.Add(config);
        db.MobileConfigurationVersions.Add(draft);
        await db.SaveChangesAsync();

        config.SetDraftVersion(draft.Id);
        await db.SaveChangesAsync();

        _tenantId = tenant.Id;
        _configId = config.Id;
        _draftId = draft.Id;
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE mobile_configurations SET \"PublishedVersionId\" = NULL, \"DraftVersionId\" = NULL WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM mobile_configuration_versions WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM mobile_themes WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM mobile_configurations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task Two_concurrent_publishes_of_the_same_draft_never_corrupt_state()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var dbA = NewContext();
        await using var dbB = NewContext();

        // Two-way rendezvous around the initial GetByTenantIdAsync read (the read PublishAsync
        // uses to load config+draft+theme): A reads, signals, waits for B; B waits for A's signal,
        // reads, signals back. Neither is allowed to proceed to validate/compose/write until BOTH
        // have read the exact same pre-write MobileConfiguration/draft state — deterministic, not
        // timing-luck.
        var aHasRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bHasRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var svcA = BuildService(dbA, repo => new RendezvousMobileConfigurationRepository(repo, aHasRead, bHasRead.Task));
        var svcB = BuildService(dbB, repo => new RendezvousMobileConfigurationRepository(repo, bHasRead, aHasRead.Task, WaitBeforeRead: true));

        // actingUserId: null — MobileConfigurationVersion.CreatedBy has a real FK to `users`; this
        // test is about publish concurrency, not user attribution, so it does not seed a user row.
        var taskA = svcA.PublishAsync(_tenantId, null);
        var taskB = svcB.PublishAsync(_tenantId, null);

        var results = await Task.WhenAll(taskA, taskB);

        _output.WriteLine(string.Join(" | ", results.Select(r =>
            r.Error is null ? $"OK version={r.Published!.Version}" : $"ERROR type={r.Error.Type} msg={r.Error.Message}")));

        var successCount = results.Count(r => r.Error is null);
        var conflictCount = results.Count(r => r.Error?.Type == MobileConfigPublishErrorType.ConcurrentPublish);

        // Exactly one publish wins; the other gets a clean, safe-to-retry conflict — never both
        // succeeding (two "published" versions) and never both failing outright.
        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        await using var verifyDb = NewContext();
        var publishedVersions = await verifyDb.MobileConfigurationVersions
            .AsNoTracking()
            .Where(v => v.TenantId == _tenantId && v.Status == MobileConfigurationVersionStatus.Published)
            .ToListAsync();
        Assert.Single(publishedVersions);
        Assert.Equal(_draftId, publishedVersions[0].Id); // the original draft became THE published version

        var finalConfig = await verifyDb.MobileConfigurations
            .AsNoTracking()
            .SingleAsync(c => c.Id == _configId);
        Assert.Equal(_draftId, finalConfig.PublishedVersionId);
        Assert.NotNull(finalConfig.DraftVersionId);
        Assert.NotEqual(_draftId, finalConfig.DraftVersionId); // repointed at a new, independently-mutable row

        var allVersions = await verifyDb.MobileConfigurationVersions
            .AsNoTracking()
            .Where(v => v.TenantId == _tenantId)
            .ToListAsync();
        Assert.Equal(2, allVersions.Count); // the published version + exactly one new draft clone — no orphaned duplicate
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private AppDbContext NewContext()
    {
        var dataSource = new NpgsqlDataSourceBuilder(_connectionString).EnableDynamicJson().Build();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(dataSource)
            // Same fix as the sibling Pos/Loyalty concurrency integration test files — this file
            // builds its own distinct NpgsqlDataSource/DbContextOptions, and enough such files
            // exist in this test assembly to trip EF Core's
            // ManyServiceProvidersCreatedWarning-as-error past its cumulative threshold when the
            // full suite runs together. See TestDbContextOptionsExtensions.
            .IgnoreManyServiceProvidersWarning()
            .Options;
        return new AppDbContext(options);
    }

    private static MobileConfigPublishService BuildService(
        AppDbContext db, Func<IMobileConfigurationRepository, IMobileConfigurationRepository> wrapRepo) =>
        new(wrapRepo(new MobileConfigurationRepository(db)), new MobileConfigValidator(), new ActivityLogRepository(db));

    /// <summary>
    /// Delegates everything to <paramref name="Inner"/> except <see cref="GetByTenantIdAsync"/>,
    /// which is wrapped in a two-way rendezvous: whichever side is NOT
    /// <paramref name="WaitBeforeRead"/> reads first, signals <paramref name="SignalSelf"/>, then
    /// blocks on <paramref name="WaitFor"/>; the <paramref name="WaitBeforeRead"/> side blocks on
    /// <paramref name="WaitFor"/> first, then reads, then signals <paramref name="SignalSelf"/> to
    /// release the other side. Net effect: BOTH publishers are guaranteed to observe the same
    /// pre-write state before EITHER is allowed to proceed to validate/compose/write.
    /// </summary>
    private sealed class RendezvousMobileConfigurationRepository(
        IMobileConfigurationRepository Inner,
        TaskCompletionSource SignalSelf,
        Task WaitFor,
        bool WaitBeforeRead = false) : IMobileConfigurationRepository
    {
        public async Task<MobileConfiguration?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default)
        {
            if (WaitBeforeRead)
                await WaitFor;

            var result = await Inner.GetByTenantIdAsync(tenantId, ct);
            SignalSelf.TrySetResult();

            if (!WaitBeforeRead)
                await WaitFor;

            return result;
        }

        public Task<MobileConfiguration?> GetPublishedByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Inner.GetPublishedByTenantIdAsync(tenantId, ct);
        public Task AddAsync(MobileConfiguration configuration, CancellationToken ct = default) => Inner.AddAsync(configuration, ct);
        public void Update(MobileConfiguration configuration) => Inner.Update(configuration);
        public Task AddVersionAsync(MobileConfigurationVersion version, CancellationToken ct = default) => Inner.AddVersionAsync(version, ct);
        public void UpdateVersion(MobileConfigurationVersion version) => Inner.UpdateVersion(version);
        public Task AddThemeAsync(MobileTheme theme, CancellationToken ct = default) => Inner.AddThemeAsync(theme, ct);
        public void UpdateTheme(MobileTheme theme) => Inner.UpdateTheme(theme);
        public Task<int> GetMaxVersionNumberAsync(Guid tenantId, CancellationToken ct = default) => Inner.GetMaxVersionNumberAsync(tenantId, ct);
        public Task<MobileConfigurationVersion?> GetVersionByIdAsync(Guid tenantId, Guid versionId, CancellationToken ct = default) =>
            Inner.GetVersionByIdAsync(tenantId, versionId, ct);
        public Task<IReadOnlyList<MobileConfigurationVersion>> GetVersionsForTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Inner.GetVersionsForTenantAsync(tenantId, ct);
        public Task SaveChangesAsync(CancellationToken ct = default) => Inner.SaveChangesAsync(ct);
    }
}
