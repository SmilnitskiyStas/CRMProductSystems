using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for tenant-scoped supplier staff roles (TASK-306). Every method is
/// scoped by <c>tenantId</c> — standard tenant RLS also applies via the
/// TenantConnectionInterceptor for the authenticated supplier-cabinet caller.
/// </summary>
public interface ISupplierRolesRepository
{
    Task<IReadOnlyList<SupplierRole>> GetAllAsync(Guid tenantId, CancellationToken ct = default);

    Task<SupplierRole?> GetByIdAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);

    Task<bool> DisplayNameExistsAsync(Guid tenantId, string displayName, CancellationToken ct = default);

    /// <summary>Whether at least one user of this tenant currently has the given role assigned.</summary>
    Task<bool> IsAssignedToAnyUserAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);

    Task AddAsync(SupplierRole role, CancellationToken ct = default);

    void Remove(SupplierRole role);

    Task SaveChangesAsync(CancellationToken ct = default);
}
