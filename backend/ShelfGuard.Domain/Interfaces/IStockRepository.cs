using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IStockRepository
{
    Task<List<ProductStock>> GetAllAsync(
        Guid? storeId,
        string? status,
        Guid? zoneId,
        Guid? productId,
        CancellationToken ct = default);

    Task<(List<ProductStock> Items, int Total)> GetPagedAsync(
        Guid[]? storeIds,
        string? status,
        Guid? zoneId,
        Guid? productId,
        string? search,
        string? sortBy,
        bool? sortDescending,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<ProductStock?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<List<ProductStock>> GetExpiringAsync(Guid? storeId, int days, CancellationToken ct = default);

    Task<List<ProductStock>> GetExpiredAsync(Guid? storeId, CancellationToken ct = default);

    Task<List<ProductStock>> GetNeedsCheckAsync(Guid? storeId, CancellationToken ct = default);

    /// <summary>Returns active batches for a product+store ordered by expiry_date ASC (FEFO order).</summary>
    Task<List<ProductStock>> GetFefoOrderedAsync(Guid productId, Guid storeId, CancellationToken ct = default);

    /// <summary>Returns warning/critical batches for building suggestions.</summary>
    Task<List<ProductStock>> GetActionRequiredAsync(Guid? storeId, CancellationToken ct = default);

    /// <summary>Returns stock with deficit (quantity below min_stock) for a product across all stores.</summary>
    Task<List<ProductStock>> GetDeficitStocksAsync(Guid productId, Guid excludeStoreId, CancellationToken ct = default);

    /// <summary>
    /// Bulk variant: returns deficit stocks (quantity &lt; min_stock, quantity &gt; 0) for many
    /// products in one query, grouped by productId and ordered by expiry_date ascending within
    /// each group (across all stores — caller must filter out the source batch's own store).
    /// Every requested productId is present in the result (empty list when no deficit exists).
    /// </summary>
    Task<Dictionary<Guid, List<ProductStock>>> GetDeficitStocksBulkAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default);

    /// <summary>Returns locations of type 'production' or 'distribution' for the tenant.</summary>
    Task<List<Location>> GetProductionStoresAsync(CancellationToken ct = default);

    Task<Dictionary<string, int>> GetStatusCountsAsync(Guid[]? storeIds, CancellationToken ct = default);

    Task<List<(Guid? ZoneId, string ZoneName, string ZoneType, string Status)>> GetStockByZoneRawAsync(Guid[]? storeIds, CancellationToken ct = default);

    Task AddAsync(ProductStock stock, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    void Update(ProductStock stock);
    Task SaveChangesAsync(CancellationToken ct = default);
}
