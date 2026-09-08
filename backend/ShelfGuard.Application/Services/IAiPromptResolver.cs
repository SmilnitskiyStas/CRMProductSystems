namespace ShelfGuard.Application.Services;

/// <summary>
/// Wraps an AI advisor's own system prompt with the mandatory per-tenant isolation guardrail
/// (managed-AI Phase 1). Every advisor in <c>ShelfGuard.Infrastructure.AI</c> must route its
/// <c>System</c> prompt through this before calling the model, so the agent stays scoped to the
/// calling business's own data and refuses competitor / cross-business / benchmark questions.
///
/// The guardrail is an invariant — it cannot be disabled by tenant or provider config. The
/// per-tenant <c>extra_instructions</c> (set by the provider in the client card) is appended
/// after the guardrail and before the advisor's base prompt.
///
/// Resolution reads the current tenant from <see cref="ITenantContext"/> (JWT-scoped). When
/// there is no tenant context (provider / worker / unauthenticated), the base prompt is
/// returned unchanged.
///
/// <paramref name="slot"/> (managed-AI Phase 4) selects which agent profile's
/// <c>extra_instructions</c> to append and which guardrail variant to use — the
/// <see cref="AiSlot.Consumer"/> guardrail is hardened for a customer-facing audience.
/// </summary>
public interface IAiPromptResolver
{
    Task<string> WrapSystemPromptAsync(string basePrompt, AiSlot slot, CancellationToken ct = default);

    /// <summary>
    /// Pure guardrail composition for a caller that already holds the tenant name and the
    /// slot's <c>extra_instructions</c> (managed-AI Phase 4b — the consumer assistant has no
    /// <see cref="ITenantContext"/>, so it must not go through <see cref="WrapSystemPromptAsync"/>,
    /// which would silently drop the guardrail for a null tenant context). No I/O.
    /// </summary>
    string Compose(string? tenantName, string? extraInstructions, string basePrompt, AiSlot slot);
}
