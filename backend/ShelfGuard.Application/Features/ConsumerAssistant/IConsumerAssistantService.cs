namespace ShelfGuard.Application.Features.ConsumerAssistant;

/// <summary>
/// Consumer-facing AI shopping assistant (managed-AI Phase 4b). A <c>ConsumerAccount</c>
/// session asks a question of a specific tenant's assistant (route <c>{tenantId}</c>); the
/// service aggregates ONLY that consumer's own loyalty data for that tenant plus the tenant's
/// public catalog / program rules / stores, wraps the prompt with the hardened
/// <see cref="Services.AiSlot.Consumer"/> isolation guardrail, and calls the model with the
/// tenant's provider-configured <c>ai_consumer</c> slot credentials.
///
/// The per-tenant <c>ai_consumer</c> config lives in <c>integration_configs</c> under RLS
/// <c>tenant_isolation</c>, which a consumer session (no <c>app.tenant_id</c> claim) cannot
/// read — the implementation reads it through <see cref="Services.ITenantSessionOverride"/>
/// exactly like the other consumer-facing tenant-scoped reads (ConsumerContentService).
/// </summary>
public interface IConsumerAssistantService
{
    /// <summary>
    /// Answers <paramref name="message"/>. Returns an error string (with HTTP status) when the
    /// tenant has no enabled <c>ai_consumer</c> slot, when the message is empty, or on a model
    /// failure.
    /// </summary>
    Task<(ConsumerAssistantAnswer? Answer, string? Error, int? StatusCode)> AskAsync(
        Guid consumerAccountId, Guid tenantId, string message, CancellationToken ct = default);
}

/// <param name="Text">The assistant's reply, already trimmed.</param>
public sealed record ConsumerAssistantAnswer(string Text, string Model, int TokensUsed);

/// <summary>POST /api/consumer/assistant/{tenantId}/ask body.</summary>
public sealed record ConsumerAssistantAskRequest(string Message);
