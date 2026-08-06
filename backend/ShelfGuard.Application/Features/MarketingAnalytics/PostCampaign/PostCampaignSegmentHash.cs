using System.Security.Cryptography;
using System.Text;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;

/// <summary>
/// Deterministic hash of a frozen post-campaign segment's identity + analyzed window, stored on
/// <see cref="Domain.Entities.PostCampaignSegment.SegmentHash"/> and recomputed only when
/// <c>analyze</c> re-freezes the window (never per-request) — same purpose as Фаза 1's
/// <c>RfmFilterHash</c>/Фаза 3's <c>AudienceBuilderFilterHash</c>, but a stored, versioned field
/// rather than a per-request-computed one, since a post-campaign segment's underlying member list
/// is itself a persisted, frozen artifact (source doc §7/§29 — every report must be traceably tied
/// to one specific analyzed snapshot, not just one filter combination).
/// </summary>
public static class PostCampaignSegmentHash
{
    public static string Compute(Guid tenantId, Guid segmentId, DateOnly afterStart, DateOnly afterEnd)
    {
        var raw = string.Join('|',
            tenantId.ToString("N"),
            segmentId.ToString("N"),
            afterStart.ToString("yyyy-MM-dd"),
            afterEnd.ToString("yyyy-MM-dd"));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
