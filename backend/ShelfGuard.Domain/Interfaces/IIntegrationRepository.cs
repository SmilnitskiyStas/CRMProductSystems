using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IIntegrationRepository
{
    Task<IReadOnlyList<IntegrationConfig>> GetAllByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<IntegrationConfig?> GetByServiceAsync(Guid tenantId, string service, CancellationToken ct = default);
    Task UpsertAsync(Guid tenantId, string service, string config, bool isEnabled, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid tenantId, string service, CancellationToken ct = default);
}
