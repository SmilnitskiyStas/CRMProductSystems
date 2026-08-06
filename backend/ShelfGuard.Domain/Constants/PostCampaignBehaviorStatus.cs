using System.Text.Json.Serialization;

namespace ShelfGuard.Domain.Constants;

/// <summary>
/// The four mutually-exclusive before/after behavioral states for a Фаза 4 post-campaign
/// segment member (TASK-472, `docs/uployal/AUDIENCE_ANALYSIS.md` §11.1-§11.6/§36.4). Computed
/// purely from purchase counts in the before/after windows — see
/// <see cref="Application.Features.MarketingAnalytics.PostCampaign.PostCampaignBehaviorClassifier"/>.
///
/// Named/placed the same way <see cref="RfmSegmentKey"/>/<see cref="PriceSegmentKey"/> are —
/// classifier-OUTPUT enum, so it lives in Domain.Constants (not alongside request DTOs, which is
/// where AudienceBuilder's own request-shape-toggle enums live instead — see that module's Dtos
/// file for why the two cases are kept apart).
///
/// [JsonConverter] scoped to only this type, same convention as every other wire-facing enum in
/// this codebase (RfmSegmentKey, PriceSegmentKey, ...): serializes as "Reactivated"/"Retained"/
/// etc, never a raw ordinal.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PostCampaignBehaviorStatus
{
    /// <summary>purchases_before = 0 AND purchases_after &gt; 0.</summary>
    Reactivated = 1,
    /// <summary>purchases_before &gt; 0 AND purchases_after &gt; 0.</summary>
    Retained = 2,
    /// <summary>purchases_before &gt; 0 AND purchases_after = 0.</summary>
    Dropped = 3,
    /// <summary>purchases_before = 0 AND purchases_after = 0.</summary>
    NotReturned = 4,
}
