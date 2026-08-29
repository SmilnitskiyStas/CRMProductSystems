using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.ConsumerContent;

public sealed class ConsumerContentRepositoryMobileCatalogLocationsTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"mobile-catalog-locations-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task GetCatalogPagedAsync_AssignedStore_ReturnsCuratedCatalog()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var product = new Item { TenantId = tenantId, Name = "Catalog product", Unit = "шт", IsActive = true, PriceRetail = 100m };
        var catalog = new MobileCatalogSettings
        {
            TenantId = tenantId, IsEnabled = true, Status = MobileCatalogPublicationStatus.Published,
            PublishAt = DateTime.UtcNow.AddHours(-1),
        };
        catalog.Items.Add(new MobileCatalogItem { TenantId = tenantId, SettingsId = catalog.Id, ProductId = product.Id, ProductNameSnapshot = product.Name, UnitSnapshot = product.Unit });
        catalog.Locations.Add(new MobileCatalogLocation { TenantId = tenantId, SettingsId = catalog.Id, LocationId = storeId });
        db.Items.Add(product); db.MobileCatalogSettings.Add(catalog);
        await db.SaveChangesAsync();

        var result = await new ConsumerContentRepository(db).GetCatalogPagedAsync(tenantId, storeId, null, null, 1, 20);

        Assert.Single(result.Items);
        Assert.Equal(product.Id, result.Items[0].Id);
        Assert.Equal(catalog.Id, result.Items[0].CatalogId);
    }

    [Fact]
    public async Task GetCatalogPagedAsync_UnassignedStore_ReturnsNoCatalogItems()
    {
        await using var db = MakeDb();
        var tenantId = Guid.NewGuid();
        var assignedStoreId = Guid.NewGuid();
        var requestedStoreId = Guid.NewGuid();
        var product = new Item { TenantId = tenantId, Name = "Catalog product", Unit = "шт", IsActive = true, PriceRetail = 100m };
        var catalog = new MobileCatalogSettings
        {
            TenantId = tenantId, IsEnabled = true, Status = MobileCatalogPublicationStatus.Published,
            PublishAt = DateTime.UtcNow.AddHours(-1),
        };
        catalog.Items.Add(new MobileCatalogItem { TenantId = tenantId, SettingsId = catalog.Id, ProductId = product.Id, ProductNameSnapshot = product.Name, UnitSnapshot = product.Unit });
        catalog.Locations.Add(new MobileCatalogLocation { TenantId = tenantId, SettingsId = catalog.Id, LocationId = assignedStoreId });
        db.Items.Add(product); db.MobileCatalogSettings.Add(catalog);
        await db.SaveChangesAsync();

        var result = await new ConsumerContentRepository(db).GetCatalogPagedAsync(tenantId, requestedStoreId, null, null, 1, 20);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }
}
