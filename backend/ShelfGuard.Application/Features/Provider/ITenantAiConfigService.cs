using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Application.Features.Provider;

/// <summary>
/// Provider-only management of a tenant's AI-agent slot connections (managed-AI Phase 1;
/// per-slot since Phase 4). Backs <c>/api/provider/tenants/{id}/ai-agents[/{slot}]</c>. Writes
/// land in the tenant's <c>integration_configs</c> row for that slot (service
/// <c>ai_analyst</c> / <c>ai_assistant</c> / <c>ai_consumer</c>, provider inside the jsonb) —
/// a provider session already has the cross-tenant <c>provider_bypass</c> RLS grant, so no
/// override primitive is taken here. Authorization is enforced at the controller
/// (<c>ProviderOnly</c>).
/// </summary>
public interface ITenantAiConfigService
{
    /// <summary>Every slot (configured or not), analyst first.</summary>
    Task<IReadOnlyList<TenantAiAgentDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default);

    Task<TenantAiAgentDto> GetAsync(Guid tenantId, AiSlot slot, CancellationToken ct = default);

    /// <summary>Upserts the slot's config. Returns an error string on validation failure, else null.</summary>
    Task<string?> UpdateAsync(Guid tenantId, AiSlot slot, UpdateAiAgentRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid tenantId, AiSlot slot, CancellationToken ct = default);

    /// <summary>Connectivity probe. Always succeeds as an operation — the result carries ok/error.</summary>
    Task<AiAgentTestResult> TestAsync(Guid tenantId, AiSlot slot, UpdateAiAgentRequest? candidate, CancellationToken ct = default);
}
