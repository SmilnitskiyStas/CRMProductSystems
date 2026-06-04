namespace ShelfGuard.Domain.Entities;

public sealed class StockMovement
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string MovementType { get; init; } = string.Empty;
    public Guid? ProductStockId { get; init; }
    public Guid ProductId { get; init; }
    public Guid? FromStoreId { get; init; }
    public Guid? ToStoreId { get; init; }
    public Guid? FromZoneId { get; init; }
    public Guid? ToZoneId { get; init; }
    public decimal Quantity { get; init; }
    public decimal? QuantityBefore { get; init; }
    public decimal? QuantityAfter { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? TotalAmount { get; init; }
    public Guid? ReferenceId { get; init; }
    public string? ReferenceType { get; init; }
    public Guid? PerformedBy { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
