namespace ShelfGuard.Application.Features.Stock;

/// <summary>
/// Allowlist source of truth for <c>GET api/stock</c>'s <c>sortBy</c> query param (TASK-630).
/// Same shape as <c>PostCampaignSortKeys</c>/<c>PriceSegmentSortKeys</c> — an unrecognized/
/// omitted value silently normalizes to the default rather than throwing (sorting is a display
/// nicety, never worth a 400). The raw <c>sortBy</c> string is only ever compared against this
/// fixed set here and in <c>StockRepository</c>'s OrderBy switch — never used to build an
/// expression dynamically.
///
/// Default is <c>"expirydate"</c> — the FEFO-relevant natural order (nearest-expiry-first,
/// ascending) that <c>StockRepository.GetPagedAsync</c> already applied implicitly before this
/// task. See <c>StockRepository</c>'s paging method for how the default direction is preserved
/// when the caller omits <c>sortDescending</c>.
/// </summary>
public static class StockSortKeys
{
    public const string Default = "expirydate";

    private static readonly HashSet<string> Keys =
        ["expirydate", "productname", "quantity", "status"];

    public static string Normalize(string? sortBy)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key is not null && Keys.Contains(key) ? key : Default;
    }
}
