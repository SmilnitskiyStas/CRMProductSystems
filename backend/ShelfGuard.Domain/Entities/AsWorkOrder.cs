namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Work order (наряд-замовлення) in the Auto Service module.
/// Groups services and parts applied to a specific vehicle.
/// </summary>
public sealed class AsWorkOrder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid VehicleId { get; init; }
    /// <summary>Assigned mechanic. Nullable — may be unassigned.</summary>
    public Guid? MechanicUserId { get; set; }
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.New;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public Tenant? Tenant { get; init; }
    public AsVehicle? Vehicle { get; init; }
    public User? Mechanic { get; init; }
    public ICollection<AsWorkOrderLine> Lines { get; init; } = new List<AsWorkOrderLine>();
}
