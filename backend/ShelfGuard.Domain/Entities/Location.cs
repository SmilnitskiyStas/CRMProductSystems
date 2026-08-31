namespace ShelfGuard.Domain.Entities;

public sealed class Location
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    /// <summary>
    /// Structured Ukraine region code this location sits in (TASK-649), e.g. "UA-32" (oblast)
    /// or "UA-18-ZHYTOMYR" (city). Nullable — existing rows stay NULL, set manually via the
    /// location form (address-based backfill is unreliable). Snapshotted onto
    /// <see cref="MarketplaceOrder.DestinationRegionCode"/> at order creation and feeds the
    /// supplier delivery-time-by-region metrics.
    /// </summary>
    public string? RegionCode { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string Type { get; set; } = "shop";
    // v4: universal location type (retail_store / warehouse / auto_service / office / production / restaurant)
    public string LocationType { get; set; } = "retail_store";
    public string? FloorPlan { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>Optional legal entity this location is registered under (TASK-321).</summary>
    public Guid? LegalEntityId { get; set; }

    public Tenant? Tenant { get; init; }
    public ICollection<LocationZone> Zones { get; init; } = new List<LocationZone>();
}
