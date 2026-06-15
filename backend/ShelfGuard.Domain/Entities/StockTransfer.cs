namespace ShelfGuard.Domain.Entities;

public sealed class StockTransfer
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid FromStoreId { get; init; }
    public Guid ToStoreId { get; init; }
    public string? TransferType { get; set; }
    public string Status { get; set; } = "draft";
    public Guid? InitiatedBy { get; init; }
    public Guid? ConfirmedBy { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public ICollection<StockTransferItem> Items { get; init; } = new List<StockTransferItem>();

    // Navigation properties
    public Location? FromStore { get; set; }
    public Location? ToStore { get; set; }
}
