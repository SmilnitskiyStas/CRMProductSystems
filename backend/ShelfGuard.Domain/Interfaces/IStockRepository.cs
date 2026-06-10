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

    /// <summary>Returns stores of type 'production' or 'distribution' for the tenant.</summary>
    Task<List<Store>> GetProductionStoresAsync(CancellationToken ct = default);

    Task AddAsync(ProductStock stock, CancellationToken ct = default);
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);
    void Update(ProductStock stock);
    Task SaveChangesAsync(CancellationToken ct = default);
}
