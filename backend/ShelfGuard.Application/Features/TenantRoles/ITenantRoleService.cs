using ShelfGuard.Application.Features.TenantRoles.Dtos;

namespace ShelfGuard.Application.Features.TenantRoles;

/// <summary>
/// CRUD for tenant-scoped custom capability-set templates (ADR-020, TASK-345/346). Every
/// operation is scoped by tenantId — templates are per-tenant; editing one propagates live to
/// every assignee (User.TenantRoleId), same "template not snapshot" semantics as
/// ProviderRole/SupplierRole.
/// </summary>
public interface ITenantRoleService
{
    Task<IReadOnlyList<TenantRoleDto>> GetAllAsync(Guid tenantId, bool includeInactive, CancellationToken ct = default);

    Task<(TenantRoleDto? Role, string? Error)> GetByIdAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);

    /// <summary>Validates Capabilities against TenantRoleCapabilities.All and Name uniqueness
    /// (active templates only) before creating.</summary>
    Task<(TenantRoleDto? Role, string? Error)> CreateAsync(
        Guid tenantId, Guid? createdByUserId, CreateTenantRoleRequest request, CancellationToken ct = default);

    Task<(TenantRoleDto? Role, string? Error)> UpdateAsync(
        Guid tenantId, Guid roleId, UpdateTenantRoleRequest request, CancellationToken ct = default);

    /// <summary>Archives (soft-deletes) a template — never a hard delete, see TenantRole.Deactivate.
    /// Existing assignees (User.TenantRoleId) are left pointing at the archived row; their
    /// effective capabilities become empty on next JWT mint (AuthService.BuildEffectiveCapabilitiesAsync).</summary>
    Task<(bool Success, string? Error)> DeactivateAsync(Guid tenantId, Guid roleId, CancellationToken ct = default);

    /// <summary>Backend source-of-truth capability catalog grouped by specialty
    /// (GET /api/tenant-roles/capabilities) — not tenant-scoped, same for every caller.</summary>
    IReadOnlyList<TenantRoleCapabilityGroupDto> GetCapabilityCatalog();
}
