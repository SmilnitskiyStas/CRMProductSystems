using ShelfGuard.Application.Features.Provider.Dtos;

namespace ShelfGuard.Application.Features.Provider;

/// <summary>
/// Provider-only management of a tenant's AI-agent connection (managed-AI Phase 1). Backs
/// <c>/api/provider/tenants/{id}/ai-agent</c>. Writes land in the tenant's
/// <c>integration_configs</c> row (service='claude') — a provider session already has the
/// cross-tenant <c>provider_bypass</c> RLS grant, so no override primitive is taken here.
/// Authorization is enforced at the controller (<c>ProviderOnly</c>).
/// </summary>
public interface ITenantAiConfigService
{
    Task<TenantAiAgentDto> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Upserts the config. Returns an error string on validation failure, else null.</summary>
    Task<string?> UpdateAsync(Guid tenantId, UpdateAiAgentRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Connectivity probe. Always succeeds as an operation — the result carries ok/error.</summary>
    Task<AiAgentTestResult> TestAsync(Guid tenantId, UpdateAiAgentRequest? candidate, CancellationToken ct = default);
}
