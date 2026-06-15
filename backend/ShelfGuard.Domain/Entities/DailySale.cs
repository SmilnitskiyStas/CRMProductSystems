namespace ShelfGuard.Domain.Entities;

/// <summary>
/// One day of sales for a product in a store. Source data for ADU calculation (v2).
/// Filled from POS data, CSV import, or manual entry.
/// </summary>
public sealed class DailySale
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public Guid ProductId { get; init; }
    public DateOnly Date { get; init; }
    public decimal QuantitySold { get; set; }
    public decimal? QuantityEndOfDay { get; set; }
    public bool IsPromoDay { get; set; }
    /// <summary>Excluded from ADU when true (spikes, data errors).</summary>
    public bool IsAnomaly { get; set; }
    /// <summary>manual / pos / import</summary>
    public string Source { get; set; } = "manual";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public CatalogProduct? Product { get; init; }
    public Location? Store { get; init; }
}
