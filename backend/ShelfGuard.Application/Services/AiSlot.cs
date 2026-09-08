namespace ShelfGuard.Application.Services;

/// <summary>
/// Which managed-AI agent profile an advisor uses (managed-AI Phase 4). Each slot is a
/// separate per-tenant config row — <c>integration_configs</c> with <c>Service</c> = the slot's
/// service key and the provider name inside the <c>Config</c> jsonb — so the platform provider
/// can run deep analysis on a strong model and high-volume light work on a cheap one, each
/// with its own key / model / preset. The isolation guardrail applies to every slot.
/// </summary>
public enum AiSlot
{
    /// <summary>Deep internal analysis — order advisor, supplier advisor, business assistant. Strong model.</summary>
    Analyst,

    /// <summary>Light internal explainers — marketing / price-segment / post-campaign. Cheap model, short output.</summary>
    Assistant,

    /// <summary>Consumer-facing assistant in the mobile app (Phase 4b). Hardened guardrail — talks to a customer, not staff.</summary>
    Consumer,
}

/// <summary>Slot ↔ <c>integration_configs.Service</c> key mapping.</summary>
public static class AiSlots
{
    public const string AnalystService = "ai_analyst";
    public const string AssistantService = "ai_assistant";
    public const string ConsumerService = "ai_consumer";

    public static readonly IReadOnlyList<AiSlot> All = new[] { AiSlot.Analyst, AiSlot.Assistant, AiSlot.Consumer };

    public static string ServiceKey(this AiSlot slot) => slot switch
    {
        AiSlot.Analyst => AnalystService,
        AiSlot.Assistant => AssistantService,
        AiSlot.Consumer => ConsumerService,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };

    public static bool TryFromService(string? service, out AiSlot slot)
    {
        switch (service)
        {
            case AnalystService: slot = AiSlot.Analyst; return true;
            case AssistantService: slot = AiSlot.Assistant; return true;
            case ConsumerService: slot = AiSlot.Consumer; return true;
            default: slot = default; return false;
        }
    }
}
