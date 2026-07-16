using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Stock;

/// <summary>
/// Block 3 pre-launch audit ("FEFO is sacred", CLAUDE.md): pins the actual EF query
/// behind GetFefoOrderedAsync, which StockServiceTests cannot exercise because it mocks
/// IStockRepository directly. Verifies the exclusions the DB partial indexes
/// (idx_stock_fefo_active: Quantity &gt; 0; idx_stock_expiry_active: Quantity &gt; 0 AND
/// Status NOT IN ('sold_out','archived')) assume are enforced by this query.
/// </summary>
public sealed class StockRepositoryFefoTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"fefo-{Guid.NewGuid()}")
            .Options);

    // Product/Store are required (non-nullable FK) navigations that GetFefoOrderedAsync
    // Includes — the InMemory provider excludes rows whose related entity is missing, so
    // every distinct productId/storeId used by a test's batches must have a matching row.
    private static void SeedCatalog(AppDbContext db, Guid tenantId, Guid productId, Guid storeId)
    {
        // .Local (not a store query) so back-to-back calls before SaveChanges see
        // entities added earlier in the same unit of work.
        if (!db.Items.Local.Any(i => i.Id == productId))
            db.Items.Add(new Item { Id = productId, TenantId = tenantId, Name = "Test Item" });
        if (!db.Locations.Local.Any(l => l.Id == storeId))
            db.Locations.Add(new Location { Id = storeId, TenantId = tenantId, Name = "Test Store" });
    }

    private static ProductStock Batch(
        Guid tenantId, Guid productId, Guid storeId,
        decimal quantity, int daysToExpiry, string status, string? batchNumber = null) => new()
    {
        TenantId = tenantId,
        ProductId = productId,
        StoreId = storeId,
        Quantity = quantity,
        QuantityInitial = quantity,
        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysToExpiry)),
        Status = status,
        BatchNumber = batchNumber,
        LastCheckedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task GetFefoOrderedAsync_ZeroQuantityBatch_IsExcluded()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        SeedCatalog(db, tenantId, productId, storeId);

        var soldOut = Batch(tenantId, productId, storeId, quantity: 0, daysToExpiry: 5, status: "sold_out", batchNumber: "SOLD-OUT");
        var active = Batch(tenantId, productId, storeId, quantity: 10, daysToExpiry: 10, status: "safe", batchNumber: "ACTIVE");
        db.ProductStocks.AddRange(soldOut, active);
        await db.SaveChangesAsync();

        var repo = new StockRepository(db);
        var result = await repo.GetFefoOrderedAsync(productId, storeId);

        var batchNumbers = result.Select(b => b.BatchNumber).ToList();
        Assert.DoesNotContain("SOLD-OUT", batchNumbers);
        Assert.Contains("ACTIVE", batchNumbers);
    }

    [Fact]
    public async Task GetFefoOrderedAsync_ArchivedBatchWithLeftoverQuantity_IsExcluded()
    {
        // Current invariant (cleanup.job.ts) only archives batches already at quantity=0,
        // so this row shouldn't occur in practice — but GetFefoOrderedAsync must not rely
        // solely on that invariant: an explicit status filter protects FEFO consumption if
        // some future path ever leaves quantity>0 on an archived batch.
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        SeedCatalog(db, tenantId, productId, storeId);

        var archivedButNotEmpty = Batch(tenantId, productId, storeId, quantity: 5, daysToExpiry: 1, status: "archived", batchNumber: "ARCHIVED");
        var active = Batch(tenantId, productId, storeId, quantity: 10, daysToExpiry: 10, status: "safe", batchNumber: "ACTIVE");
        db.ProductStocks.AddRange(archivedButNotEmpty, active);
        await db.SaveChangesAsync();

        var repo = new StockRepository(db);
        var result = await repo.GetFefoOrderedAsync(productId, storeId);

        var batchNumbers = result.Select(b => b.BatchNumber).ToList();
        Assert.DoesNotContain("ARCHIVED", batchNumbers);
        Assert.Contains("ACTIVE", batchNumbers);
    }

    [Fact]
    public async Task GetFefoOrderedAsync_OrdersByExpiryDateAscending_NearestFirst()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        SeedCatalog(db, tenantId, productId, storeId);

        var far = Batch(tenantId, productId, storeId, quantity: 10, daysToExpiry: 365, status: "safe", batchNumber: "FAR");
        var near = Batch(tenantId, productId, storeId, quantity: 10, daysToExpiry: 2, status: "critical", batchNumber: "NEAR");
        var mid = Batch(tenantId, productId, storeId, quantity: 10, daysToExpiry: 30, status: "safe", batchNumber: "MID");
        db.ProductStocks.AddRange(far, near, mid);
        await db.SaveChangesAsync();

        var repo = new StockRepository(db);
        var result = await repo.GetFefoOrderedAsync(productId, storeId);

        Assert.Equal(["NEAR", "MID", "FAR"], result.Select(b => b.BatchNumber));
    }

    [Fact]
    public async Task GetFefoOrderedAsync_DifferentStoreOrProduct_IsExcluded()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var otherStoreId = Guid.NewGuid();
        var otherProductId = Guid.NewGuid();
        SeedCatalog(db, tenantId, productId, storeId);
        SeedCatalog(db, tenantId, productId, otherStoreId);
        SeedCatalog(db, tenantId, otherProductId, storeId);

        var wrongStore = Batch(tenantId, productId, otherStoreId, quantity: 10, daysToExpiry: 5, status: "safe", batchNumber: "WRONG-STORE");
        var wrongProduct = Batch(tenantId, otherProductId, storeId, quantity: 10, daysToExpiry: 5, status: "safe", batchNumber: "WRONG-PRODUCT");
        var correct = Batch(tenantId, productId, storeId, quantity: 10, daysToExpiry: 5, status: "safe", batchNumber: "CORRECT");
        db.ProductStocks.AddRange(wrongStore, wrongProduct, correct);
        await db.SaveChangesAsync();

        var repo = new StockRepository(db);
        var result = await repo.GetFefoOrderedAsync(productId, storeId);

        Assert.Equal(["CORRECT"], result.Select(b => b.BatchNumber));
    }
}
