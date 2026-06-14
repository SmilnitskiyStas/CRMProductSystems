using ShelfGuard.Application.Features.Admin.Dtos;

namespace ShelfGuard.Application.Features.Admin;

/// <summary>
/// SaaS Admin Panel: tenant lifecycle management.
/// All operations require role = "provider"; authorization is enforced at the controller level.
/// </summary>
public interface ITenantAdminService
{
    Task<IReadOnlyList<TenantDto>> GetAllTenantsAsync(CancellationToken ct);
    Task<TenantDto?> GetTenantAsync(Guid id, CancellationToken ct);
    Task<(TenantDto? Tenant, string? Error)> CreateTenantAsync(CreateTenantRequest req, CancellationToken ct);
    Task<(TenantDto? Tenant, string? Error)> UpdatePlanAsync(Guid id, string plan, CancellationToken ct);
    Task<(TenantDto? Tenant, string? Error)> UpdateModulesAsync(Guid id, IReadOnlyList<string> modules, CancellationToken ct);
    Task<(TenantDto? Tenant, string? Error)> ActivateAsync(Guid id, CancellationToken ct);
    Task<(TenantDto? Tenant, string? Error)> DeactivateAsync(Guid id, CancellationToken ct);
}
