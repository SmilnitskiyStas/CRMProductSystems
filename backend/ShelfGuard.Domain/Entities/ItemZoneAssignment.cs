namespace ShelfGuard.Domain.Entities;

/// <summary>
/// TASK-714: many-to-many tag linking a catalog <see cref="Item"/> to one or more
/// <see cref="LocationZone"/>s (store zones/counters). Backend groundwork for the floor-plan
/// canvas feature (TASK-715/716) — this is catalog metadata (which zones a product belongs to),
/// distinct from <c>ProductStock.ZoneId</c>/<c>ShelfNumber</c> which is a per-batch, per-store
/// physical placement fact. Flat join entity, no soft-delete — mirrors
/// <see cref="ProductSupplierSetting"/>.
/// </summary>
public sealed class ItemZoneAssignment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid ItemId { get; init; }
    public Guid ZoneId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Item? Item { get; init; }
    public LocationZone? Zone { get; init; }
}
