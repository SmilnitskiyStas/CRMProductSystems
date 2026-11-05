namespace ShelfGuard.Domain.Entities.Erp;

public sealed class DocumentLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid DocumentId { get; init; }
    public int LineNumber { get; init; }
    public Guid ItemId { get; init; }
    public Guid? FromLocationId { get; init; }
    public Guid? ToLocationId { get; init; }
    public decimal Quantity { get; set; }
    public string Unit { get; init; } = "шт";
    public decimal? UnitPrice { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? UnitCost { get; set; }
    public string ItemNameSnapshot { get; set; } = string.Empty;
    public string? BarcodeSnapshot { get; set; }
    public string? BatchNumberSnapshot { get; set; }
    public DateOnly? ExpiryDateSnapshot { get; set; }
    public string? Metadata { get; set; }
}
