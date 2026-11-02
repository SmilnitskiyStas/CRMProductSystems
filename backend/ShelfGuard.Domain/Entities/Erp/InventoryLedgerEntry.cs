namespace ShelfGuard.Domain.Entities.Erp;

/// <summary>Signed physical batch delta. Corrections append a linked reversal; entries are never edited.</summary>
public sealed class InventoryLedgerEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid DocumentId { get; init; }
    public Guid PostingId { get; init; }
    public Guid DocumentLineId { get; init; }
    public int EntryNumber { get; init; }
    public Guid ItemId { get; init; }
    public Guid LocationId { get; init; }
    public Guid BatchId { get; init; }
    public Guid? ZoneId { get; init; }
    public decimal QuantityDelta { get; init; }
    public decimal? QuantityBefore { get; init; }
    public decimal? QuantityAfter { get; init; }
    public decimal? UnitCost { get; init; }
    public decimal? ValueDelta { get; init; }
    public string Currency { get; init; } = "UAH";
    public string? BatchNumberSnapshot { get; init; }
    public DateOnly? ExpiryDateSnapshot { get; init; }
    public Guid? ReversesEntryId { get; init; }
    public DateTime OccurredAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
