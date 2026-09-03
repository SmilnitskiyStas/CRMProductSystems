using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Orders;

/// <summary>
/// Phase 4 (plan 1-partitioned-book.md, D5 / п.2) — <c>OrderCalcRepository
/// .GetOpenMarketplaceInTransitAsync</c>: the query shape that makes an open B2B marketplace
/// order visible to the replenishment engine so it stops recommending goods the buyer already
/// ordered. InMemory provider (same convention as <c>ItemRepositoryGetPagedTests</c>) — no raw
/// SQL / ILike here, just joins + group-by. RLS tenant scoping is exercised through the explicit
/// <c>it.TenantId == tenantId</c> filter; the real Postgres <c>tenant_isolation</c> layer on
/// <c>items</c> / <c>marketplace_orders</c> is covered by the RLS integration suite.
/// </summary>
public sealed class OrderCalcRepositoryOpenMarketplaceInTransitTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"mp-intransit-{Guid.NewGuid()}")
            .Options);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();

    private Item SeedItem(AppDbContext db, Guid supplierItemId, string unit = "шт", Guid? tenantId = null)
    {
        var item = new Item
        {
            TenantId = tenantId ?? _tenantId,
            Name = "Товар",
            ManagementType = "MTS",
            Unit = unit,
            SourceSupplierItemId = supplierItemId,
        };
        db.Items.Add(item);
        return item;
    }

    private MarketplaceOrder SeedOrder(
        AppDbContext db, string status, Guid supplierItemId, decimal qty,
        string lineUnit = "шт", Guid? destinationStoreId = null)
    {
        var order = new MarketplaceOrder
        {
            OrderNumber = "MP-2026-001",
            AgreementId = Guid.NewGuid(),
            SupplierTenantId = Guid.NewGuid(),
            ClientTenantId = _tenantId,
            Status = status,
            DestinationStoreId = destinationStoreId ?? _storeId,
        };
        order.Items.Add(new MarketplaceOrderItem
        {
            OrderId = order.Id,
            SupplierTenantId = order.SupplierTenantId,
            ClientTenantId = order.ClientTenantId,
            SupplierItemId = supplierItemId,
            ItemName = "Товар",
            Unit = lineUnit,
            Qty = qty,
        });
        db.MarketplaceOrders.Add(order);
        return order;
    }

    [Theory]
    [InlineData("new")]
    [InlineData("confirmed")]
    [InlineData("shipped")]
    public async Task Open_order_counts_toward_in_transit(string status)
    {
        await using var db = MakeDb();
        var supplierItemId = Guid.NewGuid();
        var item = SeedItem(db, supplierItemId);
        SeedOrder(db, status, supplierItemId, qty: 40);
        await db.SaveChangesAsync();

        var result = await new OrderCalcRepository(db)
            .GetOpenMarketplaceInTransitAsync(_storeId, [item.Id], _tenantId);

        Assert.Equal(40m, Assert.Contains(item.Id, result));
    }

    [Theory]
    [InlineData("delivered")]
    [InlineData("cancelled")]
    public async Task Closed_order_does_not_count(string status)
    {
        await using var db = MakeDb();
        var supplierItemId = Guid.NewGuid();
        var item = SeedItem(db, supplierItemId);
        SeedOrder(db, status, supplierItemId, qty: 40);
        await db.SaveChangesAsync();

        var result = await new OrderCalcRepository(db)
            .GetOpenMarketplaceInTransitAsync(_storeId, [item.Id], _tenantId);

        Assert.DoesNotContain(item.Id, result);
    }

    [Fact]
    public async Task Unit_mismatch_line_is_excluded()
    {
        await using var db = MakeDb();
        var supplierItemId = Guid.NewGuid();
        var item = SeedItem(db, supplierItemId, unit: "шт");
        // Order snapshot unit is "ящик" — a box is not an each; counting 10 boxes as 10 units
        // would understate demand coverage wildly, so the line is dropped (plan п.2).
        SeedOrder(db, "confirmed", supplierItemId, qty: 10, lineUnit: "ящик");
        await db.SaveChangesAsync();

        var result = await new OrderCalcRepository(db)
            .GetOpenMarketplaceInTransitAsync(_storeId, [item.Id], _tenantId);

        Assert.DoesNotContain(item.Id, result);
    }

    [Fact]
    public async Task Other_store_and_other_tenant_are_excluded()
    {
        await using var db = MakeDb();
        var supplierItemId = Guid.NewGuid();
        var item = SeedItem(db, supplierItemId);

        // Right item lineage, but the order is headed to a different store.
        SeedOrder(db, "confirmed", supplierItemId, qty: 15, destinationStoreId: Guid.NewGuid());
        await db.SaveChangesAsync();

        var repo = new OrderCalcRepository(db);

        Assert.DoesNotContain(item.Id,
            await repo.GetOpenMarketplaceInTransitAsync(_storeId, [item.Id], _tenantId));
        // A foreign tenant asking about the same item id sees nothing.
        Assert.DoesNotContain(item.Id,
            await repo.GetOpenMarketplaceInTransitAsync(_storeId, [item.Id], Guid.NewGuid()));
    }

    [Fact]
    public async Task Multiple_open_lines_for_one_item_are_summed()
    {
        await using var db = MakeDb();
        var supplierItemId = Guid.NewGuid();
        var item = SeedItem(db, supplierItemId);
        SeedOrder(db, "new", supplierItemId, qty: 12);
        SeedOrder(db, "shipped", supplierItemId, qty: 8);
        SeedOrder(db, "delivered", supplierItemId, qty: 100); // ignored
        await db.SaveChangesAsync();

        var result = await new OrderCalcRepository(db)
            .GetOpenMarketplaceInTransitAsync(_storeId, [item.Id], _tenantId);

        Assert.Equal(20m, Assert.Contains(item.Id, result));
    }
}
