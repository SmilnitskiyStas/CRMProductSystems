namespace ShelfGuard.Domain.Entities;

public sealed class ProductStock
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid ProductId { get; init; }
    public Guid StoreId { get; init; }
    public Guid? ZoneId { get; set; }
    public int? ShelfNumber { get; set; }
    public string? BatchNumber { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuantityInitial { get; init; }
    public DateOnly ExpiryDate { get; init; }
    public string Status { get; set; } = "safe";
    public string? SourceType { get; init; }
    public Guid? SourceId { get; init; }
    public Guid? AddedBy { get; init; }
    public DateTime AddedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;
    public DateTime? NotifiedWarningAt { get; set; }
    public DateTime? NotifiedCriticalAt { get; set; }

    public Item? Product { get; init; }
    public Location? Store { get; init; }
    public LocationZone? Zone { get; init; }
}
