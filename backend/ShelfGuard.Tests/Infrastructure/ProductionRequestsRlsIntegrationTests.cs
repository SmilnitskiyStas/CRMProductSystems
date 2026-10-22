using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Application.Features.Transfers;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Live-Postgres proof for <c>production_requests</c>: tenant_isolation, worker_bypass, RESTRICTIVE
/// store_scope (requesting store OR production location), plus a real request -> review -> ship flow
/// through the EF repositories (guards against the client-set-Guid "Modified" trap on new lines).
/// Soft-skips when no Postgres is reachable or the AddProductionRequests migration is not applied.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class ProductionRequestsRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;
    private bool _available;

    public ProductionRequestsRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (!_fixture.DbAvailable)
        {
            _output.WriteLine($"Skipping production_requests RLS tests - no Postgres: {_fixture.UnavailableReason}");
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
            _output.WriteLine("Skipping production_requests RLS tests - migration AddProductionRequests not applied.");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "GRANT ALL PRIVILEGES ON production_requests, production_request_lines, production_order_request_lines " +
            "TO rls_audit_test_role;");
        _available = true;
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null) await _dataSource.DisposeAsync();
    }

    private sealed record Seed(
        Guid TenantA, Guid TenantB, Guid StoreA, Guid ProdA, Guid OtherStoreA,
        Guid UserStore, Guid UserProd, Guid UserOther, Guid ItemA);

    private async Task<Seed> SeedAsync()
    {
        await using var db = new AppDbContext(_options!);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var storeA = Guid.NewGuid();
        var prodA = Guid.NewGuid();
        var otherA = Guid.NewGuid();
        var storeB = Guid.NewGuid();
        var prodB = Guid.NewGuid();
        var uStore = Guid.NewGuid();
        var uProd = Guid.NewGuid();
        var uOther = Guid.NewGuid();
        var itemA = Guid.NewGuid();
        var itemB = Guid.NewGuid();

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO tenants (""Id"", ""Name"", ""Slug"") VALUES
              ({a}, 'PR RLS A', {$"pr-rls-a-{a:N}"}), ({b}, 'PR RLS B', {$"pr-rls-b-{b:N}"})");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO locations (""Id"", ""TenantId"", ""Name"", ""Type"") VALUES
              ({storeA}, {a}, 'Store A', 'shop'), ({prodA}, {a}, 'Prod A', 'production'), ({otherA}, {a}, 'Other A', 'shop'),
              ({storeB}, {b}, 'Store B', 'shop'), ({prodB}, {b}, 'Prod B', 'production')");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO items (""Id"", ""TenantId"", ""Name"", ""MinStock"", ""MaxStock"", ""SafetyBuffer"") VALUES
              ({itemA}, {a}, 'Bread', 0, 0, 0), ({itemB}, {b}, 'Bread', 0, 0, 0)");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO users (""Id"", ""TenantId"", ""Email"", ""PasswordHash"", ""FullName"", ""Role"", ""IsActive"") VALUES
              ({uStore}, {a}, {$"pr-store-{a:N}@test.local"}, 'x', 'S', 'store_manager', true),
              ({uProd}, {a}, {$"pr-prod-{a:N}@test.local"}, 'x', 'P', 'storekeeper', true),
              ({uOther}, {a}, {$"pr-other-{a:N}@test.local"}, 'x', 'O', 'store_manager', true)");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO user_locations (""Id"", ""TenantId"", ""UserId"", ""LocationId"") VALUES
              (gen_random_uuid(), {a}, {uStore}, {storeA}),
              (gen_random_uuid(), {a}, {uProd}, {prodA}),
              (gen_random_uuid(), {a}, {uOther}, {otherA})");

        foreach (var (t, s, p, i) in new[] { (a, storeA, prodA, itemA), (b, storeB, prodB, itemB) })
        {
            var reqId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO production_requests (""Id"", ""TenantId"", ""StoreLocationId"", ""ProductionLocationId"", ""NeededBy"", ""Status"", ""Source"")
                VALUES ({reqId}, {t}, {s}, {p}, '2030-01-01', 'submitted', 'manual')");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO production_request_lines (""Id"", ""TenantId"", ""RequestId"", ""ItemId"", ""RequestedQty"")
                VALUES ({Guid.NewGuid()}, {t}, {reqId}, {i}, 5)");
        }

        return new Seed(a, b, storeA, prodA, otherA, uStore, uProd, uOther, itemA);
    }

    private async Task CleanupAsync(Seed s)
    {
        await using var db = new AppDbContext(_options!);
        foreach (var t in new[] { s.TenantA, s.TenantB })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM production_order_request_lines WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM production_orders WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM production_requests WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_transfer_items WHERE \"TransferId\" IN (SELECT \"Id\" FROM stock_transfers WHERE \"TenantId\" = {t})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_transfers WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_movements WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM notification_queue WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM product_stock WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM user_locations WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM users WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {t}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {t}");
        }
    }

    private async Task<int> CountAsync(Guid? tenant, string role, Guid? user)
    {
        await using var db = new AppDbContext(_options!);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
        await db.Database.ExecuteSqlRawAsync("RESET app.tenant_id; RESET app.role; RESET app.user_id;");
        if (tenant is not null) await db.Database.ExecuteSqlRawAsync($"SET app.tenant_id = '{tenant:D}';");
        if (role.Length > 0) await db.Database.ExecuteSqlRawAsync($"SET app.role = '{role}';");
        if (user is not null) await db.Database.ExecuteSqlRawAsync($"SET app.user_id = '{user:D}';");
        try
        {
            return await db.ProductionRequests.CountAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("RESET ROLE;");
        }
    }

    [Fact]
    public async Task StoreScope_StoreUser_And_ProductionUser_SeeRequest_UnrelatedUser_And_OtherTenant_DoNot()
    {
        if (!_available) return;
        var s = await SeedAsync();
        try
        {
            Assert.Equal(1, await CountAsync(s.TenantA, "store_manager", s.UserStore));
            Assert.Equal(1, await CountAsync(s.TenantA, "storekeeper", s.UserProd));
            Assert.Equal(0, await CountAsync(s.TenantA, "store_manager", s.UserOther));
            // scoped user with no user_locations at all -> fail closed
            Assert.Equal(0, await CountAsync(s.TenantA, "store_manager", Guid.NewGuid()));
            // enterprise_admin: tenant-wide, but never another tenant's rows
            Assert.Equal(1, await CountAsync(s.TenantA, "enterprise_admin", null));
            Assert.Equal(1, await CountAsync(s.TenantB, "enterprise_admin", null));
            // no tenant context -> nothing
            Assert.Equal(0, await CountAsync(null, "", null));
        }
        finally
        {
            await CleanupAsync(s);
        }
    }

    [Fact]
    public async Task WorkerAndProvider_SeeAllTenants()
    {
        if (!_available) return;
        var s = await SeedAsync();
        try
        {
            Assert.True(await CountAsync(null, "worker", null) >= 2);
            Assert.True(await CountAsync(null, "provider", null) >= 2);
        }
        finally
        {
            await CleanupAsync(s);
        }
    }

    /// <summary>
    /// Real EF repositories: create (submit) -> partial review -> ship against seeded production stock.
    /// Proves new lines are INSERTed (not mis-tracked as Modified), the FEFO transfer copies
    /// expiry/batch as-is, stock is deducted, and the request ends fulfilled with a linked transfer.
    /// </summary>
    [Fact]
    public async Task RealFlow_Create_Review_Ship_PersistsAndLinksTransfer()
    {
        if (!_available) return;
        var s = await SeedAsync();
        try
        {
            await using var db = new AppDbContext(_options!);
            var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4));
            var stockId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO product_stock (""Id"", ""TenantId"", ""ProductId"", ""LocationId"", ""Quantity"", ""QuantityInitial"", ""ExpiryDate"", ""BatchNumber"")
                VALUES ({stockId}, {s.TenantA}, {s.ItemA}, {s.ProdA}, 20, 20, {expiry}, 'B-1')");

            var requests = new ProductionRequestRepository(db);
            var production = new ProductionRepository(db);
            var transfers = new TransferService(new TransferRepository(db), new UserLocationRepository(db));
            var svc = new ProductionRequestService(requests, production, new NotificationRepository(db), transfers);

            var (created, cErr, _) = await svc.CreateAsync(
                new ProductionRequestCreateDto(s.StoreA, s.ProdA, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
                    "flow", [new(s.ItemA, 12m)], true),
                s.TenantA, s.UserStore);
            Assert.Null(cErr);
            Assert.Equal("submitted", created!.Status);
            Assert.Single(created.Lines);

            var (reviewed, rErr, _) = await svc.ReviewAsync(created.Id,
                new ProductionRequestReviewDto([new(created.Lines[0].Id, 8m, "ліміт")], null), s.UserProd);
            Assert.Null(rErr);
            Assert.Equal("partial", reviewed!.Status);

            var (shipped, sErr, _) = await svc.ShipAsync(created.Id, s.TenantA, s.UserProd);
            Assert.Null(sErr);
            Assert.Equal("fulfilled", shipped!.Status);
            Assert.NotNull(shipped.ShipmentTransferId);
            Assert.Equal("in_transit", shipped.ShipmentStatus);

            await using var verify = new AppDbContext(_options!);
            var transfer = await verify.StockTransfers.Include(t => t.Items)
                .SingleAsync(t => t.Id == shipped.ShipmentTransferId);
            Assert.Equal("production_to_store", transfer.TransferType);
            var item = Assert.Single(transfer.Items);
            Assert.Equal(8m, item.Quantity);
            Assert.Equal(expiry, item.ExpiryDate);
            Assert.Equal("B-1", item.BatchNumber);
            Assert.Equal(12m, (await verify.ProductStocks.SingleAsync(x => x.Id == stockId)).Quantity);
            Assert.Equal(1, await verify.NotificationQueues.CountAsync(
                n => n.TenantId == s.TenantA && n.EventType == ProductionRequestService.SubmittedEventType));
        }
        finally
        {
            await CleanupAsync(s);
        }
    }
}
