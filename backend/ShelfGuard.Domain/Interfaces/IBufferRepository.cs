using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IBufferRepository
{
    Task<ProductBuffer?> GetAsync(Guid storeId, Guid productId, CancellationToken ct = default);

    /// <summary>Existing buffer rows for a store, keyed by ProductId (for upsert).</summary>
    Task<Dictionary<Guid, ProductBuffer>> GetByStoreAsync(Guid storeId, CancellationToken ct = default);

    /// <summary>ADU rows with a non-null effective value for the store (buffer inputs).</summary>
    Task<List<ProductAdu>> GetEffectiveAdusAsync(Guid storeId, CancellationToken ct = default);

    /// <summary>Map product id → default supplier id for the given products.</summary>
    Task<Dictionary<Guid, Guid>> GetProductSuppliersAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default);

    /// <summary>Active supply schedules for the store, keyed by SupplierId.</summary>
    Task<Dictionary<Guid, SupplySchedule>> GetActiveSchedulesAsync(
        Guid storeId, CancellationToken ct = default);

    /// <summary>Valid-day sold quantities per product over the window (variability input).</summary>
    Task<List<DailySale>> GetSalesWindowAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, DateOnly fromDate,
        CancellationToken ct = default);

    Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default);

    Task AddAsync(ProductBuffer buffer, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
