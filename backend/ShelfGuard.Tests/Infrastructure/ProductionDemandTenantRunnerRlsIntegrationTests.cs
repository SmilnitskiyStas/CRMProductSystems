using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-754 live-Postgres proof: the worker's per-tenant production-demand run executes under
/// RLS as exactly ONE tenant (connection is SET ROLE rls_audit_test_role — NOSUPERUSER
/// NOBYPASSRLS — so policies really apply). Two tenants, each with a store + production location
/// + self-produced item (active recipe) + CDA buffer and zero stock:
///   run(A) -> one auto draft in A, nothing in B; run(B) -> one auto draft in B, A unchanged;
///   tenant without the production module -> Skipped (no writes); unknown tenant -> NotFound;
///   the SET LOCAL context is gone after the run (cannot leak into a pooled connection).
/// Soft-skips when no Postgres is reachable or AddProductionRequests is not applied.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class ProductionDemandTenantRunnerRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;
    private bool _available;
    private readonly List<Guid> _tenants = [];

    public ProductionDemandTenantRunnerRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!_fixture.DbAvailable)
        {
            _output.WriteLine($"Skipping production-demand runner RLS tests - no Postgres: {_fixture.UnavailableReason}");
            return;
        }

        _dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).EnableDynamicJson().Build();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_dataSource).IgnoreManyServiceProvidersWarning().Options;

        await using var db = new AppDbContext(_options);
        var exists = await db.Database
            .SqlQueryRaw<bool>("SELECT to_regclass('public.production_requests') IS NOT NULL AS \"Value\"")
            .SingleAsync();
        if (!exists)
        {
            _output.WriteLine("Skipping production-demand runner RLS tests - migration AddProductionRequests not applied.");
            return;
        }

        // Tables created after the fixture's one-time GRANT ON ALL TABLES.
        await db.Database.ExecuteSqlRawAsync(
            "GRANT ALL PRIVILEGES ON production_requests, production_request_lines, production_order_request_lines " +
            "TO rls_audit_test_role;");
        _available = true;
    }

    public async Task DisposeAsync()
    {
        if (_available)
        {
            await using var db = new AppDbContext(_options!);
            foreach (var t in _tenants)
            {
                foreach (var sql in new[]
                {
                    "DELETE FROM production_requests WHERE \"TenantId\" = {0}",
                    "DELETE FROM notification_queue WHERE \"TenantId\" = {0}",
                    "DELETE FROM product_buffer WHERE \"TenantId\" = {0}",
                    "DELETE FROM recipes WHERE \"TenantId\" = {0}",
                    "DELETE FROM items WHERE \"TenantId\" = {0}",
                    "DELETE FROM locations WHERE \"TenantId\" = {0}",
                    "DELETE FROM tenants WHERE \"Id\" = {0}",
                })
                    await db.Database.ExecuteSqlRawAsync(sql, t);
            }
        }

        if (_dataSource is not null) await _dataSource.DisposeAsync();
    }

    private sealed record TenantSeed(Guid TenantId, Guid StoreId, Guid ProdId, Guid ItemId);

    private async Task<TenantSeed> SeedTenantAsync(string label, bool productionModule)
    {
        await using var db = new AppDbContext(_options!);
        var run = Guid.NewGuid().ToString("N");
        var tenant = Tenant.Create($"PD Runner {label} {run}", $"pd-runner-{label}-{run}");
        tenant.UpdateModules(productionModule ? ["inventory", "production"] : ["inventory", "pos"]);
        _tenants.Add(tenant.Id);

        var store = new Location { TenantId = tenant.Id, Name = $"Store {label}", Type = "shop" };
        var prod = new Location { TenantId = tenant.Id, Name = $"Kitchen {label}", Type = "production" };
        var item = new Item { TenantId = tenant.Id, Name = $"Bread {label}" };
        var recipe = new Recipe { TenantId = tenant.Id, Name = $"Bread recipe {label}", OutputItemId = item.Id, OutputQty = 1m, Unit = "шт" };
        var buffer = new ProductBuffer
        {
            TenantId = tenant.Id, StoreId = store.Id, ProductId = item.Id,
            BufferTotal = 10m, BufferGreen = 4m, BufferYellow = 3m, BufferRed = 3m,
        };

        db.Tenants.Add(tenant);
        db.Locations.AddRange(store, prod);
        db.Items.Add(item);
        db.Recipes.Add(recipe);
        db.ProductBuffers.Add(buffer);
        await db.SaveChangesAsync();
        return new TenantSeed(tenant.Id, store.Id, prod.Id, item.Id);
    }

    /// <summary>
    /// Real runner + real repositories on ONE connection switched to the non-bypass audit role,
    /// with all app.* session variables reset first (what the interceptor does for the
    /// unauthenticated internal request). Returns the result plus app.tenant_id/app.role as seen
    /// on the same connection right after the run.
    /// </summary>
    private async Task<(ProductionDemandRunResult Result, string? TenantAfter, string? RoleAfter)> RunAsync(Guid tenantId)
    {
        await using var db = new AppDbContext(_options!);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SET ROLE rls_audit_test_role; RESET app.tenant_id; RESET app.role; RESET app.user_id; RESET app.consumer_account_id;");
        try
        {
            var tenantContext = new TenantContext(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var demand = new ProductionDemandService(
                new ProductionDemandRepository(db),
                new ProductionRequestRepository(db),
                new OrderCalcService(new OrderCalcRepository(db), new EventRepository(db), new WeatherRepository(db),
                    new CannibalizationRepository(db), tenantContext),
                new NotificationRepository(db));
            var runner = new ProductionDemandTenantRunner(db, tenantContext, demand);

            var result = await runner.RunAsync(tenantId);

            var tenantAfter = await db.Database
                .SqlQueryRaw<string>("SELECT COALESCE(current_setting('app.tenant_id', true), '') AS \"Value\"").SingleAsync();
            var roleAfter = await db.Database
                .SqlQueryRaw<string>("SELECT COALESCE(current_setting('app.role', true), '') AS \"Value\"").SingleAsync();
            Assert.Null(tenantContext.TenantId); // job binding released
            return (result, tenantAfter, roleAfter);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE;");
        }
    }

    private async Task<List<ProductionRequest>> RequestsOfAsync(Guid tenantId)
    {
        await using var db = new AppDbContext(_options!); // superuser: sees the truth
        return await db.ProductionRequests.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.TenantId == tenantId).ToListAsync();
    }

    [Fact]
    public async Task RunForA_CreatesDraftOnlyInA_ThenRunForB_CreatesOnlyInB()
    {
        if (!_available) return;
        var a = await SeedTenantAsync("A", productionModule: true);
        var b = await SeedTenantAsync("B", productionModule: true);

        var (runA, tenantAfter, roleAfter) = await RunAsync(a.TenantId);
        Assert.Equal(ProductionDemandRunStatus.Completed, runA.Status);
        var draftA = Assert.Single(runA.Drafts);
        Assert.Equal(a.StoreId, draftA.StoreLocationId);
        Assert.Equal(a.ProdId, draftA.ProductionLocationId);
        Assert.Equal(10m, Assert.Single(draftA.Lines).RequestedQty);
        // SET LOCAL reverted with the transaction — nothing left on the connection.
        Assert.True(string.IsNullOrEmpty(tenantAfter), $"app.tenant_id leaked: '{tenantAfter}'");
        Assert.True(string.IsNullOrEmpty(roleAfter), $"app.role leaked: '{roleAfter}'");

        var inA = await RequestsOfAsync(a.TenantId);
        Assert.Single(inA);
        Assert.All(inA, r => Assert.All(r.Lines, l => Assert.Equal(a.TenantId, l.TenantId)));
        Assert.Empty(await RequestsOfAsync(b.TenantId));

        var (runB, _, _) = await RunAsync(b.TenantId);
        Assert.Equal(ProductionDemandRunStatus.Completed, runB.Status);
        var draftB = Assert.Single(runB.Drafts);
        Assert.Equal(b.StoreId, draftB.StoreLocationId);
        Assert.Equal(b.ProdId, draftB.ProductionLocationId);

        Assert.Single(await RequestsOfAsync(b.TenantId));
        var inAAfter = Assert.Single(await RequestsOfAsync(a.TenantId));
        Assert.Equal(inA[0].Id, inAAfter.Id);
        Assert.Equal(inA[0].UpdatedAt, inAAfter.UpdatedAt); // B's run did not touch A

        // Re-running A is idempotent (draft unchanged -> not returned, not duplicated).
        var (rerunA, _, _) = await RunAsync(a.TenantId);
        Assert.Equal(ProductionDemandRunStatus.Completed, rerunA.Status);
        Assert.Empty(rerunA.Drafts);
        Assert.Single(await RequestsOfAsync(a.TenantId));
    }

    [Fact]
    public async Task TenantWithoutProductionModule_Skipped_NoWrites()
    {
        if (!_available) return;
        var c = await SeedTenantAsync("C", productionModule: false);

        var (run, _, _) = await RunAsync(c.TenantId);

        Assert.Equal(ProductionDemandRunStatus.Skipped, run.Status);
        Assert.Empty(run.Drafts);
        Assert.Empty(await RequestsOfAsync(c.TenantId));
    }

    [Fact]
    public async Task UnknownTenant_NotFound()
    {
        if (!_available) return;
        var (run, _, _) = await RunAsync(Guid.NewGuid());
        Assert.Equal(ProductionDemandRunStatus.TenantNotFound, run.Status);
    }
}
