namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Customer vehicle in the Auto Service module.
/// One customer can have multiple vehicles.
/// </summary>
public sealed class AsVehicle
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid CustomerId { get; init; }
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public short? Year { get; set; }
    public string? Vin { get; set; }
    public string? LicensePlate { get; set; }
    public int? Mileage { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
    public AsCustomer? Customer { get; init; }
    public ICollection<AsWorkOrder> WorkOrders { get; init; } = new List<AsWorkOrder>();
}
