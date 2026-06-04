namespace ShelfGuard.Domain.Entities;

public sealed class Store
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string Type { get; set; } = "shop";
    public string? FloorPlan { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Tenant? Tenant { get; init; }
    public ICollection<StoreZone> Zones { get; init; } = new List<StoreZone>();
}
