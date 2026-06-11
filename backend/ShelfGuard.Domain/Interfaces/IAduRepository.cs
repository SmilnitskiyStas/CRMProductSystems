using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IAduRepository
{
    /// <summary>
    /// Products eligible for ADU calculation in a store (v2-spec §1):
    /// MTS, active, default supplier has an active supply schedule for the store,
    /// and no approved discount active today.
    /// </summary>
    Task<List<Guid>> GetEligibleProductIdsAsync(Guid storeId, CancellationToken ct = default);

    /// <summary>Daily sales rows for the given products in a store since fromDate.</summary>
    Task<List<DailySale>> GetSalesWindowAsync(
        Guid storeId,
        IReadOnlyCollection<Guid> productIds,
        DateOnly fromDate,
        CancellationToken ct = default);

    Task<ProductAdu?> GetAsync(Guid storeId, Guid productId, CancellationToken ct = default);

    /// <summary>Existing ADU rows for a store, keyed by ProductId (for upsert).</summary>
    Task<Dictionary<Guid, ProductAdu>> GetByStoreAsync(Guid storeId, CancellationToken ct = default);

    Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default);

    Task AddAsync(ProductAdu adu, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
