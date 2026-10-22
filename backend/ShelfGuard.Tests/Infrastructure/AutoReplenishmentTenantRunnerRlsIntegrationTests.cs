using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-755 live-Postgres proof: the worker's per-tenant auto-replenishment run executes under RLS
/// as exactly ONE tenant (connection is SET ROLE rls_audit_test_role - NOSUPERUSER NOBYPASSRLS).
/// Two tenants, each with a shop + a purchased item (buffer 10, no stock) + a self-produced item:
///   run(A) -> one auto draft in A (only the purchased item), nothing in B; run(B) -> only in B;
///   re-run is idempotent; a TAKEN draft is never modified and counts as in transit;
///   tenant without an active non-production location -> Skipped; unknown tenant -> NotFound;
///   the SET LOCAL context is gone after the run.
/// Also proves the in-transit repository filter (untaken auto drafts skipped only when asked).
/// Soft-skips when no Postgres is reachable or AddAutoReplenishmentDrafts is not applied.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class AutoReplenishmentTenantRunnerRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;
    private bool _available;
    private readonly List<Guid> _tenants = [];

    public AutoReplenishmentTenantRunnerRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!_fixture.DbAvailable)
        {
            _output.WriteLine($"Skipping auto-replenishment RLS tests - no Postgres: {_fixture.UnavailableReason}");
            return;
        }

        _dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).EnableDynamicJson().Build();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_dataSource).IgnoreManyServiceProvidersWarning().Options;

        await using var db = new AppDbContext(_options);
        var applied = await db.Database
            .SqlQueryRaw<bool>(
                "SELECT EXISTS (SELECT 1 FROM information_schema.columns " +
                "WHERE table_name = 'stock_receipts' AND column_name = 'TakenByUserId') AS \"Value\"")
            .SingleAsync();
        if (!applied)
        {
            _output.WriteLine("Skipping auto-replenishment RLS tests - migration AddAutoReplenishmentDrafts not applied.");
            return;
        }

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
                    "DELETE FROM stock_receipts WHERE \"TenantId\" = {0}",
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

    private sealed record TenantSeed(Guid TenantId, Guid StoreId, Guid BoughtItemId, Guid MadeItemId);

    private async Task<TenantSeed> SeedTenantAsync(string label, bool withShop = true)
    {
        await using var db = new AppDbContext(_options!);
        var run = Guid.NewGuid().ToString("N");
        var tenant = Tenant.Create($"AR Runner {label} {run}", $"ar-runner-{label}-{run}");
        _tenants.Add(tenant.Id);

        var store = new Location { TenantId = tenant.Id, Name = $"Store {label}", Type = withShop ? "shop" : "production" };
        var bought = new Item { TenantId = tenant.Id, Name = $"Milk {label}" };
        var made = new Item { TenantId = tenant.Id, Name = $"Bread {label}" };
        var recipe = new Recipe { TenantId = tenant.Id, Name = $"Bread recipe {label}", OutputItemId = made.Id, OutputQty = 1m, Unit = "шт" };

        db.Tenants.Add(tenant);
        db.Locations.Add(store);
        db.Items.AddRange(bought, made);
        db.Recipes.Add(recipe);
        foreach (var item in new[] { bought, made })
            db.ProductBuffers.Add(new ProductBuffer
            {
                TenantId = tenant.Id, StoreId = store.Id, ProductId = item.Id,
                BufferTotal = 10m, BufferGreen = 4m, BufferYellow = 3m, BufferRed = 3m,
            });
        await db.SaveChangesAsync();
        return new TenantSeed(tenant.Id, store.Id, bought.Id, made.Id);
    }

    private async Task<(AutoReplenishmentRunResult Result, string? TenantAfter, string? RoleAfter)> RunAsync(Guid tenantId)
    {
        await using var db = new AppDbContext(_options!);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SET ROLE rls_audit_test_role; RESET app.tenant_id; RESET app.role; RESET app.user_id; RESET app.consumer_account_id;");
        try
        {
            var tenantContext = new TenantContext(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
            var receiptRepo = new ReceiptRepository(db);
            var notifications = new NotificationRepository(db);
            var receiptService = new Application.Features.Receipts.ReceiptService(
                receiptRepo, notifications, new ActivityLogRepository(db));
            var service = new AutoReplenishmentService(
                receiptRepo,
                new ProductionDemandRepository(db),
                new OrderCalcService(new OrderCalcRepository(db), new EventRepository(db), new WeatherRepository(db),
                    new CannibalizationRepository(db), tenantContext),
                notifications,
                receiptService);
            var runner = new AutoReplenishmentTenantRunner(db, tenantContext, service);

            var result = await runner.RunAsync(tenantId);

            var tenantAfter = await db.Database
                .SqlQueryRaw<string>("SELECT COALESCE(current_setting('app.tenant_id', true), '') AS \"Value\"").SingleAsync();
            var roleAfter = await db.Database
                .SqlQueryRaw<string>("SELECT COALESCE(current_setting('app.role', true), '') AS \"Value\"").SingleAsync();
            Assert.Null(tenantContext.TenantId);
            return (result, tenantAfter, roleAfter);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE;");
        }
    }

    private async Task<List<StockReceipt>> ReceiptsOfAsync(Guid tenantId)
    {
        await using var db = new AppDbContext(_options!); // superuser: sees the truth
        return await db.StockReceipts.AsNoTracking().Include(r => r.Items)
            .Where(r => r.TenantId == tenantId).OrderBy(r => r.CreatedAt).ToListAsync();
    }

    [Fact]
    public async Task RunForA_CreatesDraftOnlyInA_ThenB_AndIsIdempotent()
    {
        if (!_available) return;
        var a = await SeedTenantAsync("A");
        var b = await SeedTenantAsync("B");

        var (runA, tenantAfter, roleAfter) = await RunAsync(a.TenantId);
        Assert.Equal(AutoReplenishmentRunStatus.Completed, runA.Status);
        var draftA = Assert.Single(runA.Drafts);
        Assert.Equal(a.StoreId, draftA.DestinationStoreId);
        Assert.Equal("auto_replenishment", draftA.Source);
        var line = Assert.Single(draftA.Items);                 // self-produced item excluded
        Assert.Equal(a.BoughtItemId, line.ProductId);
        Assert.Equal(10m, line.QuantityOrdered);
        Assert.True(string.IsNullOrEmpty(tenantAfter), $"app.tenant_id leaked: '{tenantAfter}'");
        Assert.True(string.IsNullOrEmpty(roleAfter), $"app.role leaked: '{roleAfter}'");

        var inA = await ReceiptsOfAsync(a.TenantId);
        var persistedA = Assert.Single(inA);
        Assert.Equal(a.TenantId, persistedA.TenantId);
        Assert.Null(persistedA.CreatedBy);
        Assert.Null(persistedA.SupplierId);
        Assert.Empty(await ReceiptsOfAsync(b.TenantId));

        await using (var db = new AppDbContext(_options!))
        {
            var events = await db.NotificationQueues.AsNoTracking().Where(n => n.TenantId == a.TenantId).ToListAsync();
            var ev = Assert.Single(events);
            Assert.Equal("order.auto_draft", ev.EventType);
            Assert.Equal(a.StoreId, ev.StoreId);
        }

        var (runB, _, _) = await RunAsync(b.TenantId);
        Assert.Equal(AutoReplenishmentRunStatus.Completed, runB.Status);
        Assert.Equal(b.StoreId, Assert.Single(runB.Drafts).DestinationStoreId);
        Assert.Single(await ReceiptsOfAsync(b.TenantId));
        Assert.Equal(persistedA.Id, Assert.Single(await ReceiptsOfAsync(a.TenantId)).Id);

        // Re-run A: own untaken draft must NOT hide its own need (no duplicate, nothing changed).
        var (rerunA, _, _) = await RunAsync(a.TenantId);
        Assert.Equal(AutoReplenishmentRunStatus.Completed, rerunA.Status);
        Assert.Empty(rerunA.Drafts);
        var after = Assert.Single(await ReceiptsOfAsync(a.TenantId));
        Assert.Equal("draft", after.Status);
        Assert.Equal(10m, Assert.Single(after.Items).QuantityOrdered);
    }

    [Fact]
    public async Task TakenDraft_IsUntouched_CountsAsInTransit_AndRemainingNeedGetsNewDraft()
    {
        if (!_available) return;
        var a = await SeedTenantAsync("T");
        await RunAsync(a.TenantId);
        var taken = Assert.Single(await ReceiptsOfAsync(a.TenantId));

        await using (var db = new AppDbContext(_options!))
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE stock_receipts SET \"TakenByUserId\" = {0}, \"TakenAt\" = NOW() WHERE \"Id\" = {1}",
                Guid.NewGuid(), taken.Id);
        }

        // Buffer unchanged: the taken draft (10) covers it -> no new draft, taken one untouched.
        var (same, _, _) = await RunAsync(a.TenantId);
        Assert.Empty(same.Drafts);
        Assert.Single(await ReceiptsOfAsync(a.TenantId));

        // Buffer grows to 25: need 15 beyond the taken 10 -> a NEW untaken draft, taken stays at 10.
        await using (var db = new AppDbContext(_options!))
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE product_buffer SET \"BufferTotal\" = 25 WHERE \"TenantId\" = {0} AND \"ProductId\" = {1}",
                a.TenantId, a.BoughtItemId);
        }
        var (grown, _, _) = await RunAsync(a.TenantId);
        var created = Assert.Single(grown.Drafts);
        Assert.Equal(15m, Assert.Single(created.Items).QuantityOrdered);

        var all = await ReceiptsOfAsync(a.TenantId);
        Assert.Equal(2, all.Count);
        var stillTaken = all.Single(r => r.Id == taken.Id);
        Assert.NotNull(stillTaken.TakenByUserId);
        Assert.Equal(10m, Assert.Single(stillTaken.Items).QuantityOrdered);

        // Re-run: the new untaken draft is refreshed in place (not stacked on itself).
        var (again, _, _) = await RunAsync(a.TenantId);
        Assert.Empty(again.Drafts);
        Assert.Equal(2, (await ReceiptsOfAsync(a.TenantId)).Count);
    }

    [Fact]
    public async Task NeedDisappears_UntakenDraftCancelled()
    {
        if (!_available) return;
        var a = await SeedTenantAsync("C");
        await RunAsync(a.TenantId);

        await using (var db = new AppDbContext(_options!))
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE product_buffer SET \"BufferTotal\" = 0, \"BufferGreen\" = 0, \"BufferYellow\" = 0, \"BufferRed\" = 0 " +
                "WHERE \"TenantId\" = {0}", a.TenantId);
        }

        var (run, _, _) = await RunAsync(a.TenantId);
        Assert.Equal("cancelled", Assert.Single(run.Drafts).Status);
        Assert.Equal("cancelled", Assert.Single(await ReceiptsOfAsync(a.TenantId)).Status);
    }

    [Fact]
    public async Task TenantWithoutActiveNonProductionLocation_Skipped_NoWrites()
    {
        if (!_available) return;
        var c = await SeedTenantAsync("P", withShop: false);

        var (run, _, _) = await RunAsync(c.TenantId);

        Assert.Equal(AutoReplenishmentRunStatus.Skipped, run.Status);
        Assert.Empty(run.Drafts);
        Assert.Empty(await ReceiptsOfAsync(c.TenantId));
    }

    [Fact]
    public async Task UnknownTenant_NotFound()
    {
        if (!_available) return;
        var (run, _, _) = await RunAsync(Guid.NewGuid());
        Assert.Equal(AutoReplenishmentRunStatus.TenantNotFound, run.Status);
    }

    [Fact]
    public async Task InTransitRepository_SkipsOnlyUntakenAutoDrafts_WhenAsked()
    {
        if (!_available) return;
        var a = await SeedTenantAsync("R");

        await using var db = new AppDbContext(_options!);
        StockReceipt Receipt(string source, Guid? takenBy, decimal qty)
        {
            var r = new StockReceipt
            {
                TenantId = a.TenantId, DestinationStoreId = a.StoreId, Source = source, TakenByUserId = takenBy,
            };
            r.Items.Add(new StockReceiptItem { ReceiptId = r.Id, ProductId = a.BoughtItemId, QuantityOrdered = qty });
            return r;
        }
        db.StockReceipts.AddRange(
            Receipt(ReceiptSource.AutoReplenishment, null, 30m),          // own untaken auto draft
            Receipt(ReceiptSource.AutoReplenishment, Guid.NewGuid(), 5m), // taken auto draft
            Receipt(ReceiptSource.Manual, null, 7m));                     // manual draft
        await db.SaveChangesAsync();

        var repo = new OrderCalcRepository(db);
        var ids = new[] { a.BoughtItemId };
        var all = await repo.GetInTransitAsync(a.StoreId, ids);
        var filtered = await repo.GetInTransitAsync(a.StoreId, ids, default, excludeAutoUntakenDrafts: true);

        Assert.Equal(42m, all[a.BoughtItemId]);
        Assert.Equal(12m, filtered[a.BoughtItemId]);
    }
}
