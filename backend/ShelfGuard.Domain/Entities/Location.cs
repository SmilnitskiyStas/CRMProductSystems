namespace ShelfGuard.Domain.Entities;

public sealed class Location
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string Type { get; set; } = "shop";
    // v4: universal location type (retail_store / warehouse / auto_service / office / production / restaurant)
    public string LocationType { get; set; } = "retail_store";
    public string? FloorPlan { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Tenant? Tenant { get; init; }
    public ICollection<LocationZone> Zones { get; init; } = new List<LocationZone>();
}
