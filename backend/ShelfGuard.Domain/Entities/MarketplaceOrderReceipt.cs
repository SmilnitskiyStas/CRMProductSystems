namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Client-confirmed receipt of a <see cref="MarketplaceOrder"/> (TASK-586, ADR-033). Replaces
/// the supplier's one-click Shipped → Delivered transition: the client scans/counts what
/// physically arrived, and finalizing this receipt is the only remaining path that sets
/// <see cref="MarketplaceOrder.Status"/> to Delivered. 1:1 with its order (unique index on
/// <see cref="MarketplaceOrderId"/> — v1 scope limit, one receiving session per order).
/// SupplierTenantId/ClientTenantId/DestinationStoreId are denormalised copies taken from the
/// order at draft-creation time, same convention <see cref="MarketplaceOrderItem"/> already
/// established for this feature area — lets RLS filter without a join.
/// </summary>
public sealed class MarketplaceOrderReceipt
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid MarketplaceOrderId { get; set; }
    public Guid ClientTenantId { get; set; }
    public Guid SupplierTenantId { get; set; }
    public Guid DestinationStoreId { get; set; }
    /// <summary>"draft" → "received" only — no "cancelled" state (ADR-033).</summary>
    public string Status { get; set; } = "draft";
    public Guid? CreatedByUserId { get; set; }
    public Guid? ReceivedByUserId { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public MarketplaceOrder? Order { get; init; }
    public Location? DestinationStore { get; init; }
    public ICollection<MarketplaceOrderReceiptItem> Items { get; init; } = new List<MarketplaceOrderReceiptItem>();
}
