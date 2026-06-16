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

    public WriteOff? WriteOff { get; init; }
    public Item? Product { get; set; }
    public ProductStock? ProductStock { get; set; }
}
