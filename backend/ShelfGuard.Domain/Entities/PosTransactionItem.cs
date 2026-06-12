namespace ShelfGuard.Domain.Entities;

public sealed class PosTransactionItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TransactionId { get; init; }
    public Guid ProductId { get; init; }
    // FEFO batch the sale consumed from (SET NULL if the batch is later removed)
    public Guid? ProductStockId { get; set; }
    public decimal Quantity { get; init; }
    public decimal PriceRetail { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal PriceFinal { get; init; }
    // Expiry read from the package by CV at checkout (v3 Phase 3); null = pure FEFO
    public DateOnly? ExpiryDate { get; init; }
    public int? CvConfidence { get; init; }

    // Navigation properties
    public PosTransaction? Transaction { get; init; }
    public CatalogProduct? Product { get; init; }
}
