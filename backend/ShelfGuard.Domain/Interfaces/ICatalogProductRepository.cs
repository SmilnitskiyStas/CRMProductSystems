using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ICatalogProductRepository
{
    Task<List<CatalogProduct>> GetAllAsync(
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        CancellationToken ct = default);

    Task<CatalogProduct?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CatalogProduct?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);

    Task<List<ProductSupplierSetting>> GetSupplierSettingsAsync(Guid productId, CancellationToken ct = default);
    Task<bool> SupplierSettingExistsAsync(Guid productId, Guid supplierId, CancellationToken ct = default);

    Task AddAsync(CatalogProduct product, CancellationToken ct = default);
    Task AddSupplierSettingAsync(ProductSupplierSetting setting, CancellationToken ct = default);
    void Update(CatalogProduct product);
    Task SaveChangesAsync(CancellationToken ct = default);
}
