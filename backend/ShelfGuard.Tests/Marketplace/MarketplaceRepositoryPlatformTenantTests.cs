using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// BUG-012: AdminCreateSupplierAsync used TenantId = Guid.Empty, violating the
/// suppliers→tenants FK in production. The InMemory/mocked tests of TASK-275 never
/// caught it because they don't enforce FKs — these tests instead pin the new
/// behavior: a real "Platform Marketplace" tenant row is get-or-created and reused.
/// </summary>
public sealed class MarketplaceRepositoryPlatformTenantTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"bug012-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task GetOrCreatePlatformTenantIdAsync_FirstCall_CreatesSystemTenant()
    {
        await using var db = MakeDb();
        var repo = new MarketplaceRepository(db, new PassThroughProviderRlsOverride());

        var id = await repo.GetOrCreatePlatformTenantIdAsync();

        var tenant = await db.Tenants.SingleAsync();
        Assert.Equal(id, tenant.Id);
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(MarketplaceRepository.PlatformTenantSlug, tenant.Slug);
        Assert.Equal("supplier", tenant.BusinessType);
        Assert.False(tenant.IsActive);          // system tenant — not loginable
        Assert.Empty(tenant.Users);             // no users → cabinet/auth unreachable
    }

    [Fact]
    public async Task GetOrCreatePlatformTenantIdAsync_SecondCall_ReusesExistingTenant()
    {
        await using var db = MakeDb();
        var repo = new MarketplaceRepository(db, new PassThroughProviderRlsOverride());

        var first  = await repo.GetOrCreatePlatformTenantIdAsync();
        var second = await repo.GetOrCreatePlatformTenantIdAsync();

        Assert.Equal(first, second);
        Assert.Equal(1, await db.Tenants.CountAsync());
    }

    [Fact]
    public async Task GetOrCreatePlatformTenantIdAsync_ReusesAcrossContexts()
    {
        // Simulates two separate requests (scoped DbContexts) against the same DB.
        var dbName = $"bug012-shared-{Guid.NewGuid()}";
        DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

        Guid first, second;
        await using (var db1 = new AppDbContext(Options()))
            first = await new MarketplaceRepository(db1, new PassThroughProviderRlsOverride())
                .GetOrCreatePlatformTenantIdAsync();
        await using (var db2 = new AppDbContext(Options()))
        {
            second = await new MarketplaceRepository(db2, new PassThroughProviderRlsOverride())
                .GetOrCreatePlatformTenantIdAsync();
            Assert.Equal(first, second);
            Assert.Equal(1, await db2.Tenants.CountAsync());
        }
    }

    [Fact]
    public async Task GetOwnerManagedProfileAsync_NeverResolvesPlatformSuppliers()
    {
        // A provider-created supplier lives under the platform tenant with
        // IsOwnerManaged = false — the supplier cabinet must not pick it up.
        await using var db = MakeDb();
        var repo = new MarketplaceRepository(db, new PassThroughProviderRlsOverride());

        var platformTenantId = await repo.GetOrCreatePlatformTenantIdAsync();

        var supplier = new Supplier { TenantId = platformTenantId, Name = "Provider Created" };
        db.Suppliers.Add(supplier);
        db.SupplierProfiles.Add(new SupplierProfile
        {
            SupplierId     = supplier.Id,
            TenantId       = platformTenantId,
            IsPublic       = true,
            IsOwnerManaged = false,
        });
        await db.SaveChangesAsync();

        Assert.Null(await repo.GetOwnerManagedProfileAsync(platformTenantId));
    }
}
