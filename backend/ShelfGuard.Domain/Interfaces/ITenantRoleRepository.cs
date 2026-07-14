using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for tenant-scoped custom role capability templates (ADR-020, TASK-345).
/// Every method is scoped by <c>tenantId</c> — standard tenant RLS also applies via
/// TenantConnectionInterceptor for the authenticated caller, the explicit filter is
/// defense in depth (mirrors <c>ISupplierRolesRepository</c>) and keeps unit tests
/// (InMemory provider, no RLS) meaningful. Mutating methods do not call SaveChanges —
/// the caller (service layer) controls the transaction boundary and calls
/// <see cref="SaveChangesAsync"/> once per unit of work (same convention as
/// <c>IUserPermissionGrantRepository</c>).
/// </summary>
public interface ITenantRoleRepository
{
    Task<TenantRole?> GetByIdAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);

    /// <summary>All templates for the tenant, active-only by default. Pass includeInactive=true to also return archived (IsActive=false) templates, e.g. for an admin audit view.</summary>
    Task<IReadOnlyList<TenantRole>> GetAllForTenantAsync(Guid tenantId, bool includeInactive = false, CancellationToken ct = default);

    /// <summary>
    /// Looks up an ACTIVE template by exact name, for the uniqueness pre-check before
    /// Create/Update. Only matches IsActive=true rows — this mirrors the partial unique
    /// index (uq_tenant_roles_tenant_name_active), which by design does not constrain
    /// archived rows, so an archived "HR" and a new active "HR" may legally coexist.
    /// </summary>
    Task<TenantRole?> GetByNameAsync(Guid tenantId, string name, CancellationToken ct = default);

    Task AddAsync(TenantRole role, CancellationToken ct = default);

    /// <summary>Marks an (already-mutated via TenantRole.Update) entity as modified. Safe to call even if the entity is already tracked.</summary>
    Task UpdateAsync(TenantRole role, CancellationToken ct = default);

    /// <summary>
    /// Soft-delete: sets IsActive = false via TenantRole.Deactivate() — never a hard
    /// delete, so historical references (activity log, past assignments) never dangle.
    /// Returns false if the template does not exist, belongs to a different tenant, or
    /// is already inactive.
    /// </summary>
    Task<bool> DeactivateAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
