namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Line item of a <see cref="MarketplaceOrderReceipt"/> (TASK-586, ADR-033) — closes one
/// <see cref="MarketplaceOrderItem"/> line. Created empty from the order's snapshot line at
/// draft-creation time and filled in as the client scans; unlike
/// <see cref="StockReceiptItem.ProductId"/> (required), <see cref="ProductId"/> here is
/// nullable because it only resolves at barcode-scan time. SupplierTenantId/ClientTenantId are
/// denormalised copies of the parent receipt's pair, same rationale as the parent.
/// </summary>
public sealed class MarketplaceOrderReceiptItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ReceiptId { get; init; }
    public Guid MarketplaceOrderItemId { get; init; }
    public Guid ClientTenantId { get; set; }
    public Guid SupplierTenantId { get; set; }
    public Guid? ProductId { get; set; }
    /// <summary>Snapshot of <see cref="MarketplaceOrderItem.ItemName"/> at draft-creation time.</summary>
    public string ItemNameSnapshot { get; set; } = string.Empty;
    public decimal QuantityOrdered { get; init; }
    public decimal? QuantityReceived { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? BatchNumber { get; set; }
    public string? DiscrepancyNotes { get; set; }

    public MarketplaceOrderReceipt? Receipt { get; init; }
    public MarketplaceOrderItem? OrderItem { get; init; }
    public Item? Product { get; set; }
}
