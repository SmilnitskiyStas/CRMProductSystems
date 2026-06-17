namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Auto Service customer. One customer can own multiple vehicles.
/// </summary>
public sealed class AsCustomer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
    public ICollection<AsVehicle> Vehicles { get; init; } = new List<AsVehicle>();
}
