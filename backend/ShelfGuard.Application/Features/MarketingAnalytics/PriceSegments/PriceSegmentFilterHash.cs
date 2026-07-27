using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;

/// <summary>
/// Deterministic hash of a normalized Фаза 2 filter combination, attached to every price-segments
/// response as <c>FiltersHash</c> — same purpose as <see cref="MarketingAnalytics.RfmFilterHash"/>
/// (design doc §7 item 10: "атомарне filters_hash-версіонування (уже конвенція Фази 1)"), but
/// generalized to a variadic <paramref name="extra"/> tail instead of a fixed (from, to) pair —
/// Фаза 2 has three shapes of filter (comparison window, all-time + optional tier, frequency +
/// threshold/spend/tier) that don't share one fixed parameter list. Pure function — same inputs
/// (store-id set order-independent) always produce the same hash.
/// </summary>
public static class PriceSegmentFilterHash
{
    public static string Compute(Guid tenantId, IReadOnlyList<Guid>? storeIds, params object?[] extra)
    {
        var normalizedStores = (storeIds ?? [])
            .Distinct()
            .OrderBy(id => id)
            .Select(id => id.ToString("N"));

        var raw = string.Join('|',
            tenantId.ToString("N"),
            string.Join(',', normalizedStores),
            string.Join('|', extra.Select(FormatExtra)));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string FormatExtra(object? value) => value switch
    {
        null => "null",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal dec => dec.ToString(CultureInfo.InvariantCulture),
        IEnumerable<Guid> guids => string.Join(',', guids.Distinct().OrderBy(g => g).Select(g => g.ToString("N"))),
        _ => value.ToString() ?? "null",
    };
}
