namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D2).
/// Append-only ledger of every quantity change on a <see cref="SupplierStock"/> batch —
/// mirror of <see cref="StockMovement"/>. <c>MovementType</c> is one of
/// <c>receipt</c> / <c>ship</c> / <c>adjust</c> / <c>write_off</c>.
/// </summary>
public sealed class SupplierStockMovement
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string MovementType { get; init; } = string.Empty;
    public Guid SupplierStockId { get; init; }
    public Guid SupplierItemId { get; init; }
    public Guid? FromWarehouseId { get; init; }
    public Guid? ToWarehouseId { get; init; }
    public decimal Quantity { get; init; }
    public decimal QuantityBefore { get; init; }
    public decimal QuantityAfter { get; init; }
    public string? ReferenceType { get; init; }
    public Guid? ReferenceId { get; init; }
    public Guid? PerformedBy { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
