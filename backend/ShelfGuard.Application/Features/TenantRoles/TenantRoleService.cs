using ShelfGuard.Application.Features.TenantRoles.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.TenantRoles;

/// <summary>
/// CRUD for custom TenantRole capability templates, scoped to the calling tenant (ADR-020,
/// TASK-345/346). Mirrors SupplierRolesService's validation shape (Marketplace feature) — same
/// "validate → uniqueness pre-check → mutate → SaveChanges" flow — but Deactivate archives
/// instead of hard-deleting, since TenantRole.Capabilities resolution already tolerates an
/// inactive template gracefully (AuthService.BuildEffectiveCapabilitiesAsync returns an empty
/// list for it), unlike SupplierRole which is hard-FK-referenced and must block deletion while
/// still assigned.
/// </summary>
public sealed class TenantRoleService : ITenantRoleService
{
    private readonly ITenantRoleRepository _repo;

    public TenantRoleService(ITenantRoleRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<TenantRoleDto>> GetAllAsync(
        Guid tenantId, bool includeInactive, CancellationToken ct = default)
    {
        var roles = await _repo.GetAllForTenantAsync(tenantId, includeInactive, ct);
        return roles.Select(Map).ToList();
    }

    public async Task<(TenantRoleDto? Role, string? Error)> GetByIdAsync(
        Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        var role = await _repo.GetByIdAsync(tenantId, roleId, ct);
        return role is null ? (null, "Template not found.") : (Map(role), null);
    }

    public async Task<(TenantRoleDto? Role, string? Error)> CreateAsync(
        Guid tenantId, Guid? createdByUserId, CreateTenantRoleRequest request, CancellationToken ct = default)
    {
        var capabilities = request.Capabilities ?? [];
        var validationError = Validate(request.Name, capabilities);
        if (validationError is not null)
            return (null, validationError);

        var trimmedName = request.Name.Trim();
        if (await _repo.GetByNameAsync(tenantId, trimmedName, ct) is not null)
            return (null, $"Template '{trimmedName}' already exists.");

        var role = TenantRole.Create(tenantId, trimmedName, capabilities.Distinct(), createdByUserId);
        await _repo.AddAsync(role, ct);
        await _repo.SaveChangesAsync(ct);

        return (Map(role), null);
    }

    public async Task<(TenantRoleDto? Role, string? Error)> UpdateAsync(
        Guid tenantId, Guid roleId, UpdateTenantRoleRequest request, CancellationToken ct = default)
    {
        var role = await _repo.GetByIdAsync(tenantId, roleId, ct);
        if (role is null)
            return (null, "Template not found.");

        var capabilities = request.Capabilities ?? [];
        var validationError = Validate(request.Name, capabilities);
        if (validationError is not null)
            return (null, validationError);

        var trimmedName = request.Name.Trim();
        if (trimmedName != role.Name && await _repo.GetByNameAsync(tenantId, trimmedName, ct) is not null)
            return (null, $"Template '{trimmedName}' already exists.");

        role.Update(trimmedName, capabilities.Distinct());
        await _repo.UpdateAsync(role, ct);
        await _repo.SaveChangesAsync(ct);

        return (Map(role), null);
    }

    public async Task<(bool Success, string? Error)> DeactivateAsync(
        Guid tenantId, Guid roleId, CancellationToken ct = default)
    {
        var deactivated = await _repo.DeactivateAsync(tenantId, roleId, ct);
        if (!deactivated)
            return (false, "Template not found or already archived.");

        await _repo.SaveChangesAsync(ct);
        return (true, null);
    }

    public IReadOnlyList<TenantRoleCapabilityGroupDto> GetCapabilityCatalog() =>
        TenantRoleCapabilities.Groups
            .Select(g => new TenantRoleCapabilityGroupDto(
                g.Specialty,
                g.Capabilities.Select(c => new TenantRoleCapabilityDto(c.Key, c.LabelUa)).ToList()))
            .ToList();

    // ── helpers ───────────────────────────────────────────────────────────

    private static string? Validate(string name, List<string> capabilities)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required.";

        var unknown = capabilities.Where(c => !TenantRoleCapabilities.All.Contains(c)).ToList();
        if (unknown.Count > 0)
            return $"Unknown capability(ies): {string.Join(", ", unknown)}.";

        return null;
    }

    private static TenantRoleDto Map(TenantRole r) =>
        new(r.Id, r.Name, r.Capabilities, r.IsActive, r.CreatedAt, r.UpdatedAt);
}
