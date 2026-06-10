using ShelfGuard.Application.Features.Provider.Dtos;

namespace ShelfGuard.Application.Features.Provider;

public interface IProviderService
{
    // Tenant listing
    Task<IReadOnlyList<TenantSummaryDto>> GetTenantsAsync(CancellationToken ct);
    Task<(TenantDetailDto? Tenant, string? Error)> GetTenantAsync(Guid tenantId, CancellationToken ct);

    // Plan & modules
    Task<string?> UpdatePlanAsync(Guid tenantId, string plan, CancellationToken ct);
    Task<string?> UpdateModulesAsync(Guid tenantId, string[] modules, CancellationToken ct);

    // Impersonation
    Task<(ImpersonateResponse? Response, string? Error)> ImpersonateAsync(
        Guid providerId, string providerEmail, Guid targetTenantId, CancellationToken ct);

    // Health & observability
    Task<ProviderHealthDto> GetHealthAsync(CancellationToken ct);
    Task<IReadOnlyList<ProviderLogDto>> GetLogsAsync(int limit, CancellationToken ct);
}
