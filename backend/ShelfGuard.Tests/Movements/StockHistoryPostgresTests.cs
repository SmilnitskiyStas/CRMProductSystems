using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Movements;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Movements;

public sealed class StockHistoryPostgresTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PostgreSqlQueries_ReturnDailyBalanceIncludingSales()
    {
        // Supply a disposable local database. Never silently use a shared/dev/production DB.
        var connection = Environment.GetEnvironmentVariable("SHELFGUARD_STOCK_HISTORY_TEST_DB");
        if (string.IsNullOrWhiteSpace(connection))
        {
            output.WriteLine("Not run: set SHELFGUARD_STOCK_HISTORY_TEST_DB to a disposable PostgreSQL database.");
            return;
        }
        var builder = new Npgsql.NpgsqlDataSourceBuilder(connection);
        builder.EnableDynamicJson();
        await using var dataSource = builder.Build();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm");
        await db.Database.EnsureCreatedAsync();
        var tenant = Tenant.Create("Stock history test", $"history-{Guid.NewGuid():N}");
        var store = new Location { TenantId = tenant.Id, Name = "First" };
        var otherStore = new Location { TenantId = tenant.Id, Name = "Second" };
        var product = new Item { TenantId = tenant.Id, Name = "Stock history item" };
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var batch = new ProductStock { TenantId = tenant.Id, ProductId = product.Id, StoreId = store.Id,
            QuantityInitial = 30, Quantity = 25, AddedAt = start, ExpiryDate = from.AddDays(30) };
        var otherBatch = new ProductStock { TenantId = tenant.Id, ProductId = product.Id, StoreId = otherStore.Id,
            QuantityInitial = 9, Quantity = 9, AddedAt = start, ExpiryDate = from.AddDays(30) };
        db.Tenants.Add(tenant); db.Locations.AddRange(store, otherStore); db.Items.Add(product);
        db.ProductStocks.AddRange(batch, otherBatch);
        db.StockMovements.Add(new StockMovement { TenantId = tenant.Id, ProductId = product.Id, ProductStockId = batch.Id,
            MovementType = "receipt", Quantity = 30, QuantityBefore = 0, QuantityAfter = 30, ToStoreId = store.Id,
            CreatedAt = start });
        var sale = new PosTransaction { TenantId = tenant.Id, StoreId = store.Id, CreatedAt = start.AddDays(1),
            Status = "fiscalization_failed" };
        db.PosTransactions.Add(sale);
        db.PosTransactionItems.Add(new PosTransactionItem { TransactionId = sale.Id, ProductId = product.Id, Quantity = 5 });
        await db.SaveChangesAsync();
        var service = new MovementService(new MovementRepository(db));
        var scoped = await service.GetStockHistoryAsync(tenant.Id, product.Id, store.Id, from, from.AddDays(2));
        var all = await service.GetStockHistoryAsync(tenant.Id, product.Id, null, from, from.AddDays(2));
        Assert.Equal(25m, scoped.CurrentStock);
        Assert.Equal(new decimal[] { 30, 25, 25 }, scoped.Points.Select(p => p.Stock));
        Assert.Equal(new decimal[] { 39, 34, 34 }, all.Points.Select(p => p.Stock));
    }
}
