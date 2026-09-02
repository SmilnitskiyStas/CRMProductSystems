using NSubstitute;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Catalog;

public sealed class ItemServiceTests
{
    private readonly IItemRepository _repo = Substitute.For<IItemRepository>();
    private readonly ICategoryRepository _categoryRepo = Substitute.For<ICategoryRepository>();
    private readonly ItemService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ItemServiceTests()
    {
        // Default: any category id the create/update path checks resolves to an active row.
        // The negative test overrides this for a specific id.
        _categoryRepo.ActiveExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _sut = new ItemService(_repo, _categoryRepo);
    }

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsProduct()
    {
        var req = new CreateProductRequest(
            "Молоко 2.5% 1л", null, null, null, "шт", "MTS", null,
            0, 0, 0, null, null, null, null, 20, null, 45.00m, null, null, null, null);

        var (product, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(error);
        Assert.NotNull(product);
        Assert.Equal("Молоко 2.5% 1л", product.Name);
        Assert.Equal("MTS", product.ManagementType);
        await _repo.Received(1).AddAsync(Arg.Is<Item>(p => p.TenantId == _tenantId), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_EmptyName_ReturnsError(string name)
    {
        var req = new CreateProductRequest(
            name, null, null, null, "шт", "MTS", null,
            0, 0, 0, null, null, null, null, 20, null, null, null, null, null, null);

        var (product, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(product);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("mts")]
    [InlineData("")]
    public async Task CreateAsync_InvalidManagementType_ReturnsError(string type)
    {
        var req = new CreateProductRequest(
            "Test", null, null, null, "шт", type, null,
            0, 0, 0, null, null, null, null, 20, null, null, null, null, null, null);

        var (product, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(product);
        Assert.Contains("management type", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("MTS")]
    [InlineData("MTO")]
    [InlineData("NA")]
    [InlineData("NM")]
    public async Task CreateAsync_AllValidManagementTypes_Succeed(string type)
    {
        var req = new CreateProductRequest(
            "Test Product", null, null, null, "шт", type, null,
            0, 0, 0, null, null, null, null, 20, null, null, null, null, null, null);

        var (product, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(error);
        Assert.NotNull(product);
        Assert.Equal(type, product.ManagementType);
    }

    // ── Update ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ProductNotFound_ReturnsError()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Item?)null);

        var req = new UpdateProductRequest(
            "Name", null, null, null, "шт", "MTS", null,
            0, 0, 0, null, null, null, null, 20, null, null, null, true, null, null, null);

        var (product, error) = await _sut.UpdateAsync(id, req);

        Assert.Null(product);
        Assert.Equal("Product not found.", error);
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesProduct()
    {
        var existing = new Item
        {
            TenantId = _tenantId,
            Name = "Old Name",
            ManagementType = "MTS",
            Unit = "шт",
        };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var req = new UpdateProductRequest(
            "New Name", ["1234567890"], null, null, "кг", "MTO", null,
            5, 100, 2, 0m, 8m, 14, null, 20, 30m, 50m, null, true, null, null, null);

        var (product, error) = await _sut.UpdateAsync(existing.Id, req);

        Assert.Null(error);
        Assert.NotNull(product);
        Assert.Equal("New Name", product.Name);
        Assert.Equal("MTO", product.ManagementType);
        Assert.Equal("кг", product.Unit);
        _repo.Received(1).Update(existing);
    }

    // ── CategoryId validation (B2) ─────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_UnknownOrInactiveCategoryId_ReturnsError()
    {
        var catId = Guid.NewGuid();
        _categoryRepo.ActiveExistsAsync(catId, Arg.Any<CancellationToken>()).Returns(false);

        var req = new CreateProductRequest(
            "Test", null, catId, null, "шт", "MTS", null,
            0, 0, 0, null, null, null, null, 20, null, null, null, null, null, null);

        var (product, error) = await _sut.CreateAsync(_tenantId, req);

        Assert.Null(product);
        Assert.Equal("Category not found or inactive.", error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_UnknownOrInactiveCategoryId_ReturnsError()
    {
        var existing = new Item { TenantId = _tenantId, Name = "Old", ManagementType = "MTS", Unit = "шт" };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        var catId = Guid.NewGuid();
        _categoryRepo.ActiveExistsAsync(catId, Arg.Any<CancellationToken>()).Returns(false);

        var req = new UpdateProductRequest(
            "New", null, catId, null, "шт", "MTS", null,
            0, 0, 0, null, null, null, null, 20, null, null, null, true, null, null, null);

        var (product, error) = await _sut.UpdateAsync(existing.Id, req);

        Assert.Null(product);
        Assert.Equal("Category not found or inactive.", error);
        _repo.DidNotReceive().Update(Arg.Any<Item>());
    }

    [Fact]
    public async Task UpdateAsync_UnchangedCategoryNowInactive_IsGrandfathered()
    {
        // Provider soft-deleted / retagged the category after the item was linked. An unrelated
        // edit must still save — the guard only fires for a *newly assigned* category (QA BUG-1).
        var catId = Guid.NewGuid();
        var existing = new Item { TenantId = _tenantId, Name = "Old", ManagementType = "MTS", Unit = "шт", CategoryId = catId };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);
        _categoryRepo.ActiveExistsAsync(catId, Arg.Any<CancellationToken>()).Returns(false);

        var req = new UpdateProductRequest(
            "New name", null, catId, null, "шт", "MTS", null,
            0, 0, 0, null, null, null, null, 20, null, null, null, true, null, null, null);

        var (product, error) = await _sut.UpdateAsync(existing.Id, req);

        Assert.Null(error);
        Assert.NotNull(product);
        _repo.Received().Update(Arg.Any<Item>());
    }

    // ── GetPaged (search/ids passthrough — TASK-572) ─────────────────────────

    [Fact]
    public async Task GetPagedAsync_NoSearchOrIds_PassesNullsThrough()
    {
        _repo.GetPagedAsync(null, null, null, null, null, null, null, 1, 50, null, null, null, Arg.Any<CancellationToken>())
            .Returns((new List<Item>(), 0));

        await _sut.GetPagedAsync(_tenantId, null, null, null, null, null, null, null, 1, 50);

        await _repo.Received(1).GetPagedAsync(null, null, null, null, null, null, null, 1, 50, null, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPagedAsync_WithSearch_PassesSearchThrough()
    {
        _repo.GetPagedAsync(null, null, null, "молок", null, null, null, 1, 50, null, null, null, Arg.Any<CancellationToken>())
            .Returns((new List<Item>(), 0));

        await _sut.GetPagedAsync(_tenantId, null, null, null, "молок", null, null, null, 1, 50);

        await _repo.Received(1).GetPagedAsync(null, null, null, "молок", null, null, null, 1, 50, null, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPagedAsync_WithIds_PassesIdsThroughAndReturnsExactSet()
    {
        var a = new Item { TenantId = _tenantId, Name = "A", ManagementType = "MTS" };
        var b = new Item { TenantId = _tenantId, Name = "Z", ManagementType = "MTS" };
        var ids = new List<Guid> { a.Id, b.Id };
        _repo.GetPagedAsync(null, null, null, null, ids, null, null, 1, 30, null, null, null, Arg.Any<CancellationToken>())
            .Returns((new List<Item> { a, b }, 2));

        var result = await _sut.GetPagedAsync(_tenantId, null, null, null, null, ids, null, null, 1, 30);

        Assert.Equal(2, result.Items.Count);
        await _repo.Received(1).GetPagedAsync(null, null, null, null, ids, null, null, 1, 30, null, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPagedAsync_WithSortByAndSortDescending_PassesBothThrough()
    {
        _repo.GetPagedAsync(null, null, null, null, null, "retailprice", true, 1, 50, null, null, null, Arg.Any<CancellationToken>())
            .Returns((new List<Item>(), 0));

        await _sut.GetPagedAsync(_tenantId, null, null, null, null, null, "retailprice", true, 1, 50);

        await _repo.Received(1).GetPagedAsync(null, null, null, null, null, "retailprice", true, 1, 50, null, null, null, Arg.Any<CancellationToken>());
    }

    // ── Delete ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_ProductNotFound_ReturnsFalse()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Item?)null);

        var result = await _sut.DeleteAsync(id);

        Assert.False(result);
    }

    [Fact]
    public async Task DeleteAsync_ExistingProduct_SetsInactiveSavesReturnTrue()
    {
        var existing = new Item { TenantId = _tenantId, Name = "Test", ManagementType = "MTS" };
        _repo.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _sut.DeleteAsync(existing.Id);

        Assert.True(result);
        Assert.False(existing.IsActive);
        _repo.Received(1).Update(existing);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── AddSupplier ────────────────────────────────────────────────────────

    [Fact]
    public async Task AddSupplierAsync_ProductNotFound_ReturnsError()
    {
        var productId = Guid.NewGuid();
        _repo.GetByIdAsync(productId, Arg.Any<CancellationToken>()).Returns((Item?)null);

        var req = new AddProductSupplierRequest(Guid.NewGuid(), 12, 6, null, 3, true);
        var (setting, error) = await _sut.AddSupplierAsync(productId, _tenantId, req);

        Assert.Null(setting);
        Assert.Equal("Product not found.", error);
    }

    [Fact]
    public async Task AddSupplierAsync_DuplicateSetting_ReturnsError()
    {
        var product = new Item { TenantId = _tenantId, Name = "Test", ManagementType = "MTS" };
        _repo.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _repo.SupplierSettingExistsAsync(product.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var req = new AddProductSupplierRequest(Guid.NewGuid(), 12, 6, null, 3, false);
        var (setting, error) = await _sut.AddSupplierAsync(product.Id, _tenantId, req);

        Assert.Null(setting);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task AddSupplierAsync_InvalidMoq_ReturnsError(decimal moq)
    {
        var product = new Item { TenantId = _tenantId, Name = "Test", ManagementType = "MTS" };
        _repo.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _repo.SupplierSettingExistsAsync(product.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var req = new AddProductSupplierRequest(Guid.NewGuid(), moq, 6, null, 3, false);
        var (setting, error) = await _sut.AddSupplierAsync(product.Id, _tenantId, req);

        Assert.Null(setting);
        Assert.Contains("MOQ", error);
    }

    [Fact]
    public async Task AddSupplierAsync_ValidRequest_AddsAndSaves()
    {
        var product = new Item { TenantId = _tenantId, Name = "Test", ManagementType = "MTS" };
        var supplierId = Guid.NewGuid();
        _repo.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _repo.SupplierSettingExistsAsync(product.Id, supplierId, Arg.Any<CancellationToken>()).Returns(false);
        _repo.GetSupplierSettingsAsync(product.Id, Arg.Any<CancellationToken>()).Returns([]);

        var req = new AddProductSupplierRequest(supplierId, 12, 6, 25.50m, 3, true);
        var (setting, error) = await _sut.AddSupplierAsync(product.Id, _tenantId, req);

        Assert.Null(error);
        await _repo.Received(1).AddSupplierSettingAsync(
            Arg.Is<ProductSupplierSetting>(s =>
                s.ProductId == product.Id &&
                s.SupplierId == supplierId &&
                s.Moq == 12 &&
                s.Usq == 6 &&
                s.IsPrimary == true),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
