using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Client-confirmed receipt of a shipped <see cref="MarketplaceOrder"/> (TASK-586, ADR-033) —
/// scan/count/expiry, mirroring <see cref="IReceiptService"/>'s create-draft / update-item /
/// finalize shape. The sole code path that sets MarketplaceOrder.Status = Delivered (ADR-033
/// Decision 4). Every method is order-centric (orderId, never a separately surfaced receiptId)
/// per ADR-033 Decision 5's routing design — callers never need to learn or persist a second id.
/// </summary>
public interface IMarketplaceOrderReceiptService
{
    /// <summary>Shipped orders of the client tenant that still need to be received (endpoint a).</summary>
    Task<IReadOnlyList<MarketplaceOrderDto>> ListAwaitingReceiptAsync(
        Guid clientTenantId, CancellationToken ct = default);

    /// <summary>
    /// Idempotent create-or-get (endpoint b): returns the existing receipt if one was already
    /// started for this order, otherwise creates a new "draft" pre-populated with one item per
    /// order line. Validates the order belongs to the caller, is status = shipped, and has a
    /// DestinationStoreId (ADR-033's historical-gap case).
    /// </summary>
    Task<(MarketplaceOrderReceiptDto? Receipt, string? Error)> GetOrCreateDraftAsync(
        Guid clientTenantId, Guid orderId, Guid userId, CancellationToken ct = default);

    /// <summary>Read-only fetch (endpoint c). 404 (via null + error) if no receipt exists yet.</summary>
    Task<(MarketplaceOrderReceiptDto? Receipt, string? Error)> GetAsync(
        Guid clientTenantId, Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Per-item scan/count update (endpoint d) — one physical item per call. Rejects once the
    /// receipt is no longer "draft".
    /// </summary>
    Task<(MarketplaceOrderReceiptDto? Receipt, string? Error)> UpdateItemAsync(
        Guid clientTenantId, Guid orderId, Guid itemId, UpdateMarketplaceOrderReceiptItemRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Finalize (endpoint e): gates on every item having ProductId + QuantityReceived +
    /// ExpiryDate, then creates ProductStock/StockMovement per item, marks the receipt
    /// "received", and sets the order to Delivered.
    /// </summary>
    Task<(MarketplaceOrderReceiptDto? Receipt, string? Error)> ReceiveAsync(
        Guid clientTenantId, Guid orderId, Guid receivedByUserId, CancellationToken ct = default);
}
