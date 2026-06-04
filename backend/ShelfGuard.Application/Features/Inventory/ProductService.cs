using ShelfGuard.Application.Features.Inventory.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Inventory;

public sealed class ProductService : IProductService
{
    private readonly IProductRepository _repository;

    public ProductService(IProductRepository repository) => _repository = repository;

    public async Task<IEnumerable<ProductDto>> GetAllAsync(CancellationToken ct = default)
    {
        var products = await _repository.GetAllAsync(ct);
        return products.Select(ToDto);
    }

    public async Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _repository.GetByIdAsync(id, ct);
        return product is null ? null : ToDto(product);
    }

    public async Task<(ProductDto? Product, string? Error)> CreateAsync(
        CreateProductRequest request, CancellationToken ct = default)
    {
        if (await _repository.ExistsBySkuAsync(request.Sku, ct))
            return (null, $"A product with SKU '{request.Sku}' already exists.");

        var product = Product.Create(
            request.Sku,
            request.Name,
            request.Description,
            request.Category,
            request.Unit,
            request.CostPrice,
            request.SalePrice,
            request.ReorderLevel);

        await _repository.AddAsync(product, ct);
        await _repository.SaveChangesAsync(ct);

        return (ToDto(product), null);
    }

    public async Task<(ProductDto? Product, string? Error)> UpdateAsync(
        Guid id, UpdateProductRequest request, CancellationToken ct = default)
    {
        var product = await _repository.GetByIdAsync(id, ct);
        if (product is null)
            return (null, null);

        product.Update(
            request.Name,
            request.Description,
            request.Category,
            request.Unit,
            request.CostPrice,
            request.SalePrice,
            request.ReorderLevel,
            request.IsActive);

        _repository.Update(product);
        await _repository.SaveChangesAsync(ct);

        return (ToDto(product), null);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _repository.GetByIdAsync(id, ct);
        if (product is null)
            return false;

        _repository.Remove(product);
        await _repository.SaveChangesAsync(ct);
        return true;
    }

    private static ProductDto ToDto(Product p) => new(
        p.Id, p.Sku, p.Name, p.Description, p.Category, p.Unit,
        p.CostPrice, p.SalePrice, p.StockQuantity, p.ReorderLevel,
        p.IsActive, p.CreatedAt, p.UpdatedAt);
}
