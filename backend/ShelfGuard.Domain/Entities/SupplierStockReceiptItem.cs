namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D3).
/// A single <c>(SupplierItemId, ExpiryDate, BatchNumber)</c> line of a
/// <see cref="SupplierStockReceipt"/>. <b>N rows may share the same
/// <c>SupplierItemId</c></b> — one row per expiry/batch, unlike
/// <see cref="StockReceiptItem"/> (one expiry per line). <c>ExpiryDate</c> is nullable
/// while the receipt is a draft; it is required to finalize.
/// <c>TenantId</c> is denormalized so RLS stays a plain <c>tenant_isolation</c> with no join.
/// </summary>
public sealed class SupplierStockReceiptItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ReceiptId { get; init; }
    public Guid TenantId { get; init; }
    public Guid SupplierItemId { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public string? BatchNumber { get; set; }
    public decimal? UnitCost { get; set; }
    public string? Notes { get; set; }

    public SupplierStockReceipt? Receipt { get; init; }
    public SupplierItem? SupplierItem { get; set; }
}
