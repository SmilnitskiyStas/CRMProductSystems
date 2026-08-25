using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Reviews;

/// <summary>
/// TASK-618: pins the actual EF query behind
/// <see cref="PurchaseReviewRepository.GetRecentForCustomerAsync"/>, which
/// CustomerServiceTests cannot exercise because it mocks
/// <see cref="ShelfGuard.Domain.Interfaces.IPurchaseReviewRepository"/> directly.
/// <see cref="PurchaseReview"/> carries no CustomerId of its own — this joins through
/// <see cref="PosTransaction.CustomerId"/> (see the interface doc for why).
/// </summary>
public sealed class PurchaseReviewRepositoryGetRecentForCustomerTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"review-recent-{Guid.NewGuid()}")
            .Options);

    private static PosTransaction Transaction(Guid tenantId, Guid? customerId) => new()
    {
        TenantId = tenantId,
        StoreId = Guid.NewGuid(),
        ReceiptNumber = Guid.NewGuid().ToString("N"),
        CustomerId = customerId,
    };

    private static PurchaseReview Review(Guid tenantId, Guid posTransactionId, short rating, DateTimeOffset createdAt) => new()
    {
        TenantId = tenantId,
        ConsumerAccountId = Guid.NewGuid(),
        PosTransactionId = posTransactionId,
        Rating = rating,
        CreatedAt = createdAt,
    };

    [Fact]
    public async Task GetRecentForCustomerAsync_ReturnsOnlyThisCustomersReviews_NewestFirst()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();

        var myOldTx = Transaction(tenantId, customerId);
        var myNewTx = Transaction(tenantId, customerId);
        var otherTx = Transaction(tenantId, otherCustomerId);
        db.PosTransactions.AddRange(myOldTx, myNewTx, otherTx);

        var now = DateTimeOffset.UtcNow;
        db.PurchaseReviews.AddRange(
            Review(tenantId, myOldTx.Id, 4, now.AddDays(-2)),
            Review(tenantId, myNewTx.Id, 5, now),
            Review(tenantId, otherTx.Id, 1, now.AddDays(-1)));
        await db.SaveChangesAsync();

        var repo = new PurchaseReviewRepository(db);
        var result = await repo.GetRecentForCustomerAsync(customerId, tenantId, take: 5);

        Assert.Equal(2, result.Count);
        Assert.Equal((short)5, result[0].Rating); // newest first
        Assert.Equal((short)4, result[1].Rating);
    }

    [Fact]
    public async Task GetRecentForCustomerAsync_RespectsTakeLimit()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 7; i++)
        {
            var tx = Transaction(tenantId, customerId);
            db.PosTransactions.Add(tx);
            db.PurchaseReviews.Add(Review(tenantId, tx.Id, 3, now.AddMinutes(-i)));
        }
        await db.SaveChangesAsync();

        var repo = new PurchaseReviewRepository(db);
        var result = await repo.GetRecentForCustomerAsync(customerId, tenantId, take: 5);

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task GetRecentForCustomerAsync_CustomerWithNoReviews_ReturnsEmptyList()
    {
        await using var db = MakeDb();
        var repo = new PurchaseReviewRepository(db);

        var result = await repo.GetRecentForCustomerAsync(Guid.NewGuid(), Guid.NewGuid(), take: 5);

        Assert.Empty(result);
    }
}
