using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.ConsumerContent;

/// <summary>
/// TASK-572 — <c>ConsumerContentRepository.GetCatalogByIdsAsync</c>, the read path that resolves a
/// curated productIds selection (ADR-032) regardless of where the ids fall in the default
/// alphabetical page window. Unlike <c>GetCatalogPagedAsync</c>'s <c>search</c> filter (Npgsql-only
/// <c>EF.Functions.ILike</c>, needs real Postgres), this method's filter is plain
/// <c>ids.Contains(i.Id)</c> + an <c>IsActive</c> check — both InMemory-provider-safe, so this can
/// run as a fast unit test, same InMemory-EF convention <c>StockRepositoryFefoTests</c> already uses.
/// </summary>
public sealed class ConsumerContentRepositoryGetCatalogByIdsTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"catalog-by-ids-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task GetCatalogByIdsAsync_DeactivatedItem_IsExcluded()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();

        var active = new Item { TenantId = tenantId, Name = "Active Item", Unit = "шт", IsActive = true };
        var inactive = new Item { TenantId = tenantId, Name = "Inactive Item", Unit = "шт", IsActive = false };
        db.Items.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var repo = new ConsumerContentRepository(db);
        var result = await repo.GetCatalogByIdsAsync(tenantId, storeId, [active.Id, inactive.Id]);

        Assert.Single(result);
        Assert.Equal(active.Id, result[0].Id);
    }

    [Fact]
    public async Task GetCatalogByIdsAsync_IdNotInCatalog_IsSilentlyAbsentNotAnError()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();

        var existing = new Item { TenantId = tenantId, Name = "Existing", Unit = "шт", IsActive = true };
        db.Items.Add(existing);
        await db.SaveChangesAsync();

        var repo = new ConsumerContentRepository(db);
        var missingId = Guid.NewGuid();
        var result = await repo.GetCatalogByIdsAsync(tenantId, storeId, [existing.Id, missingId]);

        Assert.Single(result);
        Assert.Equal(existing.Id, result[0].Id);
    }

    [Fact]
    public async Task GetCatalogByIdsAsync_EmptyIds_ReturnsEmpty()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();

        var repo = new ConsumerContentRepository(db);
        var result = await repo.GetCatalogByIdsAsync(tenantId, storeId, []);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCatalogByIdsAsync_OtherTenantsItem_IsExcluded()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();

        var otherTenantItem = new Item { TenantId = otherTenantId, Name = "Other Tenant Item", Unit = "шт", IsActive = true };
        db.Items.Add(otherTenantItem);
        await db.SaveChangesAsync();

        var repo = new ConsumerContentRepository(db);
        var result = await repo.GetCatalogByIdsAsync(tenantId, storeId, [otherTenantItem.Id]);

        Assert.Empty(result);
    }
}
