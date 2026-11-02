using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Auth;

public sealed class ConsumerPurchaseRepositoryTests
{
    [Fact]
    public async Task Purchases_are_owned_deduplicated_filtered_and_paginated_as_receipts()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"purchases-{Guid.NewGuid()}").Options);
        var tenant = Guid.NewGuid();
        var membership = Guid.NewGuid();
        var store = new Location { TenantId = tenant, Name = "Store A" };
        var otherStore = new Location { TenantId = tenant, Name = "Store B" };
        db.Locations.AddRange(store, otherStore);
        PosTransaction Purchase(Location location, int days, string status = "completed", Guid? owner = null, Guid? tenantOverride = null) => new()
        {
            TenantId = tenantOverride ?? tenant, StoreId = location.Id, TotalAmount = 100,
            ReceiptNumber = Guid.NewGuid().ToString(), Status = status,
            CreatedAt = DateTime.UtcNow.AddDays(days), LoyaltyMembershipId = owner,
        };
        var legacy = Purchase(store, -3);
        var zeroBonus = Purchase(store, -2, owner: membership);
        var anotherStore = Purchase(otherStore, -1, owner: membership);
        var unrelated = Purchase(store, 0, owner: Guid.NewGuid());
        db.PosTransactions.AddRange(legacy, zeroBonus, anotherStore, unrelated,
            Purchase(store, 0, "cancelled", membership), Purchase(store, 0, "fiscalization_failed", membership),
            Purchase(store, 0, owner: membership, tenantOverride: Guid.NewGuid()));
        var product = new Item { TenantId = tenant, Name = "Кава", Unit = "шт" };
        db.Items.Add(product);
        db.PosTransactionItems.Add(new PosTransactionItem { TransactionId = legacy.Id, ProductId = product.Id,
            Quantity = 2, PriceRetail = 60, DiscountAmount = 10, PriceFinal = 50 });
        db.LoyaltyLedgerEntries.AddRange(
            new LoyaltyLedgerEntry { TenantId = tenant, MembershipId = membership, PosTransactionId = legacy.Id,
                EntryType = "accrual", Amount = 2 },
            new LoyaltyLedgerEntry { TenantId = tenant, MembershipId = membership, PosTransactionId = legacy.Id,
                EntryType = "redemption", Amount = -1 },
            new LoyaltyLedgerEntry { TenantId = tenant, MembershipId = Guid.NewGuid(), PosTransactionId = unrelated.Id,
                EntryType = "accrual", Amount = 2 });
        await db.SaveChangesAsync();
        var repo = new LoyaltyRepository(db);
        var (all, total) = await repo.GetPurchasesPagedAsync(tenant, membership, null, 1, 50);
        Assert.Equal(3, total);
        Assert.Equal(new[] { anotherStore.Id, zeroBonus.Id, legacy.Id }, all.Select(p => p.Id));
        Assert.Equal("Store A", all[1].Store!.Name);
        var (filtered, filteredTotal) = await repo.GetPurchasesPagedAsync(tenant, membership, store.Id, 2, 1);
        Assert.Equal(2, filteredTotal);
        Assert.Equal(legacy.Id, Assert.Single(filtered).Id);
        var (empty, emptyTotal) = await repo.GetPurchasesPagedAsync(tenant, membership, Guid.NewGuid(), 1, 50);
        Assert.Empty(empty);
        Assert.Equal(0, emptyTotal);
        var details = await repo.GetPurchaseByIdAsync(tenant, membership, legacy.Id);
        Assert.NotNull(details);
        var line = Assert.Single(details.Items);
        Assert.Equal("Кава", line.Product!.Name);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(10m, line.DiscountAmount);
        Assert.Null(await repo.GetPurchaseByIdAsync(tenant, membership, unrelated.Id));
        Assert.Null(await repo.GetPurchaseByIdAsync(Guid.NewGuid(), membership, legacy.Id));
        Assert.Null(await repo.GetPurchaseByIdAsync(tenant, Guid.NewGuid(), zeroBonus.Id));
        Assert.Null(await repo.GetPurchaseByIdAsync(tenant, membership, Guid.NewGuid()));
    }
}
