namespace ShelfGuard.Domain.Entities;

public sealed class StockReceipt
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid? SupplierId { get; set; }
    public Guid DestinationStoreId { get; init; }
    public bool ViaCentralStore { get; set; }
    public string Status { get; set; } = "draft";
    public DateOnly? ExpectedAt { get; set; }

    // ── Postponement (a delayed delivery that the buyer moved to a new date) ──────────────────────────
    /// <summary>The first promised date, kept when the delivery is postponed for the first time.</summary>
    public DateOnly? OriginalExpectedAt { get; set; }
    /// <summary>How many times the expected date was moved. 0 = never postponed.</summary>
    public int PostponeCount { get; set; }
    public DateTime? PostponedAt { get; set; }
    public Guid? PostponedBy { get; set; }
    /// <summary>Why the delivery is late / was moved (latest postponement).</summary>
    public string? DelayReason { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public Guid? CreatedBy { get; init; }
    public Guid? ReceivedBy { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public ICollection<StockReceiptItem> Items { get; init; } = new List<StockReceiptItem>();

    // Navigation properties
    public Supplier? Supplier { get; set; }
    public Location? DestinationStore { get; set; }
}
