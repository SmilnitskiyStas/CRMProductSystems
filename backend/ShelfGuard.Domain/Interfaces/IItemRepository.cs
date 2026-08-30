using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IItemRepository
{
    Task<List<Item>> GetAllAsync(
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        CancellationToken ct = default);

    // TASK-640: minPrice/maxPrice — additive range filter on Item.PriceRetail for the frontend
    // table filter UI. Appended at the very end (still before ct) so no pre-existing
    // parameter's positional index shifts for existing callers/fakes (WriteOffServiceTests
    // reads `ids` off a fixed positional index).
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
        decimal? minPrice = null,
        decimal? maxPrice = null,
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
