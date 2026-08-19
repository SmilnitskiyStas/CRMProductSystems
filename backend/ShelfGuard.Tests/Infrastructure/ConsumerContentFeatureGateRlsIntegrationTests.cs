using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-558 — live-Postgres proof that <c>[RequireConsumerFeature]</c>, as actually wired onto
/// <c>ConsumerContentController.GetPromotions</c>/<c>GetCatalog</c>, holds the production-safety
/// contract under a REAL <see cref="ConsumerFeatureFlagService"/> backed by a REAL
/// <see cref="MobileConfigPublishedReadService"/>/repositories/RLS session — not a mocked
/// <c>IConsumerFeatureFlagService</c>, which is what <c>RequireConsumerFeatureFilterTests</c> and
/// <c>ConsumerFeatureFlagServiceTests</c> already exercise at the unit level. This is the
/// non-negotiable DoD proof for TASK-558: a tenant with ZERO <c>MobileConfiguration</c> activity —
/// i.e. every already-onboarded production tenant today — must still pass the gate.
///
/// Same skip-when-no-DB, TENANT_ISOLATION_TESTS collection convention as every other file in this
/// folder (see <see cref="MobileConfigPublishedReadRlsIntegrationTests"/> remarks).
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class ConsumerContentFeatureGateRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private string _connectionString = RlsAuditRoleFixture.DefaultConnectionString;
    private bool _dbAvailable;

    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    public ConsumerContentFeatureGateRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
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
                $"Skipping ConsumerContent feature-gate RLS integration tests — no reachable Postgres at '{_connectionString}': {_fixture.UnavailableReason}");
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
                $"Skipping ConsumerContent feature-gate RLS integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    /// <summary>
    /// THE critical DoD proof: a tenant with no MobileConfiguration row at all — the real shape of
    /// every production tenant today — must still be let through the gate for both flag keys
    /// actually attached to the controller ("promotions", "catalog"). If this fails, the wiring
    /// regressed the documented default-enabled safety contract and would 403 real production
    /// consumer traffic the moment this ships.
    /// </summary>
    [Theory]
    [InlineData("promotions")]
    [InlineData("catalog")]
    public async Task PRODUCTION_SAFETY_tenant_with_zero_MobileConfiguration_activity_passes_the_gate(string flagKey)
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync();

        try
        {
            await using var session = await OpenAnonymousSessionAsync();
            var filter = BuildRealFilter(session.Db, flagKey);
            var context = BuildActionContext(tenant.Id);
            var nextCalled = false;

            await filter.OnActionExecutionAsync(context, () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(
                    context, new List<IFilterMetadata>(), controller: new object()));
            });

            Assert.True(nextCalled);
            Assert.Null(context.Result);
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
        }
    }

    /// <summary>
    /// Converse of the safety-default proof, same real-service/real-DB stack: a tenant that HAS
    /// actually published a config explicitly setting the flag to false must be rejected with the
    /// filter's documented 403 shape.
    /// </summary>
    [Theory]
    [InlineData("promotions")]
    [InlineData("catalog")]
    public async Task Explicit_false_in_a_published_config_returns_403_through_the_real_stack(string flagKey)
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync();
        await SeedPublishedConfigAsync(tenant.Id, flagKey, enabled: false);

        try
        {
            await using var session = await OpenAnonymousSessionAsync();
            var filter = BuildRealFilter(session.Db, flagKey);
            var context = BuildActionContext(tenant.Id);

            await filter.OnActionExecutionAsync(context, () =>
                Task.FromResult(new ActionExecutedContext(
                    context, new List<IFilterMetadata>(), controller: new object())));

            var result = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static RequireConsumerFeatureFilter BuildRealFilter(AppDbContext db, string flagKey)
    {
        var publishedRead = new MobileConfigPublishedReadService(
            new MobileConfigurationRepository(db), new TenantRepository(db), new TenantSessionOverride(db));
        var flagService = new ConsumerFeatureFlagService(publishedRead);
        return new RequireConsumerFeatureFilter(flagKey, flagService);
    }

    private static ActionExecutingContext BuildActionContext(Guid tenantId)
    {
        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        routeData.Values["tenantId"] = tenantId.ToString();
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    private async Task<Tenant> SeedTenantAsync()
    {
        await using var db = NewContext();
        var tenant = Tenant.Create(
            $"ConsumerContent Gate RLS Test {Guid.NewGuid():N}", $"consumer-gate-rls-test-{Guid.NewGuid():N}");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task SeedPublishedConfigAsync(Guid tenantId, string flagKey, bool enabled)
    {
        await using var db = NewContext();
        var config = MobileConfiguration.Create(tenantId);

        var configurationJson = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["features"] = new JsonObject { [flagKey] = enabled },
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

        // Same two-step insert-then-point shape as MobileConfigPublishedReadRlsIntegrationTests
        // (config <-> version form a genuine FK insert cycle when both rows are brand new).
        db.MobileConfigurations.Add(config);
        db.MobileConfigurationVersions.Add(version);
        await db.SaveChangesAsync();

        config.SetPublishedVersion(version.Id);
        await db.SaveChangesAsync();
    }

    private async Task<AnonymousSession> OpenAnonymousSessionAsync()
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        // Same shape TenantConnectionInterceptor produces for a real unauthenticated request — every
        // app.* variable RESET, matching ConsumerContentController's [AllowAnonymous] posture.
        await db.Database.ExecuteSqlRawAsync(
            "RESET app.tenant_id; RESET app.role; RESET app.user_id; RESET app.consumer_account_id;");
        return new AnonymousSession(db);
    }

    private async Task CleanupTenantConfigAsync(Guid tenantId)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE mobile_configurations SET \"PublishedVersionId\" = NULL, \"DraftVersionId\" = NULL WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM mobile_configuration_versions WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM mobile_configurations WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" = {tenantId}");
    }

    private AppDbContext NewContext() => new(_options!);

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
