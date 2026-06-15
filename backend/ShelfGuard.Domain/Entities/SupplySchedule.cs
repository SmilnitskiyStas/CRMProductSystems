namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Delivery schedule for a supplier to a store (v2-spec §8). Drives dynamic
/// lead_time / order_cycle in the CDA buffer.
/// </summary>
public sealed class SupplySchedule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public Guid SupplierId { get; init; }
    /// <summary>ISO weekdays with deliveries: [1,3,5] = Mon, Wed, Fri.</summary>
    public List<int> DayOfWeek { get; set; } = [];
    /// <summary>How many days before delivery the order must be placed.</summary>
    public int? OrderLeadDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Location? Store { get; init; }
    public Supplier? Supplier { get; init; }
}
