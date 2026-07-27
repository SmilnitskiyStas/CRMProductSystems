using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;

/// <summary>
/// Pure, unit-tested classification of a customer's purchase-count transition into exactly one
/// of the 4 <see cref="FrequencyAudienceKey"/> buckets (TASK-420 design doc §1.8 /
/// docs/uployal/PRICE_SEGMENTS_ANALYSIS.md §17 / §22.8 / QA §29.6). Population is the UNION of
/// current+previous buyers (see <see cref="FrequencyAudienceKey"/>'s own doc), so
/// <paramref name="previousFrequency"/>/<paramref name="currentFrequency"/> may each legitimately
/// be 0 (never both — a customer with zero purchases in BOTH windows was never a member of the
/// union population to begin with, and callers must not invoke this for one).
///
/// QA-verified boundary cases (analysis doc §29.6, all three asserted in the unit tests):
/// previous=0 → <see cref="FrequencyAudienceKey.Growing"/> (not a divide-by-zero special case —
/// falls out of the plain <c>current &gt; previous</c> comparison); decline of EXACTLY the
/// threshold percent counts as <see cref="FrequencyAudienceKey.Declining"/> (<c>&gt;=</c>, never
/// <c>&gt;</c>); a hair under threshold does not.
///
/// Kept in sync with SQL: <c>PriceSegmentsRepository</c>'s frequency-audience paginated query
/// inlines this exact ladder (including the decline-percent arithmetic) as a SQL <c>CASE</c> —
/// see that class's remarks.
/// </summary>
public static class FrequencyAudienceClassifier
{
    public static FrequencyAudienceKey Classify(int previousFrequency, int currentFrequency, decimal declineThresholdPercent)
    {
        if (previousFrequency > 0 && currentFrequency == 0)
            return FrequencyAudienceKey.Sleeping;

        if (currentFrequency > previousFrequency)
            return FrequencyAudienceKey.Growing; // includes previousFrequency == 0

        if (currentFrequency > 0 && currentFrequency < previousFrequency)
        {
            var declinePct = (previousFrequency - currentFrequency) / (decimal)previousFrequency * 100m;
            if (declinePct >= declineThresholdPercent)
                return FrequencyAudienceKey.Declining;
        }

        return FrequencyAudienceKey.Other;
    }
}
