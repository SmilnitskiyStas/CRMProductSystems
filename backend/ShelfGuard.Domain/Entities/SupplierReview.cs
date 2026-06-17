namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Tenant review of a supplier.
/// Unique constraint: one review per (supplier_id, tenant_id).
/// RLS isolates each tenant to their own reviews.
/// </summary>
public sealed class SupplierReview
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierId { get; init; }
    public Guid TenantId { get; init; }
    /// <summary>Rating 1–5.</summary>
    public short Rating { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public Supplier? Supplier { get; init; }
    public Tenant? Tenant { get; init; }
}
