namespace ShelfGuard.Domain.Entities;

/// <summary>
/// An image associated with a supplier item. An item can have multiple
/// images — one main image and any number of gallery images.
/// </summary>
public sealed class SupplierItemImage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierItemId { get; init; }
    public Guid TenantId { get; init; }
    public string Url { get; set; } = string.Empty;
    /// <summary>'main' | 'gallery'.</summary>
    public string Kind { get; set; } = "gallery";
    public int SortOrder { get; set; } = 0;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public SupplierItem? SupplierItem { get; init; }
    public Tenant? Tenant { get; init; }
}
