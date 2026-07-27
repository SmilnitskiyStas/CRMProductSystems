using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;

/// <summary>
/// Pure, unit-tested classification of a customer's comparison-window transition into exactly
/// one of the 4 <see cref="PriceAudienceKey"/> buckets (TASK-420 design doc §1.6 /
/// docs/uployal/PRICE_SEGMENTS_ANALYSIS.md §7 / §22.7). A TOTAL function on its 4 inputs — every
/// customer in the comparison cohort (bought in both current and previous windows) gets exactly
/// one bucket, including <see cref="PriceAudienceKey.Stable"/> (design brief item 6: full parity
/// with the other three, unlike the competitor which never lists/exports it).
///
/// "Через ціни" (<see cref="PriceAudienceKey.PriceGrowth"/>) is the segment-rose-but-items-flat
/// case — equality counts as "через ціни" too (design doc §1.6: "рівність теж 'через ціни'"), so
/// <see cref="PriceAudienceKey.RealGrowth"/> requires a STRICT items/receipt increase, never
/// <c>&gt;=</c>.
///
/// Kept in sync with SQL: <c>PriceSegmentsRepository</c>'s comparison-audience paginated query
/// inlines this exact 3-branch decision as a SQL <c>CASE</c> over the two segment ordinals + two
/// items-per-receipt values it already computed — see that class's remarks.
/// </summary>
public static class PriceAudienceClassifier
{
    public static PriceAudienceKey Classify(
        PriceSegmentKey previousSegment,
        PriceSegmentKey currentSegment,
        decimal itemsPerReceiptPrevious,
        decimal itemsPerReceiptCurrent)
    {
        if (currentSegment > previousSegment)
            return itemsPerReceiptCurrent > itemsPerReceiptPrevious
                ? PriceAudienceKey.RealGrowth
                : PriceAudienceKey.PriceGrowth;

        if (currentSegment < previousSegment)
            return PriceAudienceKey.Declining;

        return PriceAudienceKey.Stable;
    }
}
