namespace ShelfGuard.Domain.Entities.Erp;

public sealed class InventoryReservation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid DocumentLineId { get; init; }
    public Guid ItemId { get; init; }
    public Guid LocationId { get; init; }
    public Guid? BatchId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal RemainingQuantity { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? ExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
