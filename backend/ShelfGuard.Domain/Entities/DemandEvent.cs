namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Demand-affecting event (v2-spec §4): holiday, promo, local_event, season_start, custom.
/// Coefficients multiply the base order quantity for matching products.
/// </summary>
public sealed class DemandEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    /// <summary>holiday / promo / local_event / season_start / custom</summary>
    public string EventType { get; set; } = "custom";
    /// <summary>network — applies to all stores; store — only StoreId; stores — several
    /// specific stores via the Stores collection.</summary>
    public string Scope { get; set; } = "network";
    public Guid? StoreId { get; set; }
    public DateOnly StartsAt { get; set; }
    public DateOnly EndsAt { get; set; }
    /// <summary>Annual events (holidays) match by month/day each year.</summary>
    public bool IsRecurring { get; set; }
    public string? RecurrenceRule { get; set; }
    public string? Notes { get; set; }
    public Guid? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Location? Store { get; init; }
    public ICollection<DemandEventCoefficient> Coefficients { get; init; } = new List<DemandEventCoefficient>();
    public ICollection<DemandEventStore> Stores { get; init; } = new List<DemandEventStore>();

    /// <summary>
    /// True when the event affects the given date. Recurring events compare
    /// month/day cyclically (handles year-wrapping windows like 25 Dec – 2 Jan).
    /// </summary>
    public bool IsActiveOn(DateOnly date)
    {
        if (!IsRecurring)
            return date >= StartsAt && date <= EndsAt;

        var d = (date.Month, date.Day);
        var s = (StartsAt.Month, StartsAt.Day);
        var e = (EndsAt.Month, EndsAt.Day);

        return s.CompareTo(e) <= 0
            ? d.CompareTo(s) >= 0 && d.CompareTo(e) <= 0
            : d.CompareTo(s) >= 0 || d.CompareTo(e) <= 0; // wraps over new year
    }
}
