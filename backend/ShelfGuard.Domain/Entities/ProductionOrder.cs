namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Production order — authorises production of a recipe at a specific location.
/// Maps to "production_orders" table.
/// </summary>
public sealed class ProductionOrder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid RecipeId { get; set; }
    public Guid LocationId { get; set; }
    public decimal PlannedQty { get; set; }
    public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Planned;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>User who created this order. Nullable — may be created by system/automation.</summary>
    public Guid? CreatedBy { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Tenant? Tenant { get; init; }
    public Recipe? Recipe { get; init; }
    public Location? Location { get; init; }
    public User? Creator { get; init; }
    public ICollection<ProductionOrderConsumption> Consumptions { get; init; } = new List<ProductionOrderConsumption>();
}
