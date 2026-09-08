namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Audit trail for the consumer-facing AI assistant (managed-AI Phase 4b). One row per answered
/// consumer question. Stores metadata + a short excerpt of the prompt and reply (first ~200
/// chars each) — enough to investigate a "the assistant said something wrong" complaint without
/// retaining the full PII dialogue. Retention: a cleanup job prunes rows older than 90 days
/// (follow-up). RLS: canonical tenant_isolation / provider_bypass / worker_bypass triad plus
/// consumer_self_access on <see cref="ConsumerAccountId"/>.
/// </summary>
public sealed class ConsumerAiRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The tenant whose consumer assistant was asked.</summary>
    public Guid TenantId { get; init; }

    /// <summary>The consumer who asked.</summary>
    public Guid ConsumerAccountId { get; init; }

    /// <summary>First ~200 chars of the consumer's message.</summary>
    public string PromptExcerpt { get; init; } = string.Empty;

    /// <summary>First ~200 chars of the assistant's reply.</summary>
    public string ResponseExcerpt { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public int TokensUsed { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
