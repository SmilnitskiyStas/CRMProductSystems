using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Marketplace;

/// <summary>
/// CRUD for custom supplier staff roles, scoped to the calling tenant (TASK-306).
/// Mirrors ProviderRolesService's validation shape, but every operation is scoped
/// by tenantId — each supplier tenant manages its own roles independently.
/// </summary>
public sealed class SupplierRolesService : ISupplierRolesService
{
    private readonly ISupplierRolesRepository _repo;

    public SupplierRolesService(ISupplierRolesRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<SupplierRoleDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        var roles = await _repo.GetAllAsync(tenantId, ct);
        return roles.Select(Map).ToList();
    }

    public async Task<(SupplierRoleDto? Role, string? Error)> CreateAsync(
        Guid tenantId, CreateSupplierRoleRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return (null, "Display name is required.");

        if (request.BaseRole != AppRoles.SupplierAdmin)
            return (null, $"Base role '{request.BaseRole}' is invalid. Only '{AppRoles.SupplierAdmin}' is supported.");

        var invalidPerms = request.Permissions.Where(p => !SupplierPermissions.All.Contains(p)).ToList();
        if (invalidPerms.Count > 0)
            return (null, $"Unknown permission(s): {string.Join(", ", invalidPerms)}.");

        var trimmedName = request.DisplayName.Trim();
        if (await _repo.DisplayNameExistsAsync(tenantId, trimmedName, ct))
            return (null, $"Role '{trimmedName}' already exists.");

        var role = SupplierRole.Create(tenantId, trimmedName, request.BaseRole, request.Permissions);
        await _repo.AddAsync(role, ct);
        await _repo.SaveChangesAsync(ct);

        return (Map(role), null);
    }

    public async Task<(SupplierRoleDto? Role, string? Error)> UpdateAsync(
        Guid tenantId, Guid roleId, UpdateSupplierRoleRequest request, CancellationToken ct = default)
    {
        var role = await _repo.GetByIdAsync(tenantId, roleId, ct);
        if (role is null)
            return (null, "Role not found.");

        if (role.IsSystem)
            return (null, "System roles cannot be edited.");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return (null, "Display name is required.");

        if (request.BaseRole != AppRoles.SupplierAdmin)
            return (null, $"Base role '{request.BaseRole}' is invalid. Only '{AppRoles.SupplierAdmin}' is supported.");

        var invalidPerms = request.Permissions.Where(p => !SupplierPermissions.All.Contains(p)).ToList();
        if (invalidPerms.Count > 0)
            return (null, $"Unknown permission(s): {string.Join(", ", invalidPerms)}.");

        var trimmedName = request.DisplayName.Trim();
        if (trimmedName != role.DisplayName && await _repo.DisplayNameExistsAsync(tenantId, trimmedName, ct))
            return (null, $"Role '{trimmedName}' already exists.");

        role.Update(trimmedName, request.BaseRole, request.Permissions);
        await _repo.SaveChangesAsync(ct);

        return (Map(role), null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(
        Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        var role = await _repo.GetByIdAsync(tenantId, roleId, ct);
        if (role is null)
            return (false, "Role not found.");

        if (role.IsSystem)
            return (false, "System roles cannot be deleted.");

        if (await _repo.IsAssignedToAnyUserAsync(tenantId, roleId, ct))
            return (false, "Cannot delete a role currently assigned to staff. Reassign them first.");

        _repo.Remove(role);
        await _repo.SaveChangesAsync(ct);

        return (true, null);
    }

    private static SupplierRoleDto Map(SupplierRole r) =>
        new(r.Id, r.DisplayName, r.BaseRole, [.. r.Permissions], r.IsSystem);
}
