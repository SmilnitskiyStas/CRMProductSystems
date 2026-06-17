namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A single line in a work order — either a service or a spare part.
/// Constraint: type='service' requires service_catalog_id; type='part' requires item_id.
/// </summary>
public sealed class AsWorkOrderLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid WorkOrderId { get; init; }
    /// <summary>'service' | 'part'</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Set when Type = 'service'.</summary>
    public Guid? ServiceCatalogId { get; set; }
    /// <summary>Set when Type = 'part' (item_type = spare_part).</summary>
    public Guid? ItemId { get; set; }
    public decimal Qty { get; set; } = 1m;
    public decimal Price { get; set; }
    public decimal Discount { get; set; } = 0m;

    public AsWorkOrder? WorkOrder { get; init; }
    public AsServiceCatalog? ServiceCatalog { get; init; }
    public Item? Item { get; init; }
}
