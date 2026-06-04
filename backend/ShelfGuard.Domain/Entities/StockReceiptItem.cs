namespace ShelfGuard.Domain.Entities;

public sealed class StockReceiptItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ReceiptId { get; init; }
    public Guid ProductId { get; init; }
    public decimal QuantityOrdered { get; init; }
    public decimal? QuantityReceived { get; set; }
    public decimal? PricePurchase { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? BatchNumber { get; set; }
    public string? DiscrepancyNotes { get; set; }

    public StockReceipt? Receipt { get; init; }
}
