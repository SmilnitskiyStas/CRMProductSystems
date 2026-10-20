using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IDailySalesRepository
{
    /// <summary>
    /// TASK-610: storeIds is a repeated query param (Guid[]?) — null/empty means "all stores"
    /// (Contains-filter), same convention as EventRepository.GetAsync / TASK-608's Analytics
    /// widening.
    /// </summary>
    /// <summary>
    /// Returns one page (1-based <paramref name="page"/>) plus the total number of rows matching
    /// the filters. The page is cut in SQL (COUNT + OFFSET/LIMIT) — never materialise the whole
    /// range. <paramref name="sortBy"/> is one of date | product | store | sold | eod | source
    /// (anything else falls back to date); ties always break on date desc, product name, id.
    /// </summary>
    Task<(List<DailySale> Items, int TotalCount)> GetPagedAsync(
        Guid[]? storeIds,
        Guid? productId,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        string? sortBy = null,
        bool sortDescending = true,
        CancellationToken ct = default);

    Task<DailySale?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<DailySale?> FindAsync(Guid storeId, Guid productId, DateOnly date, CancellationToken ct = default);

    /// <summary>Maps barcode → product id for CSV import row resolution.</summary>
    Task<Dictionary<string, Guid>> GetProductIdsByBarcodesAsync(
        IReadOnlyCollection<string> barcodes,
        CancellationToken ct = default);

    Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default);
    Task<bool> ProductExistsAsync(Guid productId, CancellationToken ct = default);

    Task AddAsync(DailySale sale, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
