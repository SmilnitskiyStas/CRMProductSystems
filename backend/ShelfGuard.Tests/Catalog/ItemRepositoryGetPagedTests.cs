using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Catalog;

/// <summary>
/// TASK-572 — <c>ItemRepository.GetPagedAsync</c>'s new <c>ids</c> filter. The <c>search</c> filter
/// (Npgsql-only <c>EF.Functions.ILike</c>) needs real Postgres to exercise and isn't covered here —
/// same InMemory-provider limitation already true of the identical pattern in
/// <c>ConsumerContentRepository.GetCatalogPagedAsync</c> (no prior test in this repo covers that
/// ILike branch either); <see cref="ShelfGuard.Tests.Catalog.ItemServiceTests"/> and
/// <see cref="ShelfGuard.Tests.Catalog.ItemsControllerTests"/> cover that <c>search</c> is wired
/// through unchanged at the layers above this one. <c>ids</c> uses plain <c>Contains</c>, which the
/// InMemory provider does support — same convention <c>StockRepositoryFefoTests</c> already uses.
/// </summary>
public sealed class ItemRepositoryGetPagedTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"items-paged-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task GetPagedAsync_WithIds_ReturnsExactSetRegardlessOfAlphabeticalPosition()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();

        // "Z..." sorts last alphabetically — outside any short default-page window, proving the
        // ids filter (not OrderBy/Skip/Take) is what selects these rows.
        var wanted = new Item { TenantId = tenantId, Name = "Zebra Product", ManagementType = "MTS" };
        var alsoWanted = new Item { TenantId = tenantId, Name = "Apple Product", ManagementType = "MTS" };
        var notWanted = new Item { TenantId = tenantId, Name = "Middle Product", ManagementType = "MTS" };
        db.Items.AddRange(wanted, alsoWanted, notWanted);
        await db.SaveChangesAsync();

        var repo = new ItemRepository(db);
        var (items, total) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: null,
            ids: [wanted.Id, alsoWanted.Id], sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Id == wanted.Id);
        Assert.Contains(items, i => i.Id == alsoWanted.Id);
        Assert.DoesNotContain(items, i => i.Id == notWanted.Id);
    }

    [Fact]
    public async Task GetPagedAsync_IdNotInCatalog_IsSilentlyAbsentNotAnError()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var existing = new Item { TenantId = tenantId, Name = "Existing", ManagementType = "MTS" };
        db.Items.Add(existing);
        await db.SaveChangesAsync();

        var repo = new ItemRepository(db);
        var (items, total) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: null,
            ids: [existing.Id, Guid.NewGuid()], sortBy: null, sortDescending: null, page: 1, pageSize: 30);

        Assert.Equal(1, total);
        Assert.Equal(existing.Id, items.Single().Id);
    }

    [Fact]
    public async Task GetPagedAsync_NoIdsOrSearch_BehavesByteIdenticallyToToday()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        db.Items.AddRange(
            new Item { TenantId = tenantId, Name = "B Item", ManagementType = "MTS" },
            new Item { TenantId = tenantId, Name = "A Item", ManagementType = "MTS" });
        await db.SaveChangesAsync();

        var repo = new ItemRepository(db);
        var (items, total) = await repo.GetPagedAsync(
            categoryId: null, segmentId: null, managementType: null, search: null,
            ids: null, sortBy: null, sortDescending: null, page: 1, pageSize: 50);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
        Assert.Equal("A Item", items[0].Name); // OrderBy(Name) unchanged
    }
}
