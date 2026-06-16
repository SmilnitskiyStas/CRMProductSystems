namespace ShelfGuard.Domain.Entities;

/// <summary>
/// CDA buffer per product+store (v2-spec §2). Recalculated on order day only —
/// frozen between orders. Total = Green + Yellow + Red.
/// </summary>
public sealed class ProductBuffer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public Guid ProductId { get; init; }
    public decimal BufferTotal { get; set; }
    /// <summary>Demand over the full cycle: ADU × (LT + OC).</summary>
    public decimal BufferGreen { get; set; }
    /// <summary>Demand variability reserve: ADU × OC × variability.</summary>
    public decimal BufferYellow { get; set; }
    /// <summary>Safety stock: ADU × LT × safety_factor.</summary>
    public decimal BufferRed { get; set; }
    /// <summary>Days from placing an order to goods on shelf (from supply schedule).</summary>
    public decimal LeadTimeDays { get; set; }
    /// <summary>Average days between deliveries (7 / deliveries per week).</summary>
    public decimal OrderCycleDays { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    public Item? Product { get; init; }
    public Location? Store { get; init; }
}
