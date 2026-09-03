using NSubstitute;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Catalog;

/// <summary>
/// B2 — <c>GET /api/categories</c> narrows the global <c>platform_categories</c> list to the
/// caller's <c>Tenant.BusinessType</c>. Empty <c>BusinessTypes</c> = "all types"; a null
/// tenant id (provider session) skips the filter entirely.
/// </summary>
public sealed class CategoryServiceTests
{
    private readonly ICategoryRepository _repo = Substitute.For<ICategoryRepository>();
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly CategoryService _sut;

    public CategoryServiceTests() => _sut = new CategoryService(_repo, _tenantRepo);

    private readonly PlatformCategory _retailOnly = new() { Name = "Молочні продукти", BusinessTypes = ["retail"] };
    private readonly PlatformCategory _autoOnly = new() { Name = "Гальмівні колодки", BusinessTypes = ["auto_service"] };
    private readonly PlatformCategory _allTypes = new() { Name = "Інше", BusinessTypes = [] };

    private void SeedCatalog() =>
        _repo.GetAllActiveAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PlatformCategory> { _retailOnly, _autoOnly, _allTypes });

    private void SeedTenant(Guid id, string businessType)
    {
        var tenant = Tenant.Create($"T-{id}", $"t-{id}");
        tenant.UpdateBusinessType(businessType);
        _tenantRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(tenant);
    }

    [Fact]
    public async Task GetAllAsync_AutoServiceTenant_HidesRetailOnlyCategory_ShowsAllTypesAndOwn()
    {
        SeedCatalog();
        var tenantId = Guid.NewGuid();
        SeedTenant(tenantId, "auto_service");

        var result = await _sut.GetAllAsync(tenantId);

        var names = result.Select(c => c.Name).ToList();
        Assert.Contains("Гальмівні колодки", names); // its own business type
        Assert.Contains("Інше", names);              // empty BusinessTypes → visible to all
        Assert.DoesNotContain("Молочні продукти", names); // retail-only → hidden
    }

    [Fact]
    public async Task GetAllAsync_RetailTenant_SeesRetailAndAllTypes_NotAutoService()
    {
        SeedCatalog();
        var tenantId = Guid.NewGuid();
        SeedTenant(tenantId, "retail");

        var result = await _sut.GetAllAsync(tenantId);

        var names = result.Select(c => c.Name).ToList();
        Assert.Contains("Молочні продукти", names);
        Assert.Contains("Інше", names);
        Assert.DoesNotContain("Гальмівні колодки", names);
    }

    [Fact]
    public async Task GetAllAsync_NullTenantId_ProviderSeesEverything()
    {
        SeedCatalog();

        var result = await _sut.GetAllAsync(tenantId: null);

        Assert.Equal(3, result.Count);
        await _tenantRepo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── SearchAsync (supplier-portal expansion #8, Phase 6e typeahead) ────────

    [Fact]
    public async Task SearchAsync_ClampsLimit_AndMapsRowsToDtos()
    {
        var tenantId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        _repo.SearchActiveAsync(tenantId, "мол", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<CategorySearchRow> { new(catId, "Молоко", "Продукти", 4) });

        var result = await _sut.SearchAsync(tenantId, "мол", limit: 999);

        var row = Assert.Single(result);
        Assert.Equal(catId, row.Id);
        Assert.Equal("Молоко", row.Name);
        Assert.Equal("Продукти", row.ParentName);
        Assert.Equal(4, row.ItemCount);
        await _repo.Received(1).SearchActiveAsync(tenantId, "мол", 50, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-5, 20)]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    [InlineData(51, 50)]
    public async Task SearchAsync_LimitClamping(int requested, int expected)
    {
        var tenantId = Guid.NewGuid();
        _repo.SearchActiveAsync(tenantId, Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<CategorySearchRow>());

        await _sut.SearchAsync(tenantId, "x", requested);

        await _repo.Received(1).SearchActiveAsync(tenantId, "x", expected, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_BusinessTypeMatchIsCaseInsensitive()
    {
        _repo.GetAllActiveAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PlatformCategory> { new() { Name = "X", BusinessTypes = ["Retail"] } });
        var tenantId = Guid.NewGuid();
        SeedTenant(tenantId, "retail");

        var result = await _sut.GetAllAsync(tenantId);

        Assert.Single(result);
    }
}
