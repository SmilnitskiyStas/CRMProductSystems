using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;
using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics.PriceSegments;

/// <summary>
/// TASK-420 design doc §1.6 / analysis doc §7 / §22.7. Key rule under test: "через ціни"
/// (<see cref="PriceAudienceKey.PriceGrowth"/>) is the catch-all for a segment-rose case that
/// isn't a STRICT items/receipt increase — equality counts as "через ціни" too (design doc:
/// "рівність теж 'через ціни'"), never <see cref="PriceAudienceKey.RealGrowth"/>.
/// </summary>
public sealed class PriceAudienceClassifierTests
{
    [Fact]
    public void RealGrowth_requires_segment_up_and_strictly_more_items_per_receipt() =>
        Assert.Equal(
            PriceAudienceKey.RealGrowth,
            PriceAudienceClassifier.Classify(PriceSegmentKey.Tier2, PriceSegmentKey.Tier4, itemsPerReceiptPrevious: 3.0m, itemsPerReceiptCurrent: 3.5m));

    [Fact]
    public void PriceGrowth_when_segment_up_but_items_per_receipt_unchanged()
    {
        // Design doc §1.6: "рівність теж 'через ціни'" — equality is NOT RealGrowth.
        var result = PriceAudienceClassifier.Classify(
            PriceSegmentKey.Tier2, PriceSegmentKey.Tier4, itemsPerReceiptPrevious: 3.0m, itemsPerReceiptCurrent: 3.0m);
        Assert.Equal(PriceAudienceKey.PriceGrowth, result);
    }

    [Fact]
    public void PriceGrowth_when_segment_up_and_items_per_receipt_fell()
    {
        var result = PriceAudienceClassifier.Classify(
            PriceSegmentKey.Tier1, PriceSegmentKey.Tier7, itemsPerReceiptPrevious: 5.0m, itemsPerReceiptCurrent: 1.0m);
        Assert.Equal(PriceAudienceKey.PriceGrowth, result);
    }

    [Theory]
    [InlineData(3.0, 3.5)] // items up doesn't matter — segment fell
    [InlineData(3.0, 2.0)]
    [InlineData(3.0, 3.0)]
    public void Declining_whenever_segment_falls_regardless_of_items_per_receipt(double prevIpr, double curIpr) =>
        Assert.Equal(
            PriceAudienceKey.Declining,
            PriceAudienceClassifier.Classify(PriceSegmentKey.Tier5, PriceSegmentKey.Tier2, (decimal)prevIpr, (decimal)curIpr));

    [Theory]
    [InlineData(3.0, 3.5)] // items changing doesn't matter — segment unchanged
    [InlineData(3.0, 2.0)]
    [InlineData(3.0, 3.0)]
    public void Stable_whenever_segment_unchanged_regardless_of_items_per_receipt(double prevIpr, double curIpr) =>
        Assert.Equal(
            PriceAudienceKey.Stable,
            PriceAudienceClassifier.Classify(PriceSegmentKey.Tier4, PriceSegmentKey.Tier4, (decimal)prevIpr, (decimal)curIpr));

    [Fact]
    public void Classify_is_a_total_function_every_tier_pair_yields_exactly_one_key()
    {
        // Design doc: "Тотальна функція на 4 виходи" — every combination must resolve without
        // throwing, and always to exactly one of the 4 keys.
        foreach (PriceSegmentKey previous in Enum.GetValues<PriceSegmentKey>())
        foreach (PriceSegmentKey current in Enum.GetValues<PriceSegmentKey>())
        {
            var result = PriceAudienceClassifier.Classify(previous, current, 1m, 1m);
            Assert.True(Enum.IsDefined(result));
        }
    }
}
