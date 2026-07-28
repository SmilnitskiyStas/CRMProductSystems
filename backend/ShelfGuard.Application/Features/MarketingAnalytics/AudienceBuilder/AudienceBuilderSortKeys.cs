namespace ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;

/// <summary>
/// Allowlist source of truth for every AudienceBuilder paginated table's <c>sortBy</c> query
/// param (design doc §7: real server-side sorting, allowlisted columns only). Used by BOTH
/// <see cref="AudienceBuilderService"/> (to echo the actually-applied key back in the response
/// DTO's <c>SortBy</c> field) and the Infrastructure repository (to map the same normalized key
/// to an actual SQL column expression) — sharing one allowlist keeps the two from drifting out of
/// sync, and guarantees the raw <c>sortBy</c> string a caller sends is NEVER concatenated into
/// SQL: only ever compared against this fixed, hardcoded set. Same shape as
/// <c>PriceSegments.PriceSegmentSortKeys</c> (not reused directly — see
/// <see cref="AudienceBuilderFilterHash"/>'s own doc for why this feature keeps its own small
/// copies rather than reaching into a sibling feature's namespace).
///
/// An unrecognized/omitted value silently normalizes to that table's default rather than
/// throwing — sorting is a display nicety, never worth a 400. Buyers/competitor-buyers default to
/// "qty" descending (analysis doc §14.2: the competitor's own base state sorts by "Куплено, шт"
/// descending); matched-items defaults to "sold" descending (analysis doc §20.1: "Список
/// ранжується за продажами у вибраному періоді").
/// </summary>
public static class AudienceBuilderSortKeys
{
    public const string BuyersDefault = "qty";
    public const string MatchedItemsDefault = "sold";
    public const string CompetitorBuyersDefault = "qty";

    private static readonly HashSet<string> BuyerKeys = ["name", "qty", "receipts", "amount"];
    private static readonly HashSet<string> MatchedItemKeys = ["name", "sold", "receipts", "buyers"];

    public static string NormalizeBuyers(string? sortBy) => Normalize(sortBy, BuyerKeys, BuyersDefault);

    public static string NormalizeMatchedItems(string? sortBy) => Normalize(sortBy, MatchedItemKeys, MatchedItemsDefault);

    public static string NormalizeCompetitorBuyers(string? sortBy) => Normalize(sortBy, BuyerKeys, CompetitorBuyersDefault);

    private static string Normalize(string? sortBy, HashSet<string> allowed, string fallback)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key is not null && allowed.Contains(key) ? key : fallback;
    }
}
