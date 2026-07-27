namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Everything the AI needs to write one "explain in more detail" answer for a Фаза 2 price-
/// segments audience/tier — deliberately ONE context shape reused by all three explain flows
/// (comparison price audience, all-time tier, frequency audience) instead of three separate
/// advisor interfaces: the three flows share the same underlying question ("elaborate on this
/// already-computed audience's KPIs and template recommendation"), differing only in which
/// numbers are meaningful for a given call site. <see cref="ExtraContextUa"/> is where each call
/// site supplies the 1-2 numbers that don't generalize across all three flows (e.g. the
/// previous/current median-check range for a comparison audience, or the previous/current
/// frequency for a reactivation audience) as a single pre-formatted Ukrainian line, mirroring how
/// <c>MarketingAdvisorContext</c> stitches its own peak-day/hour text before handing it to the
/// prompt builder.
/// </summary>
public sealed record PriceSegmentAdvisorContext(
    string TitleUa,
    int CustomerCount,
    decimal? SharePercent,
    decimal? AverageLtv,
    string? ExtraContextUa,
    string TemplateTriggerUa,
    string TemplateActionUa,
    string TemplateOfferUa,
    string TemplateCautionUa);

public sealed record PriceSegmentAdvisorResult(string ExplanationUa, string Model, int TokensUsed);

/// <summary>
/// AI price-segments advisor (Фаза 2, TASK-420 design doc §0: "5-й адвайзер, той самий патерн
/// резолву Claude-ключа, що вже в ClaudeOrderAdvisor.cs/MarketingAdvisor.cs"). Implemented by
/// the Claude client in Infrastructure/AI/PriceSegmentAdvisor — prompts and provider specifics
/// never leak past this interface, same isolation rule as <see cref="IMarketingAdvisor"/>.
/// Called only on explicit user action ("Пояснити детальніше"), never on page load, and always
/// for exactly one audience/tier at a time.
/// </summary>
public interface IPriceSegmentAdvisor
{
    /// <summary>True when an API key is available (tenant integration config or environment).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    Task<PriceSegmentAdvisorResult> ExplainAsync(PriceSegmentAdvisorContext context, CancellationToken ct = default);
}
