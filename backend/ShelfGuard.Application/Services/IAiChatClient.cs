namespace ShelfGuard.Application.Services;

/// <summary>
/// Provider-agnostic single-turn chat completion (managed-AI Phase 2). The 6 AI advisors go
/// through this instead of talking to a model SDK directly, so the same advisor code works
/// with Claude or an OpenAI-compatible endpoint depending on the tenant's provider config.
/// Implementations live in <c>ShelfGuard.Infrastructure.AI</c> (ADR-015 AI-isolation).
///
/// <see cref="CompleteAsync"/> throws on failure; the thrown exception's <c>Message</c> carries
/// the provider's raw error text verbatim — callers (<c>AiOrderService</c>, <c>AiAssistantService</c>)
/// pattern-match it for "credit balance" / "quota" to show a billing-specific message.
/// </summary>
public interface IAiChatClient
{
    Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken ct = default);
}

/// <param name="JsonSchema">
/// Raw JSON-schema string. When non-null the model is asked for structured output constrained
/// to this schema (Anthropic <c>OutputConfig</c> / OpenAI <c>response_format: json_schema</c>);
/// null = plain text.
/// </param>
public sealed record AiChatRequest(string SystemPrompt, string UserPrompt, int MaxTokens, string? JsonSchema = null);

public sealed record AiChatResult(string Text, string Model, int TokensUsed);
