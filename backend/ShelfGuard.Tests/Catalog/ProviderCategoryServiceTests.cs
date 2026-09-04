using NSubstitute;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Catalog;

/// <summary>
/// B2 — provider CRUD over the global <c>platform_categories</c> catalogue: business-type
/// allow-list validation, parent-cycle rejection, soft-delete guard, platform-wide ItemCount.
/// </summary>
public sealed class ProviderCategoryServiceTests
{
    private readonly ICategoryRepository _repo = Substitute.For<ICategoryRepository>();
    private readonly ProviderCategoryService _sut;

    public ProviderCategoryServiceTests()
    {
        _repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PlatformCategory>());
        _repo.CountItemsByCategoryAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>());
        _sut = new ProviderCategoryService(_repo);
    }

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Valid_AddsAndReturnsDto()
    {
        var req = new CreatePlatformCategoryRequest("  Напої  ", null, new[] { "retail", "RETAIL" }, 5);

        var (dto, error) = await _sut.CreateAsync(req);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal("Напої", dto!.Name);
        Assert.Equal(new[] { "retail" }, dto.BusinessTypes); // trimmed + lower-cased + deduped
        Assert.Equal(5, dto.SortOrder);
        Assert.True(dto.IsActive);
        Assert.Equal(0, dto.ItemCount);
        await _repo.Received(1).AddAsync(Arg.Is<PlatformCategory>(c => c.Name == "Напої"), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_BlankName_ReturnsError(string name)
    {
        var (dto, error) = await _sut.CreateAsync(new CreatePlatformCategoryRequest(name, null, [], null));

        Assert.Null(dto);
        Assert.Equal("Category name is required.", error);
    }

    [Fact]
    public async Task CreateAsync_UnknownBusinessType_ReturnsError()
    {
        var req = new CreatePlatformCategoryRequest("X", null, new[] { "retail", "spaceship" }, null);

        var (dto, error) = await _sut.CreateAsync(req);

        Assert.Null(dto);
        Assert.Contains("business type", error, StringComparison.OrdinalIgnoreCase);
        await _repo.DidNotReceive().AddAsync(Arg.Any<PlatformCategory>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_EmptyBusinessTypes_IsValid_MeansAllTypes()
    {
        var (dto, error) = await _sut.CreateAsync(new CreatePlatformCategoryRequest("Global cat", null, [], null));

        Assert.Null(error);
        Assert.Empty(dto!.BusinessTypes);
    }

    [Fact]
    public async Task CreateAsync_ParentDoesNotExist_ReturnsError()
    {
        var req = new CreatePlatformCategoryRequest("Child", Guid.NewGuid(), [], null);

        var (dto, error) = await _sut.CreateAsync(req);

        Assert.Null(dto);
        Assert.Contains("parent", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ValidParent_Succeeds()
    {
        var parent = new PlatformCategory { Name = "Parent" };
        _repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PlatformCategory> { parent });

        var (dto, error) = await _sut.CreateAsync(new CreatePlatformCategoryRequest("Child", parent.Id, [], null));

        Assert.Null(error);
        Assert.Equal(parent.Id, dto!.ParentId);
    }

    // ── Item-attribute defaults (Slice 2) ──────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithDefaults_PersistsAndReturnsThem()
    {
        var req = new CreatePlatformCategoryRequest(
            "Молочні", null, ["retail"], 0,
            new CategoryDefaults(VatRate: 20m, PerishabilityClass: "chilled", ManagementType: "mts", ItemType: "product", ShelfLifeDays: 7));

        var (dto, error) = await _sut.CreateAsync(req);

        Assert.Null(error);
        Assert.Equal(20m, dto!.Defaults.VatRate);
        Assert.Equal("chilled", dto.Defaults.PerishabilityClass);
        Assert.Equal("MTS", dto.Defaults.ManagementType); // upper-cased
        Assert.Equal("product", dto.Defaults.ItemType);
        Assert.Equal(7, dto.Defaults.ShelfLifeDays);
        await _repo.Received(1).AddAsync(
            Arg.Is<PlatformCategory>(c => c.DefaultPerishabilityClass == "chilled" && c.DefaultManagementType == "MTS"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_NoDefaults_ReturnsEmptyDefaults()
    {
        var (dto, error) = await _sut.CreateAsync(new CreatePlatformCategoryRequest("X", null, [], null));

        Assert.Null(error);
        Assert.Equal(CategoryDefaults.Empty, dto!.Defaults);
    }

    [Theory]
    [InlineData("spoiled", null, null, "perishability")]
    [InlineData(null, "instant", null, "management type")]
    [InlineData(null, null, "gadget", "item type")]
    public async Task CreateAsync_InvalidDefaultEnum_ReturnsError(string? cls, string? mgmt, string? itemType, string fragment)
    {
        var req = new CreatePlatformCategoryRequest(
            "X", null, [], null, new CategoryDefaults(null, cls, mgmt, itemType, null));

        var (dto, error) = await _sut.CreateAsync(req);

        Assert.Null(dto);
        Assert.Contains(fragment, error, StringComparison.OrdinalIgnoreCase);
        await _repo.DidNotReceive().AddAsync(Arg.Any<PlatformCategory>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task CreateAsync_DefaultVatOutOfRange_ReturnsError(double vat)
    {
        var req = new CreatePlatformCategoryRequest(
            "X", null, [], null, new CategoryDefaults((decimal)vat, null, null, null, null));

        var (dto, error) = await _sut.CreateAsync(req);

        Assert.Null(dto);
        Assert.Contains("VAT", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_ClearsDefaults_WhenRequestOmitsThem()
    {
        var existing = new PlatformCategory { Name = "Old", DefaultVatRate = 20m, DefaultPerishabilityClass = "chilled" };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var (dto, error) = await _sut.UpdateAsync(
            existing.Id, new UpdatePlatformCategoryRequest("Old", null, [], 0, true, Defaults: null));

        Assert.Null(error);
        Assert.Equal(CategoryDefaults.Empty, dto!.Defaults);
        Assert.Null(existing.DefaultVatRate);
        Assert.Null(existing.DefaultPerishabilityClass);
    }

    // ── Update ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsError()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlatformCategory?)null);

        var (dto, error) = await _sut.UpdateAsync(
            Guid.NewGuid(), new UpdatePlatformCategoryRequest("N", null, [], 0, true));

        Assert.Null(dto);
        Assert.Equal("Category not found.", error);
    }

    [Fact]
    public async Task UpdateAsync_Valid_MutatesAndReturnsDto()
    {
        var cat = new PlatformCategory { Name = "Old", BusinessTypes = ["retail"], SortOrder = 1, IsActive = true };
        _repo.GetByIdAsync(cat.Id, Arg.Any<CancellationToken>()).Returns(cat);
        _repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PlatformCategory> { cat });
        _repo.CountItemsByCategoryAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int> { [cat.Id] = 7 });

        var req = new UpdatePlatformCategoryRequest("New", null, new[] { "auto_service" }, 9, false);
        var (dto, error) = await _sut.UpdateAsync(cat.Id, req);

        Assert.Null(error);
        Assert.Equal("New", dto!.Name);
        Assert.Equal(new[] { "auto_service" }, dto.BusinessTypes);
        Assert.Equal(9, dto.SortOrder);
        Assert.False(dto.IsActive);
        Assert.Equal(7, dto.ItemCount);
        _repo.Received(1).Update(cat);
    }

    [Fact]
    public async Task UpdateAsync_ParentIsSelf_ReturnsCycleError()
    {
        var cat = new PlatformCategory { Name = "C" };
        _repo.GetByIdAsync(cat.Id, Arg.Any<CancellationToken>()).Returns(cat);
        _repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PlatformCategory> { cat });

        var (dto, error) = await _sut.UpdateAsync(
            cat.Id, new UpdatePlatformCategoryRequest("C", cat.Id, [], 0, true));

        Assert.Null(dto);
        Assert.Contains("cycle", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_ParentIsOwnDescendant_ReturnsCycleError()
    {
        // tree: root <- mid <- leaf. Re-parenting root under leaf would loop.
        var root = new PlatformCategory { Name = "root" };
        var mid = new PlatformCategory { Name = "mid", ParentId = root.Id };
        var leaf = new PlatformCategory { Name = "leaf", ParentId = mid.Id };
        _repo.GetByIdAsync(root.Id, Arg.Any<CancellationToken>()).Returns(root);
        _repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<PlatformCategory> { root, mid, leaf });

        var (dto, error) = await _sut.UpdateAsync(
            root.Id, new UpdatePlatformCategoryRequest("root", leaf.Id, [], 0, true));

        Assert.Null(dto);
        Assert.Contains("cycle", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Delete ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsError()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((PlatformCategory?)null);

        var error = await _sut.DeleteAsync(Guid.NewGuid());

        Assert.Equal("Category not found.", error);
    }

    [Fact]
    public async Task DeleteAsync_HasActiveChildren_ReturnsError()
    {
        var cat = new PlatformCategory { Name = "Parent" };
        _repo.GetByIdAsync(cat.Id, Arg.Any<CancellationToken>()).Returns(cat);
        _repo.HasActiveChildrenAsync(cat.Id, Arg.Any<CancellationToken>()).Returns(true);

        var error = await _sut.DeleteAsync(cat.Id);

        Assert.Equal("Category has active sub-categories.", error);
        _repo.DidNotReceive().Update(Arg.Any<PlatformCategory>());
    }

    [Fact]
    public async Task DeleteAsync_Leaf_SoftDeletes()
    {
        var cat = new PlatformCategory { Name = "Leaf", IsActive = true };
        _repo.GetByIdAsync(cat.Id, Arg.Any<CancellationToken>()).Returns(cat);
        _repo.HasActiveChildrenAsync(cat.Id, Arg.Any<CancellationToken>()).Returns(false);

        var error = await _sut.DeleteAsync(cat.Id);

        Assert.Null(error);
        Assert.False(cat.IsActive);
        _repo.Received(1).Update(cat);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── GetAll ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_MapsPlatformWideItemCount()
    {
        var a = new PlatformCategory { Name = "A", SortOrder = 1 };
        var b = new PlatformCategory { Name = "B", SortOrder = 2, IsActive = false };
        _repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<PlatformCategory> { a, b });
        _repo.CountItemsByCategoryAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int> { [a.Id] = 42 });

        var result = await _sut.GetAllAsync();

        Assert.Equal(2, result.Count); // incl. inactive
        Assert.Equal(42, result.Single(c => c.Id == a.Id).ItemCount);
        Assert.Equal(0, result.Single(c => c.Id == b.Id).ItemCount); // unmapped → 0
    }
}
