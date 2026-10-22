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

    /// <summary>
    /// Active recipe producing <paramref name="itemId"/> (null when the item is not made in-house).
    /// With several active recipes the most recently created one wins (deterministic).
    /// </summary>
    Task<Recipe?> GetActiveRecipeForOutputAsync(Guid itemId, CancellationToken ct = default);

    /// <summary>Subset of item ids that have an active recipe.</summary>
    Task<HashSet<Guid>> GetItemIdsWithActiveRecipeAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);

    /// <summary>Subset of request-line ids already allocated to a non-cancelled production order.</summary>
    Task<HashSet<Guid>> GetLineIdsInActiveOrdersAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken ct = default);

    Task AddOrderLinksAsync(IEnumerable<ProductionOrderRequestLine> links, CancellationToken ct = default);

    // ── Order sync (request → production order auto-planning) ──────────────

    /// <summary>
    /// Runs <paramref name="work"/> inside one DB transaction (joins an ambient one if already open).
    /// Commits when the delegate returns, rolls back when it throws.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);

    /// <summary>
    /// Takes transaction-scoped advisory locks (<c>pg_advisory_xact_lock</c>) for each (production location,
    /// item) key, in a stable order, so concurrent find-or-create of Planned orders serialise. Needs an open transaction.
    /// </summary>
    Task LockOrderKeysAsync(IEnumerable<(Guid LocationId, Guid ItemId)> keys, CancellationToken ct = default);

    /// <summary>Order links (with their tracked order) of the given request lines.</summary>
    Task<List<ProductionOrderRequestLine>> GetLinksForLinesAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken ct = default);

    /// <summary>Order links of every line of the given requests (order loaded; request line loaded).</summary>
    Task<List<ProductionOrderRequestLine>> GetLinksForRequestsAsync(IReadOnlyCollection<Guid> requestIds, CancellationToken ct = default);

    /// <summary>All links of one production order (request line + request loaded).</summary>
    Task<List<ProductionOrderRequestLine>> GetLinksForOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Explicit DbSet.Add (client-set Guid key must not be mis-tracked as Modified).</summary>
    Task AddOrderAsync(ProductionOrder order, CancellationToken ct = default);

    void RemoveOrderLinks(IEnumerable<ProductionOrderRequestLine> links);

    /// <summary>The Planned order producing <paramref name="itemId"/> at the location for the NeededBy date (oldest first).</summary>
    Task<ProductionOrder?> FindPlannedOrderAsync(
        Guid locationId, Guid itemId, DateOnly neededBy, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
