namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decision D2).
/// Parallel to <see cref="ProductStock"/> (not a reuse): keyed on
/// <c>SupplierItemId</c> + <c>WarehouseId</c>, no <c>store_scope</c> RLS policy (supplier
/// tenants have no <c>user_locations</c> model). A warehouse is a <see cref="Location"/>
/// row of type "warehouse". FEFO consumption / status logic is duplicated from the
/// retail Stock feature, per D2.
/// </summary>
public sealed class SupplierStock
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; init; }
    public Guid SupplierItemId { get; init; }
    public Guid WarehouseId { get; init; }
    public DateOnly ExpiryDate { get; init; }
    public decimal Quantity { get; set; }
    public decimal QuantityInitial { get; init; }
    public string? BatchNumber { get; set; }
    public string Status { get; set; } = "safe";
    /// <summary>Provenance marker, e.g. "supplier_receipt", "manual".</summary>
    public string? SourceType { get; init; }
    public Guid? SourceId { get; init; }
    public Guid? AddedBy { get; init; }
    public DateTime AddedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastCheckedAt { get; set; } = DateTime.UtcNow;

    public SupplierItem? SupplierItem { get; init; }
    public Location? Warehouse { get; init; }
}
