using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for store → production requests. Tenant isolation and store-scope are enforced by RLS.
/// Shares the scoped DbContext with <see cref="IProductionRepository"/>, so one SaveChanges commits both.
/// </summary>
public interface IProductionRequestRepository
{
    /// <summary>Loads the request with lines+items, both locations and the shipment transfer.</summary>
    Task<ProductionRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<List<ProductionRequest>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task<List<ProductionRequest>> ListAsync(
        Guid? storeLocationId, Guid? productionLocationId, IReadOnlyCollection<string>? statuses,
        DateOnly? neededFrom, DateOnly? neededTo, CancellationToken ct = default);

    Task AddAsync(ProductionRequest request, CancellationToken ct = default);
    Task AddLinesAsync(IEnumerable<ProductionRequestLine> lines, CancellationToken ct = default);
    void RemoveLines(IEnumerable<ProductionRequestLine> lines);

    Task<Location?> GetLocationAsync(Guid id, CancellationToken ct = default);

    /// <summary>Active recipe producing <paramref name="itemId"/> (null when the item is not made in-house).</summary>
    Task<Recipe?> GetActiveRecipeForOutputAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Subset of item ids that have an active recipe.</summary>
    Task<HashSet<Guid>> GetItemIdsWithActiveRecipeAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);

    /// <summary>Subset of request-line ids already allocated to a non-cancelled production order.</summary>
    Task<HashSet<Guid>> GetLineIdsInActiveOrdersAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken ct = default);

    Task AddOrderLinksAsync(IEnumerable<ProductionOrderRequestLine> links, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
