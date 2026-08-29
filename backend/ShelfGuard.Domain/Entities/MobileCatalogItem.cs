namespace ShelfGuard.Domain.Entities;

public sealed class MobileCatalogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SettingsId { get; set; }
    public Guid ProductId { get; set; }
    public int SortOrder { get; set; }
    public bool IsFeatured { get; set; }
    public decimal? MobileDiscountPercent { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string UnitSnapshot { get; set; } = string.Empty;
    public string? ImageUrlSnapshot { get; set; }
    public decimal? RegularPriceSnapshot { get; set; }
    public decimal? MobilePriceSnapshot { get; set; }
    public MobileCatalogSettings? Settings { get; set; }
    public Item? Product { get; set; }
}
