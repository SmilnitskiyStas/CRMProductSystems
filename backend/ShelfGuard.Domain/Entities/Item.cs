namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Tenant-aware v1 product catalog. Maps to "items" table.
/// Distinct from the POC "Products" table (Product entity) used by the existing catalog API.
/// </summary>
public sealed class Item
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public List<string> Barcodes { get; set; } = [];
    public string? Manufacturer { get; set; }
    public string? CountryOrigin { get; set; }
    public string PerishabilityClass { get; set; } = "standard";
    public string Name { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public Guid? SegmentId { get; set; }
    public string Unit { get; set; } = "шт";
    public string ManagementType { get; set; } = "MTS";
    public decimal MinStock { get; set; }
    public decimal MaxStock { get; set; }
    public decimal SafetyBuffer { get; set; }
    public decimal? StorageTempMin { get; set; }
    public decimal? StorageTempMax { get; set; }
    public int? ShelfLifeDays { get; set; }
    public Guid? DefaultSupplierId { get; set; }
    /// <summary>
    /// Lineage pointer: the marketplace <see cref="SupplierItem"/> this Item was
    /// auto-provisioned from at order time (TASK-596). Nullable and SET NULL on delete —
    /// this Item must survive even if the source supplier listing is later removed.
    /// </summary>
    public Guid? SourceSupplierItemId { get; set; }
    public decimal VatRate { get; set; } = 20;
    public decimal? PricePurchase { get; set; }
    public decimal? PriceRetail { get; set; }
    public string? ImageUrl { get; set; }
    public string ItemType { get; set; } = "product";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public Tenant? Tenant { get; init; }
    public Category? Category { get; init; }
    public ProductSegment? Segment { get; init; }
    public Supplier? DefaultSupplier { get; init; }
    public SupplierItem? SourceSupplierItem { get; init; }
}
