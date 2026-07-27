using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;
using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics.PriceSegments;

/// <summary>
/// TASK-420 design doc §1.3/§22.4: a median check exactly equal to a boundary belongs to the
/// HIGHER tier (half-open interval [P_n, P_n+1)) — the exact boundary-equality cases below are
/// the ones the analysis doc's own QA checklist (§29.5) calls out by name ("Перевірити значення
/// точно 120, 190, 280, 440, 610 і 960").
/// </summary>
public sealed class PriceSegmentClassifierTests
{
    private static readonly PriceSegmentBoundariesRow Boundaries = new(
        P20: 120m, P40: 190m, P60: 280m, P80: 440m, P90: 610m, P97: 960m);

    [Theory]
    [InlineData(0, PriceSegmentKey.Tier1)]
    [InlineData(119.99, PriceSegmentKey.Tier1)]
    [InlineData(120, PriceSegmentKey.Tier2)] // exactly P20 -> higher tier
    [InlineData(189.99, PriceSegmentKey.Tier2)]
    [InlineData(190, PriceSegmentKey.Tier3)] // exactly P40
    [InlineData(279.99, PriceSegmentKey.Tier3)]
    [InlineData(280, PriceSegmentKey.Tier4)] // exactly P60
    [InlineData(439.99, PriceSegmentKey.Tier4)]
    [InlineData(440, PriceSegmentKey.Tier5)] // exactly P80
    [InlineData(609.99, PriceSegmentKey.Tier5)]
    [InlineData(610, PriceSegmentKey.Tier6)] // exactly P90
    [InlineData(959.99, PriceSegmentKey.Tier6)]
    [InlineData(960, PriceSegmentKey.Tier7)] // exactly P97
    [InlineData(5000, PriceSegmentKey.Tier7)] // open-ended top tier
    public void Classify_uses_half_open_intervals_boundary_belongs_to_higher_tier(double medianCheck, PriceSegmentKey expected) =>
        Assert.Equal(expected, PriceSegmentClassifier.Classify((decimal)medianCheck, Boundaries));

    [Fact]
    public void Classify_handles_all_zero_boundaries_without_throwing()
    {
        // Degenerate case: a brand-new tenant with zero all-time purchase history (GetBoundariesAsync
        // COALESCEs every percentile to 0 rather than null/throw) — every customer falls into the
        // open-ended top tier, never a crash.
        var zeroBoundaries = new PriceSegmentBoundariesRow(0, 0, 0, 0, 0, 0);
        Assert.Equal(PriceSegmentKey.Tier7, PriceSegmentClassifier.Classify(100m, zeroBoundaries));
        Assert.Equal(PriceSegmentKey.Tier7, PriceSegmentClassifier.Classify(0m, zeroBoundaries));
    }
}
