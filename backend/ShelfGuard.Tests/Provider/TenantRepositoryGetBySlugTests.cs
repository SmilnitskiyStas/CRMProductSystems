using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Provider;

/// <summary>
/// TASK-548: <see cref="TenantRepository.GetBySlugAsync"/> — the single-tenant-by-slug lookup
/// added to back the slug-addressed <c>/api/v1/retailers/{slug}</c> discovery endpoints. Same
/// InMemory-DB pattern as <see cref="TenantRepositoryPlatformTenantTests"/> — no RLS is exercised
/// here (the "tenants" table carries no RLS policies at all, see backend-structure.md), only the
/// EF query shape itself.
/// </summary>
public sealed class TenantRepositoryGetBySlugTests
{
    private static AppDbContext MakeDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"task548-slug-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task GetBySlugAsync_matching_slug_returns_tenant()
    {
        await using var db = MakeDb();
        var tenant = Tenant.Create("Acme Retail", "acme-retail");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var repo = new TenantRepository(db);

        var result = await repo.GetBySlugAsync("acme-retail", default);

        Assert.NotNull(result);
        Assert.Equal(tenant.Id, result.Id);
    }

    [Fact]
    public async Task GetBySlugAsync_is_case_insensitive()
    {
        await using var db = MakeDb();
        var tenant = Tenant.Create("Acme Retail", "acme-retail");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var repo = new TenantRepository(db);

        var result = await repo.GetBySlugAsync("ACME-RETAIL", default);

        Assert.NotNull(result);
        Assert.Equal(tenant.Id, result.Id);
    }

    [Fact]
    public async Task GetBySlugAsync_unknown_slug_returns_null()
    {
        await using var db = MakeDb();
        var repo = new TenantRepository(db);

        var result = await repo.GetBySlugAsync("no-such-retailer", default);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBySlugAsync_returns_inactive_tenant_too_callers_decide_eligibility()
    {
        await using var db = MakeDb();
        var tenant = Tenant.Create("Closed Shop", "closed-shop");
        tenant.Deactivate();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        var repo = new TenantRepository(db);

        var result = await repo.GetBySlugAsync("closed-shop", default);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
    }
}
