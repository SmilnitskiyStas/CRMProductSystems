namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Links a demand event to one specific store when Scope == "stores" (v2-spec §4) —
/// several rows per event, one per targeted store.
/// </summary>
public sealed class DemandEventStore
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid EventId { get; init; }
    public Guid StoreId { get; init; }

    public DemandEvent? Event { get; init; }
}
