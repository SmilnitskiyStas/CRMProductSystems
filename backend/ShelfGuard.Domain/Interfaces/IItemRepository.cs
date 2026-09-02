using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IItemRepository
{
    // B2: `uncategorized` — true → only items with no category; overrides categoryId. When
    // categoryId is set (and uncategorized is not true) the filter expands to that category's
    // whole subtree, not an exact match. Appended at the very end (still before ct) so no
    // pre-existing parameter's positional index shifts for existing callers/fakes.
    Task<List<Item>> GetAllAsync(
        Guid? categoryId,
        Guid? segmentId,
        string? managementType,
        bool? uncategorized = null,
        CancellationToken ct = default);

    // TASK-640: minPrice/maxPrice — additive range filter on Item.PriceRetail for the frontend
    // table filter UI. B2: uncategorized — see GetAllAsync above. Both appended at the very end
    // (still before ct) so no pre-existing parameter's positional index shifts for existing
    // callers/fakes (WriteOffServiceTests reads `ids` off a fixed positional index).
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
        bool? uncategorized = null,
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
