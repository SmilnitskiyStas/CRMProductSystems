using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;

/// <summary>
/// Network-wide quantile boundaries a single customer's median check is classified against —
/// P20/P40/P60/P80/P90/P97 of the ALL-TIME distribution of every customer's own median check
/// (TASK-420 design doc §1.2), recomputed live on every request, never cached. Deliberately the
/// SAME 6 numbers regardless of which comparison window (30/60/90/custom) is active — the
/// competitor's own boundaries stayed identical across every window tested (design doc §1.2 /
/// docs/uployal/PRICE_SEGMENTS_ANALYSIS.md §8.3), proving they're network-scoped, not window-scoped.
/// </summary>
public sealed record PriceSegmentBoundariesRow(decimal P20, decimal P40, decimal P60, decimal P80, decimal P90, decimal P97);

/// <summary>
/// Pure, unit-tested classification of one customer's median check into exactly one of the 7
/// <see cref="PriceSegmentKey"/> tiers (TASK-420 design doc §1.3). Mirrors
/// <see cref="MarketingAnalytics.RfmSegmentClassifier"/>'s shape: zero DB/EF/HTTP dependencies,
/// scores/boundaries are computed elsewhere (<c>PriceSegmentsRepository</c>, raw SQL
/// <c>PERCENTILE_CONT</c>) and handed in already-computed.
///
/// <b>A boundary belongs to the HIGHER tier</b> — half-open interval <c>[P_n, P_n+1)</c>, taken
/// literally from the design doc's pseudocode (§1.3/§22.4): a median check exactly equal to P20
/// is Tier2, not Tier1. This is a deliberate, documented product decision filling a gap the
/// competitor's own UI never disclosed (analysis doc §22.4/§30).
///
/// <b>Kept in sync with SQL:</b> <c>PriceSegmentsRepository</c>'s paginated table queries inline
/// this EXACT same 6-comparison ladder as a SQL <c>CASE</c> expression (using the same boundary
/// values as parameters) so audience-table queries can filter/sort/paginate at the database level
/// instead of classifying tens of thousands of rows in C# first (TASK-420 design doc §2's
/// "Фаза 1 ніколи не пагінувала" distinction). This class remains the single documented source of
/// truth for the RULE; any change here must be mirrored in that SQL. See
/// <c>PriceSegmentsRepository</c>'s class-level remarks for the exact query list.
/// </summary>
public static class PriceSegmentClassifier
{
    public static PriceSegmentKey Classify(decimal medianCheck, PriceSegmentBoundariesRow boundaries)
    {
        if (medianCheck < boundaries.P20) return PriceSegmentKey.Tier1;
        if (medianCheck < boundaries.P40) return PriceSegmentKey.Tier2;
        if (medianCheck < boundaries.P60) return PriceSegmentKey.Tier3;
        if (medianCheck < boundaries.P80) return PriceSegmentKey.Tier4;
        if (medianCheck < boundaries.P90) return PriceSegmentKey.Tier5;
        if (medianCheck < boundaries.P97) return PriceSegmentKey.Tier6;
        return PriceSegmentKey.Tier7;
    }
}
