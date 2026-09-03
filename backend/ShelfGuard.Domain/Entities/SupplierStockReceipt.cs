namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D3).
/// Parallel to <see cref="StockReceipt"/>: a supplier's manual "what actually arrived"
/// intake against one warehouse. On finalize each line becomes one
/// <see cref="SupplierStock"/> batch + one <see cref="SupplierStockMovement"/> (receipt).
/// <c>Status</c> is one of <c>draft</c> / <c>received</c> / <c>cancelled</c>.
/// v1 has no supplier-order document / ordered-vs-received reconciliation
/// (user decision 2026-09-02).
/// </summary>
public sealed class SupplierStockReceipt
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid WarehouseId { get; set; }
    public string Status { get; set; } = "draft";
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public Guid? CreatedBy { get; init; }
    public Guid? ReceivedBy { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public ICollection<SupplierStockReceiptItem> Items { get; init; } = new List<SupplierStockReceiptItem>();

    public Location? Warehouse { get; set; }
}
