using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IItemRepository
{
    Task<List<Item>> GetAllAsync(
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        CancellationToken ct = default);

    Task<(List<Item> Items, int Total)> GetPagedAsync(
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        string? search,
        IReadOnlyList<Guid>? ids,
        string? sortBy,
        bool? sortDescending,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<Item?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Item?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task<IReadOnlyList<Item>> GetByAnyBarcodeAsync(IReadOnlyList<string> barcodes, CancellationToken ct = default);

    Task<List<ProductSupplierSetting>> GetSupplierSettingsAsync(Guid productId, CancellationToken ct = default);
    Task<bool> SupplierSettingExistsAsync(Guid productId, Guid supplierId, CancellationToken ct = default);

    Task AddAsync(Item product, CancellationToken ct = default);
    Task AddSupplierSettingAsync(ProductSupplierSetting setting, CancellationToken ct = default);
    void Update(Item product);
    Task SaveChangesAsync(CancellationToken ct = default);
}
