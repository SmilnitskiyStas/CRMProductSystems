namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Cached ADU (Average Daily Usage) per product+store. Recalculated before each order
/// cycle (v2-spec §1). adu_effective is the value the order formula uses.
/// </summary>
public sealed class ProductAdu
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public Guid ProductId { get; init; }
    public decimal? Adu30d { get; set; }
    public decimal? Adu60d { get; set; }
    public decimal? Adu90d { get; set; }
    public decimal? AduEffective { get; set; }
    /// <summary>1 = rare, 2 = medium, 3 = frequent (selects which window feeds adu_effective).</summary>
    public short? ProductGroup { get; set; }
    public int? ValidDays30d { get; set; }
    public int? ValidDays60d { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    public Item? Product { get; init; }
    public Location? Store { get; init; }
}
