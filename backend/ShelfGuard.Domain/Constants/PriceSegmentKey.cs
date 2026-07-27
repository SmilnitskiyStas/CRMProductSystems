using System.Text.Json.Serialization;

namespace ShelfGuard.Domain.Constants;

/// <summary>
/// The 7 network-wide price tiers (Фаза 2, `deep-cooking-nygaard.md` §"Фази 2-4" +
/// TASK-420 design doc §1.3). Deliberately ORDINAL (Tier1 = lowest .. Tier7 = "960+" open-ended
/// top tier) rather than semantic/currency-named members — the actual ₴ boundaries are
/// per-tenant and recomputed live from that tenant's own P20/P40/P60/P80/P90/P97 distribution
/// (see <c>PriceSegmentBoundariesRow</c>), so a fixed label like "120-190" would be wrong for
/// almost every tenant. A human-readable label is produced dynamically via
/// <c>PriceSegmentCatalog.RangeLabelUa(key, boundaries)</c> — never hardcoded here.
///
/// Int-backed (not just for JSON — <see cref="Application.Features.MarketingAnalytics.
/// PriceSegments.PriceAudienceClassifier"/> relies on plain <c>&gt;</c>/<c>&lt;</c> comparison
/// between two <see cref="PriceSegmentKey"/> values to detect a segment transition, which only
/// works because the numeric order below IS the tier order, low to high).
///
/// [JsonConverter] mirrors <see cref="RfmSegmentKey"/>'s own scoped (not global) string-enum
/// convention: serializes as "Tier1".."Tier7", never a raw ordinal.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PriceSegmentKey
{
    /// <summary>Below P20 — analysis doc's "до 120 ₴" example row (network-specific in reality).</summary>
    Tier1 = 1,
    Tier2 = 2,
    Tier3 = 3,
    Tier4 = 4,
    Tier5 = 5,
    Tier6 = 6,
    /// <summary>P97 and above — deliberately open-ended on the top end (never clips large spenders).</summary>
    Tier7 = 7,
}
