using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Read-only queries behind the production overview / plan / shipments / warehouse pages (TASK-763).
/// Tenant isolation and store scope are enforced by RLS; nothing here bypasses it.
/// </summary>
public interface IProductionInsightsRepository
{
    /// <summary>Active locations of Type "production"; narrowed to <paramref name="locationId"/> when given.</summary>
    Task<List<Location>> GetProductionLocationsAsync(Guid? locationId, CancellationToken ct = default);

    /// <summary>Requests of the given production locations in the given statuses (lines+items, store, transfer loaded).
    /// Fulfilled requests may be narrowed by <c>ShippedAt</c> via the optional window.</summary>
    Task<List<ProductionRequest>> GetRequestsAsync(
        IReadOnlyCollection<Guid> productionLocationIds, IReadOnlyCollection<string> statuses,
        DateTime? shippedFrom = null, DateTime? shippedToExclusive = null, CancellationToken ct = default);

    /// <summary>
    /// TASK-765 pipeline: requests of the given locations that are open (submitted/accepted/partial) OR whose
    /// SubmittedAt / ReviewedAt / ShippedAt falls in [fromUtc, toUtcExclusive). Lines+items, store, transfer loaded.
    /// </summary>
    Task<List<ProductionRequest>> GetPipelineRequestsAsync(
        IReadOnlyCollection<Guid> productionLocationIds, DateTime fromUtc, DateTime toUtcExclusive,
        CancellationToken ct = default);

    /// <summary>Order links of every line of the given requests, with ProductionOrder loaded.</summary>
    Task<List<ProductionOrderRequestLine>> GetLinksForRequestsAsync(
        IReadOnlyCollection<Guid> requestIds, CancellationToken ct = default);

    /// <summary>Orders at the given locations in the given statuses, with Recipe + Ingredients(+Item) + OutputItem loaded.</summary>
    Task<List<ProductionOrder>> GetOrdersAsync(
        IReadOnlyCollection<Guid> locationIds, IReadOnlyCollection<ProductionOrderStatus> statuses,
        CancellationToken ct = default);

    /// <summary>Active recipes with Ingredients(+Item) and OutputItem loaded, newest first.</summary>
    Task<List<Recipe>> GetActiveRecipesAsync(CancellationToken ct = default);

    /// <summary>Batches with Quantity &gt; 0 at the given locations (Product + Store loaded).</summary>
    Task<List<ProductStock>> GetStockAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct = default);

    /// <summary>Items with DefaultSupplier loaded.</summary>
    Task<List<Item>> GetItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);
}
