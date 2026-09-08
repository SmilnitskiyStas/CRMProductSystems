using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Persists the consumer-AI-assistant audit trail (managed-AI Phase 4b). The write happens
/// inside an <c>ITenantSessionOverride</c> block so the row's <c>TenantId</c> satisfies RLS
/// <c>tenant_isolation</c>.
/// </summary>
public interface IConsumerAiRequestRepository
{
    Task AddAsync(ConsumerAiRequest entry, CancellationToken ct = default);
}
