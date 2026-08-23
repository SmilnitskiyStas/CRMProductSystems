namespace ShelfGuard.Domain.Entities;

public sealed class WriteOff
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid StoreId { get; init; }
    public string Status { get; set; } = "draft";
    public string? Reason { get; set; }
    public decimal? TotalLossAmount { get; set; }
    public decimal? TotalLossAmountPurchase { get; set; }
    public decimal? TotalReimbursementAmount { get; set; }
    public string? PdfUrl { get; set; }
    public Guid? CreatedBy { get; init; }
    public Guid? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }

    public ICollection<WriteOffItem> Items { get; init; } = new List<WriteOffItem>();

    // Navigation properties
    public Location? Store { get; set; }
}
