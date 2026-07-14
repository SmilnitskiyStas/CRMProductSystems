namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Custom, named capability-set template scoped to a single tenant (ADR-020, TASK-345).
/// Unlike <see cref="ProviderRole"/>/<see cref="SupplierRole"/> (which resolve a fixed
/// <c>BaseRole</c> + free-form <c>Permissions</c> for provider/supplier-cabinet staff),
/// a TenantRole grants additive "capabilities" (module.action keys — see
/// TenantRoleCapabilities in the Application layer) on top of a user's existing
/// <see cref="User.Role"/> hierarchy. Assigning one never changes rank — it only unlocks
/// specific per-action policies (e.g. "users.manage", "analytics.view") for users whose
/// job is fully described by the template (HR, accountant, purchasing), typically paired
/// with the minimal <c>AppRoles.Staff</c> base tier.
/// Templates are live and shared: editing one propagates to every assignee
/// (<see cref="User.TenantRoleId"/>), same "template not snapshot" semantics as
/// ProviderRole/SupplierRole. Archived (IsActive = false) instead of hard-deleted so
/// historical references (e.g. activity log entries) never dangle.
/// </summary>
public sealed class TenantRole
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public List<string> Capabilities { get; private set; } = [];
    public bool IsActive { get; private set; } = true;

    /// <summary>User who created this template. Nullable/SetNull — deleting the creator must not orphan the template.</summary>
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private TenantRole() { }

    public static TenantRole Create(
        Guid tenantId, string name, IEnumerable<string> capabilities, Guid? createdByUserId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        Capabilities = capabilities.ToList(),
        IsActive = true,
        CreatedByUserId = createdByUserId,
        CreatedAt = DateTime.UtcNow,
    };

    public void Update(string name, IEnumerable<string> capabilities)
    {
        Name = name;
        Capabilities = capabilities.ToList();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Soft-delete: archived templates keep existing assignees' rows valid (User.TenantRoleId is untouched by this) but should be excluded from "assign a role" pickers.</summary>
    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
