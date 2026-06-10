namespace ShelfGuard.Domain.Entities;

public sealed class StockTransferItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TransferId { get; init; }
    public Guid ProductStockId { get; init; }
    public Guid ProductId { get; init; }
    public decimal Quantity { get; init; }
    public DateOnly ExpiryDate { get; init; }  // copied from ProductStock, never changes
    public string? BatchNumber { get; init; }  // copied from ProductStock, never changes

    public StockTransfer? Transfer { get; init; }
    public CatalogProduct? Product { get; set; }
}
