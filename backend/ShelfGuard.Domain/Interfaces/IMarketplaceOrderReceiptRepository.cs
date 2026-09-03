using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for client-confirmed marketplace order receipts (TASK-586, ADR-033). Separate
/// from <see cref="IMarketplaceOrderRepository"/> — receipts are a distinct entity pair with
/// their own RLS shape (client full read/write, supplier read-only, see ADR-033 Decision 3).
/// </summary>
public interface IMarketplaceOrderReceiptRepository
{
    /// <summary>
    /// The receipt for one order, including items (with Product/OrderItem) and DestinationStore.
    /// A unique index on MarketplaceOrderId guarantees at most one row.
    /// </summary>
    Task<MarketplaceOrderReceipt?> GetByOrderIdAsync(Guid marketplaceOrderId, CancellationToken ct = default);

    Task<MarketplaceOrderReceipt?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The supplier's shipped batch allocations for one order (Phase 3, plan D4), ordered
    /// nearest-expiry-first. Read from the CLIENT session through
    /// <c>marketplace_order_item_batches</c>' inverted <c>client_read</c> FOR SELECT policy —
    /// the client may read these rows and may never write them. Empty for legacy orders and for
    /// shipments made with the supplier's <c>supplier_inventory</c> module off, in which case
    /// draft creation falls back to one receipt item per order line.
    /// </summary>
    Task<IReadOnlyList<MarketplaceOrderItemBatch>> GetOrderItemBatchesAsync(
        Guid marketplaceOrderId, CancellationToken ct = default);

    Task AddAsync(MarketplaceOrderReceipt receipt, CancellationToken ct = default);

    void Update(MarketplaceOrderReceipt receipt);

    void UpdateItem(MarketplaceOrderReceiptItem item);

    /// <summary>Creates a ProductStock batch on finalize — same table ReceiptService writes to.</summary>
    Task AddStockAsync(ProductStock stock, CancellationToken ct = default);

    /// <summary>Creates a StockMovement audit row on finalize — same table ReceiptService writes to.</summary>
    Task AddMovementAsync(StockMovement movement, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
