namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A task on the supplier's internal task board. Owned by the supplier tenant
/// (RLS-scoped via TenantId), optionally linked to a client tenant (the B2B
/// client the task concerns) and assigned to one of the supplier's staff members.
/// </summary>
public sealed class SupplierTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierId { get; init; }
    public Guid TenantId { get; init; }
    public Guid? ClientTenantId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime? DueDate { get; set; }
    public Guid? CreatedByUserId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public Supplier? Supplier { get; init; }
    public Tenant? Tenant { get; init; }
    public Tenant? ClientTenant { get; init; }
    public User? AssignedToUser { get; init; }
    public User? CreatedByUser { get; init; }
}
