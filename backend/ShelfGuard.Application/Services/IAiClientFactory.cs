namespace ShelfGuard.Application.Services;

/// <summary>
/// Resolves the right <see cref="IAiChatClient"/> for the current request (managed-AI Phase 2;
/// per-slot since Phase 4). The advisors call <see cref="ResolveAsync"/> with their
/// <see cref="AiSlot"/>, which reads that slot's tenant AI config
/// (<c>integration_configs</c> service = <c>ai_analyst</c> / <c>ai_assistant</c> / <c>ai_consumer</c>)
/// under RLS. A slot with no config falls back to the <see cref="AiSlot.Analyst"/> slot, then to
/// the <c>Claude:ApiKey</c> / <c>OpenAI:ApiKey</c> env vars. <see cref="Create"/> builds a client
/// from explicit credentials for the provider's "test connection" button.
/// Implementation: <c>ShelfGuard.Infrastructure.AI.AiClientFactory</c>.
/// </summary>
public interface IAiClientFactory
{
    /// <summary>The chat client for <paramref name="slot"/> (with analyst / env fallback), or null when nothing is configured.</summary>
    Task<IAiChatClient?> ResolveAsync(AiSlot slot, CancellationToken ct = default);

    /// <summary>True iff <see cref="ResolveAsync"/> would return a client for <paramref name="slot"/>.</summary>
    Task<bool> IsConfiguredAsync(AiSlot slot, CancellationToken ct = default);

    /// <summary>A client built from explicit credentials — used by the connectivity probe.</summary>
    IAiChatClient Create(AiProviderConfig config);

    /// <summary>
    /// A client for an explicit provider config, falling back to the shared env key for that
    /// provider when <paramref name="apiKey"/> is null/blank. Null when neither has a key. Used
    /// by the consumer assistant (Phase 4b), which reads the tenant's <c>ai_consumer</c> config
    /// itself (through <see cref="ITenantSessionOverride"/>) and cannot use <see cref="ResolveAsync"/>.
    /// </summary>
    IAiChatClient? CreateOrEnv(string provider, string? apiKey, string? model, string? baseUrl);
}

/// <param name="Provider">"claude" (Anthropic) or "openai" (OpenAI-compatible / Azure / Codex).</param>
/// <param name="BaseUrl">Optional base URL for an OpenAI-compatible endpoint; ignored for "claude".</param>
public sealed record AiProviderConfig(string Provider, string ApiKey, string Model, string? BaseUrl = null);
