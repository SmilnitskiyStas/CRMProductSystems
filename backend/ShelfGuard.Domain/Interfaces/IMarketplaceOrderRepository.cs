using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for B2B marketplace orders (TASK-316). Two-tenant RLS: both
/// the supplier and the client tenant see the same order rows.
/// </summary>
public interface IMarketplaceOrderRepository
{
    /// <summary>Loads an order including its line items.</summary>
    Task<MarketplaceOrder?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lists orders on the supplier side, newest first (items included).</summary>
    Task<IReadOnlyList<MarketplaceOrder>> ListForSupplierAsync(
        Guid supplierTenantId, CancellationToken ct = default);

    /// <summary>Lists orders on the client side, newest first (items included).</summary>
    Task<IReadOnlyList<MarketplaceOrder>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default);

    /// <summary>
    /// Shipped orders for a client tenant that don't yet have a MarketplaceOrderReceipt, or have
    /// one still in "draft" (TASK-586) — backs the receiving-flow "awaiting receipt" list.
    /// </summary>
    Task<IReadOnlyList<MarketplaceOrder>> ListAwaitingReceiptForClientAsync(
        Guid clientTenantId, CancellationToken ct = default);

    Task AddAsync(MarketplaceOrder order, CancellationToken ct = default);

    void Update(MarketplaceOrder order);

    /// <summary>
    /// Registers one supplier-allocated shipment batch (Phase 3, plan D4). Deliberately does NOT
    /// save — the whole shipment (stock decrements + movements + these rows + the order's status
    /// change) commits in a single <c>SaveChangesAsync</c> so a mid-way failure can never leave
    /// stock consumed for an order that never shipped.
    ///
    /// Must be called on the SUPPLIER session: <c>marketplace_order_item_batches</c>'
    /// <c>tenant_isolation</c> WITH CHECK is keyed on SupplierTenantId (the inverse of every
    /// other marketplace table — see the migration).
    /// </summary>
    Task AddOrderItemBatchAsync(MarketplaceOrderItemBatch batch, CancellationToken ct = default);

    /// <summary>Total number of orders ever placed with a supplier — used to generate the next OrderNumber.</summary>
    Task<int> CountForSupplierAsync(Guid supplierTenantId, CancellationToken ct = default);

    /// <summary>
    /// "New order arrived" badge count (supplier-portal expansion #3, Phase 6a): non-cancelled
    /// orders of <paramref name="supplierTenantId"/>. When <paramref name="since"/> is non-null
    /// only orders with <c>CreatedAt &gt; since</c> are counted (the calling user's
    /// <c>SupplierOrdersLastViewedAt</c> marker); null <paramref name="since"/> — the user never
    /// opened the tab — counts every non-cancelled order. Runs on the supplier's own session.
    /// </summary>
    Task<int> CountUnseenForSupplierAsync(
        Guid supplierTenantId, DateTimeOffset? since, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
