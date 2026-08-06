namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Everything the AI needs to write one "explain in more detail" answer for a Фаза 4
/// post-campaign segment's summary KPIs — same one-context-record shape as
/// <see cref="PriceSegmentAdvisorContext"/> (TASK-420 precedent for a sibling Фаза needing its
/// own differently-shaped explain context but the identical Claude-key-resolution pattern),
/// rather than forcing this segment's reactivation/retention/turnover-delta numbers into
/// <c>MarketingAdvisorContext</c>'s <c>RfmSegmentKey</c>-shaped fields, which don't apply here
/// (a post-campaign segment is an externally-sourced customer list, not an RFM segment).
/// <see cref="ExtraContextUa"/> carries the 1-2 numbers that don't generalize (e.g. the RFM
/// up/stable/down migration counts), same role it plays in <see cref="PriceSegmentAdvisorContext"/>.
/// </summary>
public sealed record PostCampaignAdvisorContext(
    string TitleUa,
    int MatchedCount,
    decimal? ReactivationRatePercent,
    decimal? RetentionRatePercent,
    decimal? MoneyDeltaPercent,
    string? ExtraContextUa,
    string TemplateTriggerUa,
    string TemplateActionUa,
    string TemplateOfferUa,
    string TemplateCautionUa);

public sealed record PostCampaignAdvisorResult(string ExplanationUa, string Model, int TokensUsed);

/// <summary>
/// AI post-campaign advisor (Фаза 4, TASK-472). Implemented by the Claude client in
/// Infrastructure/AI/PostCampaignAdvisor — prompts and provider specifics never leak past this
/// interface, same isolation rule as <see cref="IMarketingAdvisor"/>/<see cref="IPriceSegmentAdvisor"/>.
/// Called only on explicit user action ("Пояснити детальніше"), never on page load, and always
/// for exactly one analyzed segment at a time.
/// </summary>
public interface IPostCampaignAdvisor
{
    /// <summary>True when an API key is available (tenant integration config or environment).</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    Task<PostCampaignAdvisorResult> ExplainAsync(PostCampaignAdvisorContext context, CancellationToken ct = default);
}
