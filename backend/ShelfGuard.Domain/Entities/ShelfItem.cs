namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Planogram row: catalog <see cref="Item"/> that should stand on a given shelf section
/// (<see cref="ShelfId"/> = the section Guid stored in <c>location_zones.Position</c> JSON) and
/// how many facings it should occupy. Reference data for shelf-photo AI checks — never written
/// into <c>product_stock</c>. Unique per (ZoneId, ShelfId, ItemId).
/// </summary>
public sealed class ShelfItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid LocationId { get; init; }
    public Guid ZoneId { get; init; }
    public Guid ShelfId { get; init; }
    public Guid ItemId { get; init; }
    public int ExpectedFacings { get; set; } = 1;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Item? Item { get; init; }
    public LocationZone? Zone { get; init; }
}
