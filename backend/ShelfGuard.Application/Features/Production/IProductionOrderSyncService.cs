using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Production;

/// <summary>
/// Keeps Planned production orders in step with accepted store requests (TASK-761). Idempotent:
/// compares each line's desired qty with the sum of its order links and corrects the difference.
/// </summary>
public interface IProductionOrderSyncService
{
    /// <summary>
    /// Brings the request's order links to its accepted quantities (0 for rejected/cancelled). Increases merge
    /// into / create Planned orders keyed by (item, production location, NeededBy) under an advisory lock;
    /// decreases only touch Planned orders. InProgress/Done orders are never modified. Runs in one
    /// transaction (joins an ambient one) and saves. Request must be loaded with its lines.
    /// </summary>
    Task SyncAsync(ProductionRequest request, Guid? userId, CancellationToken ct = default);

    /// <summary>Removes the order's request links (lines become unassigned again). Does NOT save — caller saves.</summary>
    Task UnlinkOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// After an order is completed: enqueues <c>production_request.ready</c> once for every linked request
    /// that has just become fully produced (stage ready).
    /// </summary>
    Task NotifyOrderCompletedAsync(Guid orderId, CancellationToken ct = default);
}
