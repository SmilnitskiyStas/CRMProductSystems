namespace ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;

/// <summary>
/// Allowlist source of truth for <c>GET segments/{id}/customers</c>'s <c>sortBy</c> query param
/// (source doc §23.2: ID itself is never sortable — only checks-before/after, turnover-before/
/// after, and the segment transition are). Same shape as <c>AudienceBuilderSortKeys</c>/
/// <c>PriceSegmentSortKeys</c> — an unrecognized/omitted value silently normalizes to the default
/// rather than throwing (sorting is a display nicety, never worth a 400).
///
/// Unlike Фаза 2/3's sort keys (which the repository also consumes to build a literal SQL ORDER
/// BY column), this table is sorted in-memory in <c>PostCampaignService</c> — see that class's
/// remarks for why (the table is capped at the 20,000-row import limit and needs the pure C#
/// <c>RfmSegmentClassifier</c> applied to every row, which has no SQL equivalent) — but the same
/// allowlist discipline still applies: the raw <c>sortBy</c> string is only ever compared against
/// this fixed set, never used to build an expression dynamically.
/// </summary>
public static class PostCampaignSortKeys
{
    public const string CustomersDefault = "turnoverafter"; // source doc §23: "Топ за оборотом «після»"

    private static readonly HashSet<string> CustomerKeys =
        ["checksbefore", "checksafter", "turnoverbefore", "turnoverafter", "transition"];

    public static string NormalizeCustomers(string? sortBy)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key is not null && CustomerKeys.Contains(key) ? key : CustomersDefault;
    }
}
