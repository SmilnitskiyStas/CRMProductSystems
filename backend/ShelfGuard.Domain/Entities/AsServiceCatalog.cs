namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Catalog of services offered by the auto service shop.
/// Can optionally be linked to an Item with item_type = 'service'.
/// </summary>
public sealed class AsServiceCatalog
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Optional FK → items.Id where item_type = 'service'.</summary>
    public Guid? ItemId { get; set; }
    public decimal? DefaultPrice { get; set; }
    public decimal? DurationHours { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
    public Item? Item { get; init; }
}
