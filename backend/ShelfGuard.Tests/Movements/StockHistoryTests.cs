using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ShelfGuard.Application.Features.Movements;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Movements;

public sealed class StockHistoryTests
{
    private static readonly DateOnly Start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly Guid Store = Guid.NewGuid();
    private static DateTime At(int day) => Start.AddDays(day).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"stock-history-{Guid.NewGuid()}").Options);
    private static ProductStock Batch(decimal initial, decimal current, int day = 0, Guid? store = null, Guid? tenant = null, Guid? product = null) => new()
    {
        TenantId = tenant ?? Tenant, ProductId = product ?? Product, StoreId = store ?? Store,
        QuantityInitial = initial, Quantity = current, AddedAt = At(day),
        ExpiryDate = Start.AddDays(30),
    };
    private static StockMovement Movement(ProductStock batch, decimal before, decimal after, int day,
        string type = "receipt", Guid? from = null, Guid? to = null, string? referenceType = null) => new()
    {
        TenantId = batch.TenantId, ProductId = batch.ProductId, ProductStockId = batch.Id,
        MovementType = type, Quantity = Math.Abs(after - before), QuantityBefore = before, QuantityAfter = after,
        FromStoreId = from, ToStoreId = to ?? batch.StoreId, CreatedAt = At(day),
        ReferenceType = referenceType,
    };
    private static void Sale(AppDbContext db, decimal quantity, int day, Guid? store = null, Guid? tenant = null,
        Guid? product = null, string status = "completed")
    {
        var transaction = new PosTransaction { TenantId = tenant ?? Tenant, StoreId = store ?? Store,
            Status = status, CreatedAt = At(day) };
        db.PosTransactions.Add(transaction);
        db.PosTransactionItems.Add(new PosTransactionItem { TransactionId = transaction.Id,
            ProductId = product ?? Product, Quantity = quantity });
    }
    private static async Task<decimal[]> History(AppDbContext db, Guid? store = null, int first = 0, int last = 5) =>
        (await new MovementService(new MovementRepository(db)).GetStockHistoryAsync(
            Tenant, Product, store, Start.AddDays(first), Start.AddDays(last))).Points.Select(p => p.Stock).ToArray();

    [Fact]
    public async Task SalesReduceTotalAcrossBatches_WithoutUsingLastBatchBalance()
    {
        await using var db = Db();
        var first = Batch(10, 8); var second = Batch(20, 17);
        db.ProductStocks.AddRange(first, second);
        db.StockMovements.AddRange(Movement(first, 0, 10, 0), Movement(second, 0, 20, 0));
        Sale(db, 5, 1);
        // The corresponding event must NOT cause a second subtraction.
        db.StockEvents.Add(new StockEvent { TenantId = Tenant, ProductId = Product, ProductStockId = first.Id,
            EventType = "pos_sale", QuantityDelta = -5, CreatedAt = At(1) });
        await db.SaveChangesAsync();
        Assert.Equal(new decimal[] { 30, 25, 25, 25, 25, 25 }, await History(db));
    }

    [Fact]
    public async Task SaleAndReceiptOnSameDay_AreBothIncluded()
    {
        await using var db = Db();
        var original = Batch(10, 7); var delivery = Batch(20, 20, day: 1);
        db.ProductStocks.AddRange(original, delivery);
        db.StockMovements.AddRange(Movement(original, 0, 10, 0), Movement(delivery, 0, 20, 1));
        Sale(db, 3, 1);
        await db.SaveChangesAsync();
        Assert.Equal(new decimal[] { 10, 27, 27, 27, 27, 27 }, await History(db));
    }

    [Fact]
    public async Task TransferRowsUseAffectedBatchStore_AndTenantProductFilters()
    {
        await using var db = Db();
        var destination = Guid.NewGuid();
        var sourceBatch = Batch(10, 7); var destinationBatch = Batch(3, 3, day: 1, store: destination);
        db.ProductStocks.AddRange(sourceBatch, destinationBatch, Batch(1000, 1000, tenant: Guid.NewGuid()),
            Batch(1000, 1000, product: Guid.NewGuid()));
        db.StockMovements.AddRange(Movement(sourceBatch, 0, 10, 0),
            Movement(sourceBatch, 10, 7, 1, "transfer", Store, destination),
            Movement(destinationBatch, 0, 3, 1, "transfer", Store, destination));
        Sale(db, 100, 1, tenant: Guid.NewGuid()); Sale(db, 100, 1, product: Guid.NewGuid());
        await db.SaveChangesAsync();
        Assert.Equal(new decimal[] { 10, 7, 7, 7, 7, 7 }, await History(db, Store));
        Assert.Equal(new decimal[] { 0, 3, 3, 3, 3, 3 }, await History(db, destination));
        Assert.Equal(Enumerable.Repeat(10m, 6), await History(db));
    }

    [Fact]
    public async Task OldWindowRewindsLaterSales_AndShortRangeHasSameBalance()
    {
        await using var db = Db();
        var batch = Batch(30, 23);
        db.ProductStocks.Add(batch); db.StockMovements.Add(Movement(batch, 0, 30, 0));
        Sale(db, 5, 2); Sale(db, 2, 4);
        await db.SaveChangesAsync();
        Assert.Equal(new decimal[] { 30, 30, 25, 25 }, await History(db, first: 0, last: 3));
        Assert.Equal(new decimal[] { 25, 25, 23, 23 }, await History(db, first: 2));
    }

    [Fact]
    public async Task MoreThanMovementPageLimit_AndFiscalizationFailureStillConsumesStock()
    {
        await using var db = Db();
        var batch = Batch(300, 49);
        db.ProductStocks.Add(batch); db.StockMovements.Add(Movement(batch, 0, 300, 0));
        for (var i = 0; i < 250; i++) Sale(db, 1, 1);
        Sale(db, 1, 1, status: "fiscalization_failed");
        Sale(db, 100, 1, status: "cancelled");
        await db.SaveChangesAsync();
        Assert.Equal(new decimal[] { 300, 49, 49, 49, 49, 49 }, await History(db));
    }

    [Fact]
    public async Task WriteOffsAndAdjustments_AreDeltas_NotReplacementProductBalances()
    {
        await using var db = Db();
        var batch = Batch(10, 9); var other = Batch(20, 20);
        db.ProductStocks.AddRange(batch, other);
        db.StockMovements.AddRange(Movement(batch, 0, 10, 0), Movement(other, 0, 20, 0),
            Movement(batch, 10, 7, 1, "write_off"), Movement(batch, 7, 9, 2, "adjustment"));
        await db.SaveChangesAsync();
        Assert.Equal(new decimal[] { 30, 27, 29, 29, 29, 29 }, await History(db));
    }

    [Fact]
    public async Task CancellingTransferFromADepletedLegacyBatchDoesNotHideItsInitialQuantity()
    {
        await using var db = Db();
        var batch = Batch(5, 5, day: 1);
        db.ProductStocks.Add(batch);
        db.StockMovements.AddRange(Movement(batch, 5, 0, 2, "transfer", Store, Guid.NewGuid()),
            Movement(batch, 0, 5, 3, "transfer", to: Store, referenceType: "stock_transfer_cancellation"));
        await db.SaveChangesAsync();
        Assert.Equal(new decimal[] { 0, 5, 0, 5, 5, 5 }, await History(db));
    }

    [Fact]
    public async Task BatchWithoutReceiptLedger_AndDepletedBatch_AreIncluded()
    {
        await using var db = Db();
        var batch = Batch(10, 0, day: 1);
        batch.Status = "archived";
        db.ProductStocks.Add(batch); Sale(db, 10, 2);
        await db.SaveChangesAsync();
        Assert.Equal(new decimal[] { 0, 10, 0, 0, 0, 0 }, await History(db));
    }

    [Fact]
    public async Task EmptyProductHistory_HasZeroBalance()
    {
        await using var db = Db();
        Assert.Equal(Enumerable.Repeat(0m, 6), await History(db));
    }

    [Fact]
    public async Task InvalidDatesAndProduct_AreRejectedBeforeQuery()
    {
        var repo = Substitute.For<IMovementRepository>();
        var service = new MovementService(repo);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetStockHistoryAsync(Tenant, Guid.Empty, null, Start, Start));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetStockHistoryAsync(Tenant, Product, null, Start.AddDays(1), Start));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetStockHistoryAsync(Tenant, Product, null, Start, Start.AddDays(6)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetStockHistoryAsync(Tenant, Product, null, Start.AddDays(-366), Start));
        Assert.Empty(repo.ReceivedCalls());
    }
}
