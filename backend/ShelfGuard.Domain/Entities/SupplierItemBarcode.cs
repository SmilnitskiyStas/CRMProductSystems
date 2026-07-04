namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A barcode associated with a supplier item. An item can have multiple
/// barcodes — one primary and any number of alternates.
/// </summary>
public sealed class SupplierItemBarcode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierItemId { get; init; }
    public Guid TenantId { get; init; }
    public string Barcode { get; set; } = string.Empty;
    /// <summary>'primary' | 'alternate'.</summary>
    public string Kind { get; set; } = "primary";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public SupplierItem? SupplierItem { get; init; }
    public Tenant? Tenant { get; init; }
}
