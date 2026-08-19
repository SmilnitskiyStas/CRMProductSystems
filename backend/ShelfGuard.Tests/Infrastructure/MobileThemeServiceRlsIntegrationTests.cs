using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Features.MobileConfig.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-536 — live-Postgres, real-<see cref="AppDbContext"/> regression coverage for
/// <see cref="MobileThemeService.UpdateThemeAsync"/>'s first-ever-write path.
///
/// This is the FIRST code path that ever inserts a row into <c>mobile_themes</c> — before TASK-536,
/// that table was only ever read (<c>MobileConfigPublishedReadService</c> falls back to an
/// in-memory, never-persisted <c>MobileTheme.CreateDefault(...)</c> when no row exists yet). TASK-534
/// and TASK-534b already found two real EF/Postgres failure modes in this exact domain
/// (<c>mobile_configurations</c>/<c>mobile_configuration_versions</c>'s mutual-FK "circular
/// dependency detected") that <c>MobileConfigDraftServiceTests</c>' mocked-repository unit tests
/// never caught because they don't exercise real EF <c>SaveChanges</c> — so this file proves the
/// theme write path against real Postgres rather than trusting the mocked-repo
/// <c>MobileThemeServiceTests</c> alone.
///
/// Unlike that mutual-FK case, <c>MobileTheme.MobileConfigurationId</c> points ONE-DIRECTIONALLY at
/// <c>MobileConfiguration.Id</c> — <c>MobileConfiguration</c> holds no reverse pointer column at
/// <c>MobileTheme</c> (only a nullable navigation property) — so <see cref="MobileThemeService"/>
/// inserts both brand-new rows in a single <c>SaveChangesAsync()</c> call with no split needed. This
/// suite confirms that actually holds against real Postgres, and additionally proves the
/// <c>mobile_themes</c> RLS <c>tenant_isolation</c> policy's <c>USING</c> clause (no separate
/// <c>WITH CHECK</c>, TASK-531's canonical triad) really does permit this INSERT under an
/// authenticated staff session with a matching <c>app.tenant_id</c>.
///
/// Skips (soft-pass) when no reachable Postgres is configured, same convention as every other file
/// in this folder — override via <c>SHELFGUARD_TEST_DB_CONNECTION</c>.
///
/// TASK-553: part of the <c>TENANT_ISOLATION_TESTS</c> collection — <c>rls_audit_test_role</c> is
/// created once for the whole collection by <see cref="RlsAuditRoleFixture"/>, not by this class.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class MobileThemeServiceRlsIntegrationTests : IAsyncLifetime
{
    private static UpdateMobileThemeRequest ValidRequest(string primaryColor = "#1E40AF") => new(
        LogoUrl: "https://cdn.example.com/logo.png",
        PrimaryColor: primaryColor,
        SecondaryColor: "#222222",
        BackgroundColor: "#FFFFFF",
        SurfaceColor: "#F7F7F7",
        TextPrimaryColor: "#111111",
        TextSecondaryColor: "#777777",
        ButtonRadius: 10,
        CardRadius: 12,
        SpacingPreset: "compact");

    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private string _connectionString = RlsAuditRoleFixture.DefaultConnectionString;
    private bool _dbAvailable;

    // One shared NpgsqlDataSource/DbContextOptions per test-method instance — same reasoning as
    // MobileConfigDraftServiceRlsIntegrationTests' field comment (avoids tripping the process-wide
    // ManyServiceProvidersCreatedWarning-as-error threshold across `dotnet test`).
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    public MobileThemeServiceRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
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
                $"Skipping MobileThemeService RLS integration tests — no reachable Postgres at '{_connectionString}': {_fixture.UnavailableReason}");
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
                $"Skipping MobileThemeService RLS integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    [Fact]
    public async Task UpdateThemeAsync_first_ever_write_for_a_tenant_succeeds_against_real_postgres()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync();

        try
        {
            await using var session = await OpenTenantSessionAsync(tenant.Id);

            // The exact regression surface: tenant.Id has never had a MobileConfiguration OR a
            // MobileTheme row before, so this takes UpdateThemeAsync's "create both" branch — the
            // first-ever write into mobile_themes for any tenant. See class remarks.
            var service = BuildService(session.Db);
            // actingUserId: null — unlike MobileConfigurationVersion.CreatedBy, ActivityLog.UserId
            // has no real FK to `users` (see AppDbContext's ActivityLog mapping), so this is purely
            // "not the focus of this test" rather than a constraint-avoidance requirement.
            var (dto, errors) = await service.UpdateThemeAsync(tenant.Id, actingUserId: null, ValidRequest());

            Assert.Empty(errors);
            Assert.NotNull(dto);
            Assert.Equal("#1E40AF", dto!.PrimaryColor);
            Assert.NotNull(dto.UpdatedAt);

            // Prove it actually landed in Postgres, not just an in-memory DTO: read back via a
            // fresh context/session under the same tenant.
            await using var readSession = await OpenTenantSessionAsync(tenant.Id);
            var persisted = await new MobileConfigurationRepository(readSession.Db).GetByTenantIdAsync(tenant.Id);

            Assert.NotNull(persisted);
            Assert.NotNull(persisted!.Theme);
            Assert.Equal("#1E40AF", persisted.Theme!.PrimaryColor);
            Assert.Equal(10, persisted.Theme.ButtonRadius);
            Assert.Equal("compact", persisted.Theme.SpacingPreset);
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
        }
    }

    [Fact]
    public async Task UpdateThemeAsync_second_write_updates_the_same_theme_row_in_place()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync();

        try
        {
            await using var createSession = await OpenTenantSessionAsync(tenant.Id);
            var (created, createErrors) =
                await BuildService(createSession.Db).UpdateThemeAsync(tenant.Id, actingUserId: null, ValidRequest());
            Assert.Empty(createErrors);
            Assert.NotNull(created);

            await using var updateSession = await OpenTenantSessionAsync(tenant.Id);
            var (updated, updateErrors) =
                await BuildService(updateSession.Db).UpdateThemeAsync(
                    tenant.Id, actingUserId: null, ValidRequest(primaryColor: "#00FF00"));

            Assert.Empty(updateErrors);
            Assert.NotNull(updated);
            Assert.Equal("#00FF00", updated!.PrimaryColor);

            // Still exactly one mobile_themes row for this tenant (updated in place, not a second
            // row) — the same "no version proliferation on repeated saves" property
            // MobileConfigDraftService's update-in-place branch already guarantees for drafts.
            await using var countDb = NewContext();
            var count = await countDb.MobileThemes.CountAsync(t => t.TenantId == tenant.Id);
            Assert.Equal(1, count);
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static MobileThemeService BuildService(AppDbContext db) =>
        new(new MobileConfigurationRepository(db), new MobileThemeValidator(), new ActivityLogRepository(db));

    private async Task<Tenant> SeedTenantAsync()
    {
        await using var db = NewContext();
        var tenant = Tenant.Create(
            $"MobileTheme RLS Test {Guid.NewGuid():N}", $"mobiletheme-rls-test-{Guid.NewGuid():N}");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<TenantSession> OpenTenantSessionAsync(Guid tenantId)
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        // Guid-typed tenantId, not a raw external string — SET doesn't accept bind parameters, so
        // EF1002 is a false positive here (same reasoning as MobileConfigDraftServiceRlsIntegrationTests).
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
    /// can `await using` it and get RESET ROLE on disposal — load-bearing now that every context in
    /// a test method shares one NpgsqlDataSource/pool.
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
