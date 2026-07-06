namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Line item of a <see cref="MarketplaceOrder"/> (TASK-316). ItemName, Unit
/// and Price are snapshots taken at order time — the referenced
/// <see cref="SupplierItem"/> may change or be deleted later (FK SET NULL).
/// SupplierTenantId/ClientTenantId are denormalised copies of the parent
/// order's pair so the RLS policy can filter rows without a join.
/// </summary>
public sealed class MarketplaceOrderItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid SupplierTenantId { get; set; }
    public Guid ClientTenantId { get; set; }
    /// <summary>Optional link to the supplier's catalog entry. SET NULL on delete.</summary>
    public Guid? SupplierItemId { get; set; }
    /// <summary>Snapshot of the item name at order time.</summary>
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal Price { get; set; }
    public decimal Qty { get; set; }
    public decimal LineTotal { get; set; }

    public MarketplaceOrder? Order { get; init; }
}
