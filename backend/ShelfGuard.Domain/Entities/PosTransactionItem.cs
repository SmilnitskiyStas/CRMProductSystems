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
    // Nullable for historical rows: never backfill old tax/cost from today's catalog.
    public decimal? VatRate { get; init; }
    public decimal? VatAmount { get; set; }
    public decimal? TaxableAmount { get; set; }
    public decimal? LineTotal { get; init; }
    public decimal? UnitCost { get; init; }
    public string? ProductNameSnapshot { get; init; }
    public string? BarcodeSnapshot { get; init; }
    public string? PriceSnapshotHash { get; init; }
    // Expiry read from the package by CV at checkout (v3 Phase 3); null = pure FEFO
    public DateOnly? ExpiryDate { get; init; }
    public int? CvConfidence { get; init; }
    // Promotion rule that priced this (sub-)line; null = regular / tier / stock discount only.
    // A line only partly covered by a promo is stored as several sub-lines of the same product
    // so PriceFinal * Quantity stays exact per row.
    public Guid? PromotionRuleId { get; init; }
    public string? PromotionLabel { get; init; }

    // Navigation properties
    public PosTransaction? Transaction { get; init; }
    public Item? Product { get; init; }
}
