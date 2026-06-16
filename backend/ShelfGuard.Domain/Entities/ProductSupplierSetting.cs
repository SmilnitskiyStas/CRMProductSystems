namespace ShelfGuard.Domain.Entities;

public sealed class ProductSupplierSetting
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid ProductId { get; init; }
    public Guid SupplierId { get; init; }
    public decimal Moq { get; set; } = 1;
    public decimal Usq { get; set; } = 1;
    public decimal? PricePurchase { get; set; }
    public int DeliveryDays { get; set; } = 3;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;

    public Item? Product { get; init; }
    public Supplier? Supplier { get; init; }
}
