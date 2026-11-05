using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>Read-side queries for automatic demand analysis of own-production items. Isolation via RLS.</summary>
public interface IProductionDemandRepository
{
    /// <summary>Active production locations, most recently used (latest production order) first, then by name.</summary>
    Task<List<Location>> GetActiveProductionLocationsAsync(CancellationToken ct = default);

    /// <summary>Active non-production locations (the requesting stores), by name.</summary>
    Task<List<Location>> GetActiveStoresAsync(Guid? onlyLocationId, CancellationToken ct = default);

    /// <summary>Output items of all active recipes (= self-produced items).</summary>
    Task<HashSet<Guid>> GetSelfProducedItemIdsAsync(CancellationToken ct = default);

    /// <summary>On-hand quantity per item excluding batches expired before <paramref name="today"/>.</summary>
    Task<Dictionary<Guid, decimal>> GetNonExpiredStockAsync(
        Guid storeId, IReadOnlyCollection<Guid> itemIds, DateOnly today, CancellationToken ct = default);

    /// <summary>Quantity per item on stock transfers to the store that are still in transit.</summary>
    Task<Dictionary<Guid, decimal>> GetTransferInTransitAsync(
        Guid storeId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);
}
