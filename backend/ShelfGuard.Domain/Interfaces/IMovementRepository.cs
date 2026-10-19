using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>One open order line for a product that has not reached stock yet.</summary>
public sealed record IncomingSupplyRow(
    string Source,
    Guid Id,
    string? Number,
    string Status,
    decimal Quantity,
    string? Unit,
    DateTime OrderedAt,
    DateOnly? ExpectedAt,
    DateTime? ShippedAt,
    string? SupplierName,
    Guid? DestinationStoreId,
    string? DestinationStoreName);

public interface IMovementRepository
{
    /// <summary>
    /// Open supply for one product (draft receipts + new/confirmed/shipped marketplace orders),
    /// optionally limited to one destination store. Same openness rules the order engine uses for
    /// its "in transit" figure, so the chart and the order calculation never disagree.
    /// </summary>
    Task<IReadOnlyList<IncomingSupplyRow>> GetIncomingAsync(
        Guid tenantId, Guid productId, Guid? storeId, CancellationToken ct = default);

    Task<IReadOnlyList<StockMovement>> GetAsync(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<int> CountAsync(
        Guid tenantId,
        Guid? productId,
        Guid? storeId,
        string? type,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default);

    /// <summary>Display names for the stores referenced by a page of movements (id -> name).</summary>
    Task<Dictionary<Guid, string>> GetStoreNamesAsync(
        IReadOnlyCollection<Guid> storeIds, CancellationToken ct = default);

    /// <summary>Full names of the users that performed a page of movements (id -> name).</summary>
    Task<Dictionary<Guid, string>> GetUserNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>Batch number + expiry of the stock rows a page of movements touched.</summary>
    Task<Dictionary<Guid, (string? BatchNumber, DateOnly ExpiryDate)>> GetBatchesAsync(
        IReadOnlyCollection<Guid> productStockIds, CancellationToken ct = default);
}
