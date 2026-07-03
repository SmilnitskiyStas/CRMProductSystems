using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Provider;

/// <summary>
/// BUG-014: the internal "platform-marketplace" system tenant (BUG-012) must never
/// show up in the provider panel's tenant list — it has no real users and is
/// permanently deactivated, so a provider clicking into it and creating an admin
/// user there produces an unreachable account.
/// </summary>
public sealed class TenantRepositoryPlatformTenantTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"bug014-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task GetAllAsync_ExcludesPlatformMarketplaceTenant()
    {
        await using var db = MakeDb();

        var realTenant = Tenant.Create("Real Client", "real-client");
        var platformTenant = Tenant.Create(
            "Platform Marketplace", MarketplaceRepository.PlatformTenantSlug);

        db.Tenants.AddRange(realTenant, platformTenant);
        await db.SaveChangesAsync();

        var repo = new TenantRepository(db);
        var result = await repo.GetAllAsync(default);

        Assert.Single(result);
        Assert.Equal(realTenant.Id, result[0].Id);
        Assert.DoesNotContain(result, t => t.Slug == MarketplaceRepository.PlatformTenantSlug);
    }
}
