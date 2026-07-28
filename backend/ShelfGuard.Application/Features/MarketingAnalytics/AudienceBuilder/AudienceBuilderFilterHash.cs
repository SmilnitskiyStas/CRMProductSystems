using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ShelfGuard.Application.Features.MarketingAnalytics.AudienceBuilder;

/// <summary>
/// Deterministic hash of a normalized AudienceBuilder filter combination, attached to every
/// response as <c>FiltersHash</c> — same purpose as <c>MarketingAnalytics.RfmFilterHash</c> /
/// <c>PriceSegments.PriceSegmentFilterHash</c> (design doc §7 item 10: "атомарне
/// filters_hash-версіонування (уже конвенція Фази 1)"). Kept as its own small copy rather than
/// reusing <c>PriceSegments.PriceSegmentFilterHash</c> (which is public and technically callable
/// from here) — reaching into a sibling FEATURE's namespace for a generic utility would be an odd,
/// undocumented cross-feature dependency; this keeps AudienceBuilder's only intentional
/// cross-feature reference the shared <c>PiiMasking</c> helper (declared one namespace up, in the
/// common <c>MarketingAnalytics</c> parent, explicitly meant to be shared). Pure function — same
/// inputs (store-id set order-independent) always produce the same hash.
/// </summary>
public static class AudienceBuilderFilterHash
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
