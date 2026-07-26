using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>Everything the AI needs to write one segment's "explain in more detail" answer —
/// the same KPIs already shown on the segment-detail panel plus the template's own generated
/// text, so the model elaborates on real numbers instead of inventing its own.</summary>
public sealed record MarketingAdvisorContext(
    RfmSegmentKey SegmentKey,
    string SegmentLabelUa,
    int CustomerCount,
    decimal SharePercentOfPeriodCustomers,
    decimal SharePercentOfPeriodRevenue,
    decimal AverageRecencyDays,
    decimal AverageLtv,
    string? TopProductName,
    string TemplateTriggerUa,
    string TemplateActionUa,
    string TemplateOfferUa,
    string TemplateCautionUa);

public sealed record MarketingAdvisorResult(string ExplanationUa, string Model, int TokensUsed);

/// <summary>
/// AI marketing advisor abstraction (TASK-406, plan §"Рекомендації — гібридний рушій").
/// Implemented by the Claude client in Infrastructure/AI/MarketingAdvisor — prompts and
/// provider specifics never leak past this interface, same isolation rule as
/// <see cref="IAiOrderAdvisor"/>. Called only on explicit user action ("Пояснити детальніше"),
/// never on page load, and always for exactly one segment at a time.
/// </summary>
public interface IMarketingAdvisor
{
    /// <summary>True when an API key is available (tenant integration config or environment).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    Task<MarketingAdvisorResult> ExplainAsync(MarketingAdvisorContext context, CancellationToken ct = default);
}
