using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using ShelfGuard.Application.Features.Loyalty;
using ShelfGuard.Application.Features.Loyalty.Dtos;
using ShelfGuard.Application.Features.MobileConfig;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Authorization;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-559 — live-Postgres proof for Option A (discovery-only gate, chosen by the user over
/// Option B's hard gate — see .claude/tasks/mobile-roadmap.md TASK-559): <c>features.loyalty</c>
/// (TASK-543/558, <see cref="IConsumerFeatureFlagService"/>) hides a tenant from NEW
/// discovery/joining (<c>ConsumerLoyaltyController.Join</c>'s
/// <c>[RequireConsumerFeature("loyalty")]</c>, <see cref="LoyaltyService.GetAvailableNetworksAsync"/>)
/// but never revokes an EXISTING member's access to their own balance/code/history.
///
/// Same real-Postgres, real-service, no-mocks rigor as TASK-558's
/// <see cref="ConsumerContentFeatureGateRlsIntegrationTests"/> and TASK-417's
/// <see cref="LoyaltyJoinRlsIntegrationTests"/>: real <see cref="LoyaltyService"/> composed from
/// real repositories/<see cref="TenantSessionOverride"/>, real
/// <see cref="ConsumerFeatureFlagService"/> composed from a real
/// <see cref="MobileConfigPublishedReadService"/>, against actual dev Postgres switched to the
/// throwaway <c>rls_audit_test_role</c>. Same skip-when-no-DB, <c>TENANT_ISOLATION_TESTS</c>
/// collection convention as every other file in this folder.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class LoyaltyFeatureGateRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private string _connectionString = RlsAuditRoleFixture.DefaultConnectionString;
    private bool _dbAvailable;

    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    public LoyaltyFeatureGateRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
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
                $"Skipping Loyalty feature-gate RLS integration tests — no reachable Postgres at '{_connectionString}': {_fixture.UnavailableReason}");
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
                $"Skipping Loyalty feature-gate RLS integration tests — no reachable Postgres at '{_connectionString}': {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    // ── Join's [RequireConsumerFeature("loyalty")] filter — same proof shape as TASK-558 ──────

    /// <summary>
    /// THE non-negotiable production-safety proof: a tenant with NO MobileConfiguration row at
    /// all — every real production tenant today — must still pass the Join gate.
    /// </summary>
    [Fact]
    public async Task PRODUCTION_SAFETY_tenant_with_zero_MobileConfiguration_activity_passes_the_join_gate()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync("loyalty");

        try
        {
            await using var session = await OpenAnonymousSessionAsync();
            var filter = BuildRealFilter(session.Db);
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
            await CleanupTenantDataAsync(tenant.Id);
        }
    }

    /// <summary>Converse: a tenant that HAS published <c>features.loyalty: false</c> must be
    /// rejected with the filter's documented 403 shape when attempting to join.</summary>
    [Fact]
    public async Task Explicit_false_in_a_published_config_returns_403_through_the_real_join_gate()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync("loyalty");
        await SeedPublishedConfigAsync(tenant.Id, enabled: false);

        try
        {
            await using var session = await OpenAnonymousSessionAsync();
            var filter = BuildRealFilter(session.Db);
            var context = BuildActionContext(tenant.Id);

            await filter.OnActionExecutionAsync(context, () =>
                Task.FromResult(new ActionExecutedContext(
                    context, new List<IFilterMetadata>(), controller: new object())));

            var result = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
            var errorProp = result.Value!.GetType().GetProperty("error");
            Assert.Equal("Feature not enabled", errorProp!.GetValue(result.Value));
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
            await CleanupTenantDataAsync(tenant.Id);
        }
    }

    // ── GetAvailableNetworksAsync — real-DB proof of the new per-tenant filter ────────────────

    [Fact]
    public async Task PRODUCTION_SAFETY_GetAvailableNetworksAsync_includes_tenant_with_zero_MobileConfiguration_activity()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync("loyalty");

        try
        {
            await using var db = NewContext();
            var service = BuildLoyaltyService(db);

            var networks = await service.GetAvailableNetworksAsync();

            Assert.Contains(networks, n => n.TenantId == tenant.Id);
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
            await CleanupTenantDataAsync(tenant.Id);
        }
    }

    [Fact]
    public async Task GetAvailableNetworksAsync_excludes_tenant_with_published_features_loyalty_false()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var gatedTenant = await SeedTenantAsync("loyalty");
        var controlTenant = await SeedTenantAsync("loyalty");
        await SeedPublishedConfigAsync(gatedTenant.Id, enabled: false);

        try
        {
            await using var db = NewContext();
            var service = BuildLoyaltyService(db);

            var networks = await service.GetAvailableNetworksAsync();

            Assert.DoesNotContain(networks, n => n.TenantId == gatedTenant.Id);
            Assert.Contains(networks, n => n.TenantId == controlTenant.Id);
        }
        finally
        {
            await CleanupTenantConfigAsync(gatedTenant.Id);
            await CleanupTenantDataAsync(gatedTenant.Id);
            await CleanupTenantConfigAsync(controlTenant.Id);
            await CleanupTenantDataAsync(controlTenant.Id);
        }
    }

    // ── THE Option-A-vs-B proof ────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE test that actually distinguishes Option A (chosen) from Option B (rejected): a
    /// consumer joins a tenant's loyalty program WHILE <c>features.loyalty</c> is still enabled
    /// (or unpublished, same thing) — a real <see cref="LoyaltyMembership"/> row is created. The
    /// tenant THEN publishes <c>features.loyalty: false</c>. Under Option A, that pre-existing
    /// member must keep full access to their own membership/code/history/preferred-store —
    /// exactly what this test proves against the real service + real DB, alongside proving the
    /// same tenant has in fact dropped out of discovery (<see cref="GetAvailableNetworksAsync"/>)
    /// and now rejects a brand-new join through the real <see cref="RequireConsumerFeatureFilter"/>
    /// — the contrast is the whole point: discovery/join are gated, this existing member is not.
    /// </summary>
    [Fact]
    public async Task OptionA_existing_member_keeps_full_access_after_tenant_later_disables_loyalty_discovery()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var tenant = await SeedTenantAsync("loyalty");
        var (consumerAccountId, _) = await SeedConsumerAccountAsync("Наталя Бондаренко");
        var storeId = await SeedShoppableStoreAsync(tenant.Id, "Флагманський магазин");

        try
        {
            // 1. Join WHILE the flag is still (implicitly) enabled — no published config yet.
            LoyaltyMembershipSummaryDto? membership;
            await using (var joinSession = await OpenConsumerSessionAsync(consumerAccountId))
            {
                var (joined, joinError, _) = await BuildLoyaltyService(joinSession.Db)
                    .JoinAsync(consumerAccountId, tenant.Id);
                Assert.Null(joinError);
                Assert.NotNull(joined);
                membership = joined;
            }

            // 2. The tenant NOW publishes features.loyalty: false — after the membership exists.
            await SeedPublishedConfigAsync(tenant.Id, enabled: false);

            // 3. Discovery/new-join are gated: the tenant drops out of GetAvailableNetworksAsync,
            //    and a brand-new Join attempt is rejected by the real filter with 403.
            await using (var db = NewContext())
            {
                var networks = await BuildLoyaltyService(db).GetAvailableNetworksAsync();
                Assert.DoesNotContain(networks, n => n.TenantId == tenant.Id);
            }

            await using (var anonSession = await OpenAnonymousSessionAsync())
            {
                var filter = BuildRealFilter(anonSession.Db);
                var context = BuildActionContext(tenant.Id);

                await filter.OnActionExecutionAsync(context, () =>
                    Task.FromResult(new ActionExecutedContext(
                        context, new List<IFilterMetadata>(), controller: new object())));

                var result = Assert.IsType<ObjectResult>(context.Result);
                Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
            }

            // 4. THE Option-A proof: the EXISTING member's own data stays fully reachable —
            //    none of these four actions look at features.loyalty at all.
            await using (var memberSession = await OpenConsumerSessionAsync(consumerAccountId))
            {
                var service = BuildLoyaltyService(memberSession.Db);

                var memberships = await service.GetMembershipsForConsumerAsync(consumerAccountId);
                Assert.Contains(memberships, m => m.TenantId == tenant.Id);

                var (code, codeError, _) = await service.GetConsumerCodeAsync(consumerAccountId, tenant.Id);
                Assert.Null(codeError);
                Assert.NotNull(code);

                var (history, historyError, _) = await service.GetHistoryAsync(
                    consumerAccountId, tenant.Id, page: 1, pageSize: 50);
                Assert.Null(historyError);
                Assert.NotNull(history);

                var (updated, storeError, _) = await service.SetPreferredStoreAsync(
                    consumerAccountId, tenant.Id, storeId);
                Assert.Null(storeError);
                Assert.NotNull(updated);
                Assert.Equal(storeId, updated!.PreferredStoreId);
            }
        }
        finally
        {
            await CleanupTenantConfigAsync(tenant.Id);
            await CleanupTenantDataAsync(tenant.Id);
            await CleanupConsumerAsync(consumerAccountId);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static RequireConsumerFeatureFilter BuildRealFilter(AppDbContext db)
    {
        var publishedRead = new MobileConfigPublishedReadService(
            new MobileConfigurationRepository(db), new TenantRepository(db), new TenantSessionOverride(db));
        var flagService = new ConsumerFeatureFlagService(publishedRead);
        return new RequireConsumerFeatureFilter("loyalty", flagService);
    }

    private static LoyaltyService BuildLoyaltyService(AppDbContext db)
    {
        var publishedRead = new MobileConfigPublishedReadService(
            new MobileConfigurationRepository(db), new TenantRepository(db), new TenantSessionOverride(db));
        var flagService = new ConsumerFeatureFlagService(publishedRead);

        return new LoyaltyService(
            new LoyaltyRepository(db),
            new CustomerRepository(db),
            new TenantRepository(db),
            Substitute.For<IUserRepository>(),
            new ConsumerAccountRepository(db),
            new LocationRepository(db),
            Substitute.For<IPasswordHasher>(),
            BuildTotpStub(),
            Substitute.For<IResolveCodeAttemptTracker>(),
            Substitute.For<IActivityLogRepository>(),
            new TenantSessionOverride(db),
            flagService,
            NullLogger<LoyaltyService>.Instance);
    }

    private static ITotpService BuildTotpStub()
    {
        var totp = Substitute.For<ITotpService>();
        totp.GenerateSecret().Returns(_ => Guid.NewGuid().ToString("N"));
        totp.GenerateCode(Arg.Any<string>()).Returns("123456");
        return totp;
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

    private async Task<Tenant> SeedTenantAsync(params string[] modules)
    {
        await using var db = NewContext();
        var tenant = Tenant.Create(
            $"Loyalty Gate RLS Test {Guid.NewGuid():N}", $"loyalty-gate-rls-test-{Guid.NewGuid():N}");
        if (modules.Length > 0) tenant.UpdateModules(modules);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<(Guid Id, string Phone)> SeedConsumerAccountAsync(string fullName)
    {
        await using var db = NewContext();
        var id = Guid.NewGuid();
        var phone = $"+380{id:N}"[..13];
        db.ConsumerAccounts.Add(new ConsumerAccount
        {
            Id = id,
            Phone = phone,
            PasswordHash = "x",
            FullName = fullName,
            IsActive = true,
        });
        await db.SaveChangesAsync();
        return (id, phone);
    }

    private async Task<Guid> SeedShoppableStoreAsync(Guid tenantId, string name)
    {
        await using var db = NewContext();
        var location = new Location { TenantId = tenantId, Name = name, IsActive = true };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    private async Task SeedPublishedConfigAsync(Guid tenantId, bool enabled)
    {
        await using var db = NewContext();
        var config = MobileConfiguration.Create(tenantId);

        var configurationJson = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["features"] = new JsonObject { ["loyalty"] = enabled },
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
        await db.Database.ExecuteSqlRawAsync(
            "RESET app.tenant_id; RESET app.role; RESET app.user_id; RESET app.consumer_account_id;");
        return new AnonymousSession(db);
    }

    private async Task<ConsumerSession> OpenConsumerSessionAsync(Guid consumerAccountId)
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        await db.Database.ExecuteSqlRawAsync("RESET app.tenant_id;");
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"SET app.role = 'consumer'; SET app.consumer_account_id = '{consumerAccountId:D}';");
#pragma warning restore EF1002
        return new ConsumerSession(db);
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
    }

    private async Task CleanupTenantDataAsync(Guid tenantId)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM loyalty_ledger_entries WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM loyalty_memberships WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM locations WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM customers WHERE \"TenantId\" = {tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" = {tenantId}");
    }

    private async Task CleanupConsumerAsync(Guid consumerAccountId)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM consumer_accounts WHERE \"Id\" = {consumerAccountId}");
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

    private sealed class ConsumerSession : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public ConsumerSession(AppDbContext db) => Db = db;

        public async ValueTask DisposeAsync()
        {
            try { await Db.Database.ExecuteSqlRawAsync("RESET ROLE;"); }
            catch { /* best-effort cleanup only */ }
            await Db.DisposeAsync();
        }
    }
}
