using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Promo signal for a catalog row (Slice 3). <c>State</c> is <c>"active"</c> (a promo discount
/// is running in at least one store right now) or <c>"upcoming"</c> (a promo starts within the
/// look-ahead window). Active wins over upcoming when a product has both across stores.
/// </summary>
public sealed record ItemPromoInfo(string State, DateTime? StartsAt, decimal? DiscountPercent);

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

    /// <summary>
    /// Slice 3: promo state for the given catalog-page product ids, aggregated across every store
    /// of the tenant. Considers only <c>promo</c>-reason / campaign-linked discounts in
    /// <c>active</c> status. "upcoming" = starts in the future but within
    /// <paramref name="upcomingWithinDays"/>. Products with no promo are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ItemPromoInfo>> GetPromoStatesAsync(
        IReadOnlyList<Guid> productIds, int upcomingWithinDays, CancellationToken ct = default);

    Task<List<ProductSupplierSetting>> GetSupplierSettingsAsync(Guid productId, CancellationToken ct = default);
    Task<bool> SupplierSettingExistsAsync(Guid productId, Guid supplierId, CancellationToken ct = default);

    Task AddAsync(Item product, CancellationToken ct = default);
    Task AddSupplierSettingAsync(ProductSupplierSetting setting, CancellationToken ct = default);
    void Update(Item product);
    Task SaveChangesAsync(CancellationToken ct = default);
}
