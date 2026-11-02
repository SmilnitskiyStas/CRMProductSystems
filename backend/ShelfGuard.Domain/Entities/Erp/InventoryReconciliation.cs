namespace ShelfGuard.Domain.Entities.Erp;

/// <summary>Immutable per-batch observation grouped by RunId; unknown history stays null.</summary>
public sealed class InventoryReconciliation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid RunId { get; init; }
    public Guid ItemId { get; init; }
    public Guid LocationId { get; init; }
    public Guid BatchId { get; init; }
    public decimal? LegacyQuantity { get; init; }
    public decimal? LedgerQuantity { get; init; }
    public decimal? Difference { get; private set; }
    public string Status { get; init; } = "unverifiable";
    public string? Explanation { get; init; }
    public DateTime AsOf { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
