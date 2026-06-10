using ShelfGuard.Application.Features.Integrations.Dtos;

namespace ShelfGuard.Application.Features.Integrations;

public interface IIntegrationService
{
    Task<IReadOnlyList<IntegrationSummaryDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<(IntegrationConfigDto? Config, string? Error)> GetByServiceAsync(Guid tenantId, string service, CancellationToken ct = default);
    Task<string?> UpsertAsync(Guid tenantId, string service, UpsertIntegrationRequest request, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> DeleteAsync(Guid tenantId, string service, CancellationToken ct = default);
}
