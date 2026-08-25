namespace ShelfGuard.Application.Features.WriteOffs;

/// <summary>
/// Allowlist source of truth for <c>GET api/write-offs</c>'s <c>sortBy</c> query param
/// (TASK-630). Same shape as <c>PostCampaignSortKeys</c>/<c>PriceSegmentSortKeys</c> — an
/// unrecognized/omitted value silently normalizes to the default rather than throwing (sorting
/// is a display nicety, never worth a 400). The raw <c>sortBy</c> string is only ever compared
/// against this fixed set here and in <c>WriteOffRepository</c>'s OrderBy switch — never used to
/// build an expression dynamically.
/// </summary>
public static class WriteOffSortKeys
{
    public const string Default = "createdat";

    private static readonly HashSet<string> Keys =
        ["createdat", "status", "reason", "netloss"];

    public static string Normalize(string? sortBy)
    {
        var key = sortBy?.Trim().ToLowerInvariant();
        return key is not null && Keys.Contains(key) ? key : Default;
    }
}
