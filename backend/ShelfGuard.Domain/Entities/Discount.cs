namespace ShelfGuard.Domain.Entities;

public sealed class Discount
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid? ProductStockId { get; init; }
    public Guid ProductId { get; init; }
    public Guid StoreId { get; init; }
    public decimal DiscountPercent { get; set; }
    public decimal? PriceOriginal { get; set; }
    public decimal? PriceDiscounted { get; set; }
    public string Reason { get; set; } = "expiry";
    public DateTime ValidFrom { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntil { get; set; }
    public string Status { get; set; } = "pending";
    public bool AutoApplied { get; set; }
    public Guid? CreatedBy { get; init; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? WebhookSentAt { get; set; }
}
