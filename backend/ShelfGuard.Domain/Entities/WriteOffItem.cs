namespace ShelfGuard.Domain.Entities;

public sealed class WriteOffItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid WriteOffId { get; init; }
    public Guid? ProductStockId { get; init; }
    public Guid ProductId { get; init; }
    public decimal Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? LossAmount { get; init; }
    public decimal? UnitPricePurchase { get; init; }   // snapshot of Item.PricePurchase at write-off time
    public decimal? LossAmountPurchase { get; init; }   // UnitPricePurchase * Quantity
    public bool IsReturnedToSupplier { get; init; }
    public string? ReimbursementType { get; init; }     // "fixed" | "percent"
    public decimal? ReimbursementValue { get; init; }
    public decimal? ReimbursementAmount { get; init; }  // computed reimbursement total

    public WriteOff? WriteOff { get; init; }
    public Item? Product { get; set; }
    public ProductStock? ProductStock { get; set; }
}
