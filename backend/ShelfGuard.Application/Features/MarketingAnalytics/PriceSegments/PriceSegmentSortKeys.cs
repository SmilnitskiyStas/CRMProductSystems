namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;

/// <summary>
/// Single allowlist source of truth for every Фаза 2 paginated table's <c>sortBy</c> query
/// param (design doc §2/§6: real server-side sorting, allowlisted columns only). Used by BOTH
/// <see cref="PriceSegmentsService"/> (to echo the actually-applied key back in the response
/// DTO's <c>SortBy</c> field) and the Infrastructure repository (to map the same normalized key
/// to an actual SQL column expression) — sharing one allowlist keeps the two from drifting out
/// of sync, and guarantees the raw <paramref name="sortBy"/> string a caller sends is NEVER
/// concatenated into SQL: only ever compared against this fixed, hardcoded set.
///
/// An unrecognized/omitted value silently normalizes to that table's default rather than
/// throwing — sorting is a display nicety, never worth a 400. The comparison table's confirmed
/// competitor default is "typical check, descending" (analysis doc §9.2); the all-time table
/// follows the same convention for consistency; the frequency table defaults to the biggest
/// negative change first ("delta", descending would put growth first — see
/// PriceSegmentsRepository's remarks for the exact ORDER BY this produces).
/// </summary>
public static class PriceSegmentSortKeys
{
    public const string PriceAudienceDefault = "check";
    public const string AllTimeDefault = "check";
    public const string FrequencyDefault = "delta";

    private static readonly HashSet<string> PriceAudienceKeys = ["name", "segment", "items", "check", "ltv"];
    private static readonly HashSet<string> AllTimeKeys = ["name", "segment", "items", "check", "purchases", "ltv"];
    private static readonly HashSet<string> FrequencyKeys = ["name", "previous", "current", "delta", "check", "spend", "ltv"];

    public static string NormalizePriceAudience(string? sortBy) => Normalize(sortBy, PriceAudienceKeys, PriceAudienceDefault);

    public static string NormalizeAllTime(string? sortBy) => Normalize(sortBy, AllTimeKeys, AllTimeDefault);

    public static string NormalizeFrequency(string? sortBy) => Normalize(sortBy, FrequencyKeys, FrequencyDefault);

    private static string Normalize(string? sortBy, HashSet<string> allowed, string fallback)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key is not null && allowed.Contains(key) ? key : fallback;
    }
}
