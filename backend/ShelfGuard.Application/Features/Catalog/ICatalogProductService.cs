using ShelfGuard.Application.Features.Catalog.Dtos;

namespace ShelfGuard.Application.Features.Catalog;

public interface ICatalogProductService
{
    Task<List<CatalogProductDto>> GetAllAsync(
        Guid tenantId,
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        CancellationToken ct = default);

    Task<CatalogProductDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<CatalogProductDto?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);

    Task<(CatalogProductDto? Product, string? Error)> CreateAsync(
        Guid tenantId,
        CreateProductRequest request,
        CancellationToken ct = default);

    Task<(CatalogProductDto? Product, string? Error)> UpdateAsync(
        Guid id,
        UpdateProductRequest request,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<List<ProductSupplierSettingDto>> GetSuppliersAsync(Guid productId, CancellationToken ct = default);

    Task<(ProductSupplierSettingDto? Setting, string? Error)> AddSupplierAsync(
        Guid productId,
        Guid tenantId,
        AddProductSupplierRequest request,
        CancellationToken ct = default);
}
