namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Extended profile for a supplier. 1-to-1 with Supplier.
/// is_public = true means the supplier is visible in the public marketplace listing.
/// plan = 'free' | 'premium' controls which fields are shown to unauthenticated visitors.
/// </summary>
public sealed class SupplierProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierId { get; init; }
    public Guid TenantId { get; init; }
    public string? Region { get; set; }
    /// <summary>JSONB array of product category tags.</summary>
    public string? Categories { get; set; }
    public string? Website { get; set; }
    /// <summary>JSONB array of delivery region codes/names.</summary>
    public string? DeliveryRegions { get; set; }
    public string? WorkingHours { get; set; }
    public string? PaymentTerms { get; set; }
    public bool IsPublic { get; set; } = false;
    public string Plan { get; set; } = "free";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Supplier? Supplier { get; init; }
    public Tenant? Tenant { get; init; }
}
