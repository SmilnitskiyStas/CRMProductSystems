using ShelfGuard.Application.Features.Inventory;
using ShelfGuard.Application.Features.Inventory.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Xunit;

namespace ShelfGuard.Tests.Inventory;

public sealed class ProductServiceTests
{
    private readonly IProductRepository _repo = Substitute.For<IProductRepository>();
    private readonly ProductService _sut;

    public ProductServiceTests() => _sut = new ProductService(_repo);

    [Fact]
    public async Task CreateAsync_returns_product_when_sku_is_unique()
    {
        _repo.ExistsBySkuAsync("SKU-1", default).Returns(false);

        var (product, error) = await _sut.CreateAsync(ValidCreateRequest("SKU-1"));

        Assert.Null(error);
        Assert.NotNull(product);
        Assert.Equal("SKU-1", product.Sku);
        Assert.Equal("Test Product", product.Name);
        await _repo.Received(1).AddAsync(Arg.Any<Product>(), default);
        await _repo.Received(1).SaveChangesAsync(default);
    }

    [Fact]
    public async Task CreateAsync_returns_error_when_sku_already_exists()
    {
        _repo.ExistsBySkuAsync("SKU-1", default).Returns(true);

        var (product, error) = await _sut.CreateAsync(ValidCreateRequest("SKU-1"));

        Assert.Null(product);
        Assert.NotNull(error);
        Assert.Contains("SKU-1", error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<Product>(), default);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_product_does_not_exist()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), default).ReturnsNull();

        var result = await _sut.GetByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_returns_null_when_product_does_not_exist()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), default).ReturnsNull();

        var (product, _) = await _sut.UpdateAsync(
            Guid.NewGuid(),
            new UpdateProductRequest("Name", null, "Category", "kg", 1m, 2m, 5m, true));

        Assert.Null(product);
        _repo.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Fact]
    public async Task DeleteAsync_returns_false_when_product_does_not_exist()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), default).ReturnsNull();

        var deleted = await _sut.DeleteAsync(Guid.NewGuid());

        Assert.False(deleted);
        _repo.DidNotReceive().Remove(Arg.Any<Product>());
    }

    [Fact]
    public async Task DeleteAsync_removes_product_and_returns_true()
    {
        var product = Product.Create("SKU-1", "Test Product", null, "Produce", "kg", 1m, 2m, 5m);
        _repo.GetByIdAsync(product.Id, default).Returns(product);

        var deleted = await _sut.DeleteAsync(product.Id);

        Assert.True(deleted);
        _repo.Received(1).Remove(product);
        await _repo.Received(1).SaveChangesAsync(default);
    }

    private static CreateProductRequest ValidCreateRequest(string sku) =>
        new(sku, "Test Product", null, "Produce", "kg", 1.0m, 2.0m, 5.0m);
}
