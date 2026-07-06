using ShelfGuard.Application.Features.Marketplace.Dtos;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// Custom staff roles scoped to a single supplier tenant (TASK-306, plan
/// calm-singing-marble.md Part 3). Unlike ProviderRole (global to the platform),
/// every supplier tenant manages its own set of roles independently — every
/// operation is scoped to <c>tenantId</c> (the calling supplier tenant).
/// </summary>
public interface ISupplierRolesService
{
    Task<IReadOnlyList<SupplierRoleDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default);

    Task<(SupplierRoleDto? Role, string? Error)> CreateAsync(
        Guid tenantId, CreateSupplierRoleRequest request, CancellationToken ct = default);

    Task<(SupplierRoleDto? Role, string? Error)> UpdateAsync(
        Guid tenantId, Guid roleId, UpdateSupplierRoleRequest request, CancellationToken ct = default);

    /// <summary>Returns an error if the role does not exist for this tenant, is a system role,
    /// or is currently assigned to at least one staff member.</summary>
    Task<(bool Success, string? Error)> DeleteAsync(
        Guid tenantId, Guid roleId, CancellationToken ct = default);
}
