namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Records the FEFO consumption of a specific stock batch when a production order is completed.
/// No TenantId — tenant scope inherited from parent ProductionOrder via JOIN.
/// Maps to "production_order_consumptions" table.
/// </summary>
public sealed class ProductionOrderConsumption
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProductionOrderId { get; init; }
    public Guid ItemId { get; set; }
    /// <summary>Specific stock batch consumed — FEFO requires tracking at batch level.</summary>
    public Guid ProductStockId { get; set; }
    public decimal QtyConsumed { get; set; }
    public DateTimeOffset ConsumedAt { get; init; } = DateTimeOffset.UtcNow;

    public ProductionOrder? ProductionOrder { get; init; }
    public Item? Item { get; init; }
    public ProductStock? ProductStock { get; init; }
}
