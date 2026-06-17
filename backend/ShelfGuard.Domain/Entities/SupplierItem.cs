namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Supplier's product/service catalog entry.
/// Either item_id (linked to global catalog) or custom_name must be set.
/// </summary>
public sealed class SupplierItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierId { get; init; }
    public Guid TenantId { get; init; }
    /// <summary>Optional FK to items.Id. Null when supplier offers a custom product not in catalog.</summary>
    public Guid? ItemId { get; set; }
    /// <summary>Custom product name used when ItemId is null.</summary>
    public string? CustomName { get; set; }
    public decimal? Price { get; set; }
    public int? MinQty { get; set; }
    public string? Unit { get; set; }
    public bool IsAvailable { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public Supplier? Supplier { get; init; }
    public Tenant? Tenant { get; init; }
    public Item? Item { get; init; }
}
