namespace ShelfGuard.Domain.Entities.Erp;

/// <summary>Rebuildable batch projection, not the inventory source of truth.</summary>
public sealed class InventoryBalanceProjection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid ItemId { get; init; }
    public Guid LocationId { get; init; }
    public Guid BatchId { get; init; }
    public decimal Quantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public Guid? LastLedgerEntryId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
