using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-534b — live-Postgres, real-<see cref="AppDbContext"/> regression coverage for
/// <see cref="MobileConfigDraftService.SaveDraftAsync"/>'s first-draft-creation path.
///
/// TASK-534 discovered that saving the very first draft for a tenant with no existing
/// <see cref="MobileConfiguration"/> row 500s against real Postgres: the service inserted a new
/// <see cref="MobileConfiguration"/> and a new <see cref="MobileConfigurationVersion"/> with the
/// <c>DraftVersionId</c> pointer already set on the in-memory graph, all in a single
/// <c>SaveChangesAsync()</c> call. Because <see cref="MobileConfiguration"/>.<c>DraftVersionId</c>
/// and <see cref="MobileConfigurationVersion"/>.<c>MobileConfigurationId</c> point at each other
/// (TASK-531's intentional root+versions-with-pointer shape), EF Core sees a genuine row-level FK
/// cycle between two brand-new "Added" entities and throws "circular dependency detected" — a
/// failure mode <c>MobileConfigDraftServiceTests</c> (TASK-532) never caught because it mocks
/// <c>IMobileConfigurationRepository</c> and never exercises real EF <c>SaveChanges</c>. This file
/// runs the real <see cref="MobileConfigurationRepository"/> against the real dev Postgres to
/// prove the fix (splitting the insert into two <c>SaveChangesAsync()</c> calls) actually works —
/// same split already used by <c>MobileConfigPublishedReadRlsIntegrationTests</c>' seeding helpers.
///
/// Unlike <c>MobileConfigPublishedReadRlsIntegrationTests</c> (whose endpoint is
/// <c>[AllowAnonymous]</c> and therefore resets every <c>app.*</c> session variable),
/// <see cref="MobileConfigDraftService"/> is called from an authenticated staff request, so these
/// tests set <c>app.tenant_id</c> directly rather than emulating the anonymous-session RESET
/// shape — <c>tenant_isolation</c>'s <c>USING</c> clause governs both reads and writes here since
/// none of the three mobile-config tables declare a separate <c>WITH CHECK</c> (TASK-531's
/// canonical triad, see <c>20260817090727_AddMobileConfigurationDomain</c>).
///
/// Skips (soft-pass) when no reachable Postgres is configured, same convention as every other file
/// in this folder — override via <c>SHELFGUARD_TEST_DB_CONNECTION</c>.
///
/// TASK-553: part of the <c>TENANT_ISOLATION_TESTS</c> collection — <c>rls_audit_test_role</c> is
/// created once for the whole collection by <see cref="RlsAuditRoleFixture"/>, not by this class.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class MobileConfigDraftServiceRlsIntegrationTests : IAsyncLifetime
{
    private const string ValidDocumentV1 = """
    {
      "schemaVersion": 1,
      "features": { "loyalty": true },
      "navigation": [
        { "type": "home", "label": "A", "icon": "home" },
        { "type": "profile", "label": "B", "icon": "user" }
      ],
      "pages": {}
    }
    """;

    private const string ValidDocumentV2 = """
    {
      "schemaVersion": 1,
      "features": { "loyalty": true, "news": true },
      "navigation": [
        { "type": "home", "label": "A", "icon": "home" },
        { "type": "profile", "label": "B", "icon": "user" }
      ],
      "pages": {}
    }
    """;

    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private string _connectionString = RlsAuditRoleFixture.DefaultConnectionString;
    private bool _dbAvailable;

    // One shared NpgsqlDataSource/DbContextOptions per test-method instance — same reasoning as
    // MobileConfigPublishedReadRlsIntegrationTests' field comment (avoids tripping the
    // process-wide ManyServiceProvidersCreatedWarning-as-error threshold across `dotnet test`).
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    public MobileConfigDraftServiceRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _connectionString = _fixture.ConnectionString;

        if (!_fixture.DbAvailable)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping MobileConfigDraftService RLS integration tests — no reachable Postgres at '{_connectionString}': {_fixture.UnavailableReason}");
            return;
        }

        try
        {
            _dataSource = new NpgsqlDataSourceBuilder(_connectionString).EnableDynamicJson().Build();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_dataSource)
                .ConfigureWarnings(w => w.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options;

            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping MobileConfigDraftService RLS integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task SaveDraftAsync_first_ever_save_for_a_tenant_succeeds_against_real_postgres()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync();
        // Null, not a random Guid: MobileConfigurationVersion.CreatedBy carries a real FK to
        // "users" (SetNull on delete) — a made-up id would fail on that unrelated constraint and
        // mask the actual regression under test here. actingUserId plumbing is already covered by
        // MobileConfigDraftServiceTests' mocked-repository unit tests.
        Guid? actingUserId = null;

        try
        {
            await using var session = await OpenTenantSessionAsync(tenant.Id);

            // This is the exact regression: tenant.Id has never had a MobileConfiguration row
            // before, so this call takes SaveDraftAsync's "get-or-create root + create first
            // draft version" branch. Pre-fix, this threw DbUpdateException wrapping Postgres'
            // "circular dependency detected" — see class remarks.
            var service = BuildService(session.Db);
            var (draft, errors) = await service.SaveDraftAsync(tenant.Id, actingUserId, ValidDocumentV1);

            Assert.Empty(errors);
            Assert.NotNull(draft);
            Assert.Equal(1, draft!.Version);
            Assert.Equal("draft", draft.Status);
            Assert.Null(draft.CreatedBy);
            Assert.Equal(ValidDocumentV1, draft.ConfigurationJson);

            // Prove it actually landed in Postgres, not just an in-memory DTO: read back via a
            // fresh context/session under the same tenant.
            await using var readSession = await OpenTenantSessionAsync(tenant.Id);
            var persisted = await new MobileConfigurationRepository(readSession.Db)
                .GetByTenantIdAsync(tenant.Id);

            Assert.NotNull(persisted);
            Assert.Equal(draft.VersionId, persisted!.DraftVersionId);
            Assert.NotNull(persisted.DraftVersion);
            // Semantic compare, not raw string equality: ConfigurationJson is a `jsonb` column
            // (AppDbContext), so Postgres re-serializes it on round-trip (whitespace stripped,
            // key order not preserved) — the read-back text is not byte-identical to what was
            // written even though it is the same document.
            Assert.True(
                JsonNode.DeepEquals(
                    JsonNode.Parse(ValidDocumentV1), JsonNode.Parse(persisted.DraftVersion!.ConfigurationJson)),
                $"Expected the persisted draft JSON to match what was saved. Got: {persisted.DraftVersion!.ConfigurationJson}");
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
        }
    }

    [Fact]
    public async Task SaveDraftAsync_second_save_mutates_the_same_draft_version_in_place()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync();
        // See the sibling test's comment on why this is null rather than a made-up Guid.
        Guid? actingUserId = null;

        try
        {
            await using var createSession = await OpenTenantSessionAsync(tenant.Id);
            var (created, createErrors) =
                await BuildService(createSession.Db).SaveDraftAsync(tenant.Id, actingUserId, ValidDocumentV1);
            Assert.Empty(createErrors);
            Assert.NotNull(created);

            // A second save for the same tenant now takes the "config.DraftVersion is not null"
            // branch (TASK-532's already-working update path) — must still round-trip cleanly
            // under real Postgres after the fix, still as a single version row (no new Version
            // number minted).
            await using var updateSession = await OpenTenantSessionAsync(tenant.Id);
            var (updated, updateErrors) =
                await BuildService(updateSession.Db).SaveDraftAsync(tenant.Id, actingUserId, ValidDocumentV2);

            Assert.Empty(updateErrors);
            Assert.NotNull(updated);
            Assert.Equal(created!.VersionId, updated!.VersionId);
            Assert.Equal(1, updated.Version);
            Assert.Equal(ValidDocumentV2, updated.ConfigurationJson);
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static MobileConfigDraftService BuildService(AppDbContext db) =>
        new(new MobileConfigurationRepository(db), new MobileConfigValidator(), new ActivityLogRepository(db));

    private async Task<Tenant> SeedTenantAsync()
    {
        await using var db = NewContext();
        var tenant = Tenant.Create(
            $"MobileConfig Draft RLS Test {Guid.NewGuid():N}", $"mobileconfig-draft-rls-test-{Guid.NewGuid():N}");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<TenantSession> OpenTenantSessionAsync(Guid tenantId)
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        // Guid-typed tenantId, not a raw external string — SET doesn't accept bind parameters,
        // so EF1002 is a false positive here (same reasoning as LoyaltyJoinRlsIntegrationTests /
        // TenantSessionOverride.ExecuteAsync).
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"SET app.tenant_id = '{tenantId:D}';");
#pragma warning restore EF1002
        return new TenantSession(db);
    }

    private async Task CleanupTenantConfigAsync(Guid tenantId)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM mobile_themes WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE mobile_configurations SET \"PublishedVersionId\" = NULL, \"DraftVersionId\" = NULL WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM mobile_configuration_versions WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM mobile_configurations WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" = {tenantId}");
    }

    /// <summary>
    /// Builds a fresh AppDbContext from the ONE shared _options/_dataSource for this test method
    /// (see the field comments above) — cheap, and does not register as yet another distinct EF
    /// internal service provider the way a brand-new NpgsqlDataSourceBuilder(...) per call would.
    /// </summary>
    private AppDbContext NewContext() => new(_options!);

    /// <summary>
    /// Wraps a manually-opened, role-switched, tenant-scoped AppDbContext connection so callers
    /// can `await using` it and get RESET ROLE on disposal — load-bearing now that every context
    /// in a test method shares one NpgsqlDataSource/pool (see
    /// LoyaltyJoinRlsIntegrationTests' ConsumerSession comment for the full reasoning).
    /// </summary>
    private sealed class TenantSession : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public TenantSession(AppDbContext db) => Db = db;

        public async ValueTask DisposeAsync()
        {
            try { await Db.Database.ExecuteSqlRawAsync("RESET ROLE; RESET app.tenant_id;"); }
            catch { /* best-effort cleanup only */ }
            await Db.DisposeAsync();
        }
    }
}
