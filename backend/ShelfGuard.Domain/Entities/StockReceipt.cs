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
    public DateTime? ReceivedAt { get; set; }
    public Guid? CreatedBy { get; init; }
    public Guid? ReceivedBy { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public ICollection<StockReceiptItem> Items { get; init; } = new List<StockReceiptItem>();

    // Navigation properties
    public Supplier? Supplier { get; set; }
    public Store? DestinationStore { get; set; }
}
