using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-534 — live-Postgres, real-repository regression coverage for
/// <c>GET /api/v1/mobile/config</c>'s read path (<see cref="MobileConfigPublishedReadService"/>).
/// <c>mobile_configurations</c>/<c>mobile_configuration_versions</c>/<c>mobile_themes</c> all carry
/// only the canonical tenant_isolation/provider_bypass/worker_bypass RLS triad (TASK-531) — no
/// identity-based policy — and this endpoint is <c>[AllowAnonymous]</c> (MASTER SPEC §12's
/// "discover before joining" flow), so a real unauthenticated request RESETs every <c>app.*</c>
/// session variable (<c>TenantConnectionInterceptor.GetSetSql</c>'s
/// <c>user.Identity.IsAuthenticated != true</c> branch), meaning tenant_isolation alone would
/// return zero rows for every tenant. This file proves the service's <c>ITenantSessionOverride</c>
/// wiring actually resolves the correct tenant's data under that exact real RLS shape — not just
/// that a mocked override was invoked with the right argument, which
/// <c>MobileConfigPublishedReadServiceTests</c> already pins at the unit level. Same split as
/// <c>LoyaltyServiceTests</c>/<c>LoyaltyJoinRlsIntegrationTests</c>.
///
/// Skips (soft-pass) when no reachable Postgres is configured, same convention as every other file
/// in this folder — override via <c>SHELFGUARD_TEST_DB_CONNECTION</c>.
///
/// TASK-553: part of the <c>TENANT_ISOLATION_TESTS</c> collection — <c>rls_audit_test_role</c> is
/// created once for the whole collection by <see cref="RlsAuditRoleFixture"/>, not by this class.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class MobileConfigPublishedReadRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private string _connectionString = RlsAuditRoleFixture.DefaultConnectionString;
    private bool _dbAvailable;

    // One shared NpgsqlDataSource/DbContextOptions per test-method instance — see
    // LoyaltyJoinRlsIntegrationTests' field comment for why (avoids tripping the process-wide
    // ManyServiceProvidersCreatedWarning-as-error threshold across the full `dotnet test` run).
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    public MobileConfigPublishedReadRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
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
                $"Skipping MobileConfig published-read RLS integration tests — no reachable Postgres at '{_connectionString}': {_fixture.UnavailableReason}");
            return;
        }

        try
        {
            _dataSource = new NpgsqlDataSourceBuilder(_connectionString).EnableDynamicJson().Build();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_dataSource)
                .IgnoreManyServiceProvidersWarning()
                .Options;

            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping MobileConfig published-read RLS integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task GetPublishedConfigAsync_returns_the_published_document_under_a_real_anonymous_session()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync();
        var (_, version) = await SeedPublishedConfigAsync(tenant.Id, true);

        try
        {
            await using var session = await OpenAnonymousSessionAsync();
            var (documentJson, etag, error) = await BuildService(session.Db).GetPublishedConfigAsync(tenant.Id);

            Assert.Null(error);
            Assert.NotNull(documentJson);
            Assert.NotNull(etag);
            Assert.Contains(tenant.Id.ToString(), documentJson);
            Assert.Contains($"\"configVersion\":{version.Version}", documentJson);

            // The SET LOCAL override must not leak past its own transaction — check on this SAME
            // still-open connection (a fresh one would trivially never have seen the override).
            await using var checkCmd = session.Db.Database.GetDbConnection().CreateCommand();
            checkCmd.CommandText = "SELECT current_setting('app.tenant_id', true)";
            var leaked = (string?)await checkCmd.ExecuteScalarAsync();
            Assert.True(
                string.IsNullOrEmpty(leaked),
                $"Expected app.tenant_id unset on this connection after the read returned, got '{leaked}'.");
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
        }
    }

    [Fact]
    public async Task GetPublishedConfigAsync_reports_no_published_configuration_when_only_a_draft_exists()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync();
        await SeedDraftOnlyConfigAsync(tenant.Id, "draft-only-marker-must-never-leak");

        try
        {
            await using var session = await OpenAnonymousSessionAsync();
            var (documentJson, etag, error) = await BuildService(session.Db).GetPublishedConfigAsync(tenant.Id);

            Assert.Null(documentJson);
            Assert.Null(etag);
            Assert.NotNull(error);
            Assert.Equal(MobileConfigReadErrorType.NoPublishedConfiguration, error!.Type);
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
        }
    }

    [Fact]
    public async Task GetPublishedConfigAsync_reports_tenant_not_found_for_an_unknown_tenant()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var session = await OpenAnonymousSessionAsync();
        var (documentJson, etag, error) = await BuildService(session.Db).GetPublishedConfigAsync(Guid.NewGuid());

        Assert.Null(documentJson);
        Assert.Null(etag);
        Assert.NotNull(error);
        Assert.Equal(MobileConfigReadErrorType.TenantNotFound, error!.Type);
    }

    [Fact]
    public async Task GetPublishedConfigAsync_keeps_two_tenants_published_configs_isolated()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        await SeedPublishedConfigAsync(tenantA.Id, true);
        await SeedPublishedConfigAsync(tenantB.Id, false);

        try
        {
            await using var sessionA = await OpenAnonymousSessionAsync();
            var (documentA, _, errorA) = await BuildService(sessionA.Db).GetPublishedConfigAsync(tenantA.Id);
            Assert.Null(errorA);
            Assert.NotNull(documentA);

            await using var sessionB = await OpenAnonymousSessionAsync();
            var (documentB, _, errorB) = await BuildService(sessionB.Db).GetPublishedConfigAsync(tenantB.Id);
            Assert.Null(errorB);
            Assert.NotNull(documentB);

            Assert.Contains(tenantA.Id.ToString(), documentA);
            Assert.DoesNotContain(tenantB.Id.ToString(), documentA!);
            Assert.Contains(tenantB.Id.ToString(), documentB);
            Assert.DoesNotContain(tenantA.Id.ToString(), documentB!);
        }
        finally
        {
            await CleanupTenantConfigAsync(tenantA.Id);
            await CleanupTenantConfigAsync(tenantB.Id);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static MobileConfigPublishedReadService BuildService(AppDbContext db) => new(
        new MobileConfigurationRepository(db), new TenantRepository(db), new TenantSessionOverride(db));

    private async Task<Tenant> SeedTenantAsync()
    {
        await using var db = NewContext();
        var tenant = Tenant.Create(
            $"MobileConfig Read RLS Test {Guid.NewGuid():N}", $"mobileconfig-read-rls-test-{Guid.NewGuid():N}");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<(MobileConfiguration Config, MobileConfigurationVersion Version)> SeedPublishedConfigAsync(
        Guid tenantId, bool newsFeatureValue)
    {
        await using var db = NewContext();
        var config = MobileConfiguration.Create(tenantId);

        // Built via JsonObject/ToJsonString rather than a hand-written string literal — this JSON
        // is brace-heavy enough that a raw interpolated string literal ($$"""...{{x}}...""") runs
        // into C#'s "not enough $ for this many consecutive closing braces" ambiguity (CS9007);
        // constructing it as nodes sidesteps that entirely and is also more obviously correct.
        var configurationJson = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["features"] = new JsonObject { ["news"] = newsFeatureValue },
            ["navigation"] = new JsonArray
            {
                new JsonObject { ["type"] = "home", ["label"] = "Home", ["icon"] = "home" },
                new JsonObject { ["type"] = "profile", ["label"] = "Profile", ["icon"] = "user" },
            },
            ["pages"] = new JsonObject(),
        }.ToJsonString();

        var version = MobileConfigurationVersion.Create(
            config.Id, tenantId, version: 1, schemaVersion: 1, configurationJson: configurationJson);
        version.Publish(DateTime.UtcNow);

        // Two SaveChanges calls, deliberately: MobileConfiguration.PublishedVersionId and
        // MobileConfigurationVersion.MobileConfigurationId form a genuine row-level insert cycle
        // when both rows are brand new (config needs version's PK for its FK; version needs
        // config's PK for its FK) — EF's batch INSERT ordering can satisfy neither table-level FK
        // first in one SaveChangesAsync call ("circular dependency was detected"). Insert both
        // rows with the pointer still null, then set/persist the pointer as a separate UPDATE —
        // same two-step shape the real publish flow (TASK-544) will need.
        db.MobileConfigurations.Add(config);
        db.MobileConfigurationVersions.Add(version);
        await db.SaveChangesAsync();

        config.SetPublishedVersion(version.Id);
        await db.SaveChangesAsync();

        return (config, version);
    }

    private async Task SeedDraftOnlyConfigAsync(Guid tenantId, string draftMarker)
    {
        await using var db = NewContext();
        var config = MobileConfiguration.Create(tenantId);

        var configurationJson = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["features"] = new JsonObject(),
            ["navigation"] = new JsonArray(),
            ["pages"] = new JsonObject(),
            ["marker"] = draftMarker,
        }.ToJsonString();

        var draft = MobileConfigurationVersion.Create(
            config.Id, tenantId, version: 1, schemaVersion: 1, configurationJson: configurationJson);

        // Same two-step insert-then-point shape as SeedPublishedConfigAsync — see its comment.
        db.MobileConfigurations.Add(config);
        db.MobileConfigurationVersions.Add(draft);
        await db.SaveChangesAsync();

        config.SetDraftVersion(draft.Id);
        await db.SaveChangesAsync();
    }

    private async Task<AnonymousSession> OpenAnonymousSessionAsync()
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        // Exact shape TenantConnectionInterceptor produces for a real unauthenticated request (the
        // user.Identity.IsAuthenticated != true branch) — every app.* variable RESET, not merely
        // absent, matching this endpoint's own [AllowAnonymous] posture.
        await db.Database.ExecuteSqlRawAsync(
            "RESET app.tenant_id; RESET app.role; RESET app.user_id; RESET app.consumer_account_id;");
        return new AnonymousSession(db);
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
    /// Wraps a manually-opened, role-switched AppDbContext connection so callers can
    /// `await using` it and get RESET ROLE on disposal — load-bearing now that every context in a
    /// test method shares one NpgsqlDataSource/pool (see LoyaltyJoinRlsIntegrationTests'
    /// ConsumerSession comment for the full reasoning).
    /// </summary>
    private sealed class AnonymousSession : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public AnonymousSession(AppDbContext db) => Db = db;

        public async ValueTask DisposeAsync()
        {
            try { await Db.Database.ExecuteSqlRawAsync("RESET ROLE;"); }
            catch { /* best-effort cleanup only */ }
            await Db.DisposeAsync();
        }
    }
}
