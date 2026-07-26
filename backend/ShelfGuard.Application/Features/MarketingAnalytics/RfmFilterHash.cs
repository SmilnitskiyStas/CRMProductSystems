using System.Security.Cryptography;
using System.Text;

namespace ShelfGuard.Application.Features.MarketingAnalytics;

/// <summary>
/// Deterministic hash of a normalized RFM filter object (tenant + stores + period), attached
/// to every marketing-analytics response as <c>FiltersHash</c> (plan requirement) so the
/// frontend/export file can prove which exact filter combination produced a given number —
/// React Query's own query-key already prevents most stale-render races, this is defense in
/// depth plus a traceable marker inside exported files.
/// Pure function — same inputs (store-id set order-independent) always produce the same hash.
/// </summary>
public static class RfmFilterHash
{
    public static string Compute(Guid tenantId, IReadOnlyList<Guid>? storeIds, DateOnly from, DateOnly to)
    {
        var normalizedStores = (storeIds ?? [])
            .Distinct()
            .OrderBy(id => id)
            .Select(id => id.ToString("N"));

        var raw = string.Join('|',
            tenantId.ToString("N"),
            from.ToString("yyyy-MM-dd"),
            to.ToString("yyyy-MM-dd"),
            string.Join(',', normalizedStores));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
