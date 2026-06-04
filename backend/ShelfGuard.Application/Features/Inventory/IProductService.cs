using ShelfGuard.Application.Features.Inventory.Dtos;

namespace ShelfGuard.Application.Features.Inventory;

public interface IProductService
{
    Task<IEnumerable<ProductDto>> GetAllAsync(CancellationToken ct = default);
    Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(ProductDto? Product, string? Error)> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<(ProductDto? Product, string? Error)> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
