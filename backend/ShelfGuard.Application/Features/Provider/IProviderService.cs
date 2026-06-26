using ShelfGuard.Application.Features.Provider.Dtos;

namespace ShelfGuard.Application.Features.Provider;

public interface IProviderService
{
    // Tenant listing
    Task<IReadOnlyList<TenantSummaryDto>> GetTenantsAsync(CancellationToken ct);
    Task<(TenantDetailDto? Tenant, string? Error)> GetTenantAsync(Guid tenantId, CancellationToken ct);

    // Tenant creation
    Task<(TenantDetailDto? Tenant, string? Error)> CreateTenantAsync(CreateTenantRequest request, CancellationToken ct);

    // Plan & modules
    Task<string?> UpdatePlanAsync(Guid tenantId, string plan, CancellationToken ct);
    Task<string?> UpdateModulesAsync(Guid tenantId, string[] modules, CancellationToken ct);

    // Tenant activation
    Task<(bool Success, string? Error)> ActivateTenantAsync(Guid tenantId, CancellationToken ct);
    Task<(bool Success, string? Error)> DeactivateTenantAsync(Guid tenantId, CancellationToken ct);

    // Impersonation
    Task<(ImpersonateResponse? Response, string? Error)> ImpersonateAsync(
        Guid providerId, string providerEmail, Guid targetTenantId, CancellationToken ct);

    // Tenant users
    Task<IReadOnlyList<TenantUserDto>> GetTenantUsersAsync(Guid tenantId, CancellationToken ct);
    Task<(TenantUserDto? User, string? Error)> CreateTenantUserAsync(Guid tenantId, CreateTenantUserRequest request, CancellationToken ct);

    // Health & observability
    Task<ProviderHealthDto> GetHealthAsync(CancellationToken ct);
    Task<ProviderLogsPageDto> GetLogsAsync(ProviderLogsQuery query, CancellationToken ct);
}
