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
/// </summary>
public interface IAiPromptResolver
{
    Task<string> WrapSystemPromptAsync(string basePrompt, CancellationToken ct = default);
}
