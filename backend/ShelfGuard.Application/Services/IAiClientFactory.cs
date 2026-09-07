namespace ShelfGuard.Application.Services;

/// <summary>
/// Resolves the right <see cref="IAiChatClient"/> for the current request (managed-AI Phase 2).
/// The advisors call <see cref="ResolveAsync"/>, which reads the caller's tenant AI config
/// (<c>integration_configs</c> service = 'claude' or 'openai') under RLS, falling back to the
/// <c>Claude:ApiKey</c> / <c>OpenAI:ApiKey</c> env vars. <see cref="Create"/> builds a client
/// from explicit credentials for the provider's "test connection" button.
/// Implementation: <c>ShelfGuard.Infrastructure.AI.AiClientFactory</c>.
/// </summary>
public interface IAiClientFactory
{
    /// <summary>The current tenant's configured chat client, or null when none is configured (nor env).</summary>
    Task<IAiChatClient?> ResolveAsync(CancellationToken ct = default);

    /// <summary>True iff <see cref="ResolveAsync"/> would return a client.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    /// <summary>A client built from explicit credentials — used by the connectivity probe.</summary>
    IAiChatClient Create(AiProviderConfig config);
}

/// <param name="Provider">"claude" (Anthropic) or "openai" (OpenAI-compatible / Azure / Codex).</param>
/// <param name="BaseUrl">Optional base URL for an OpenAI-compatible endpoint; ignored for "claude".</param>
public sealed record AiProviderConfig(string Provider, string ApiKey, string Model, string? BaseUrl = null);
