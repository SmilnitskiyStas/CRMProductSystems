using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;
using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments.Dtos;
using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics.PriceSegments;

/// <summary>
/// TASK-420: every branch of all 3 template dispatchers must resolve to non-empty
/// Тригер/Дія/Оффер/Застереження text without throwing — mirrors the shape of Фаза 1's
/// RecommendationTemplatesTests (that file wasn't available to read directly here, so this
/// follows the same "one assertion group per enum value, plus a total-function sweep" shape
/// PriceAudienceClassifierTests above already establishes for its own enum).
/// </summary>
public sealed class PriceSegmentRecommendationTemplatesTests
{
    [Theory]
    [InlineData(PriceAudienceKey.RealGrowth)]
    [InlineData(PriceAudienceKey.PriceGrowth)]
    [InlineData(PriceAudienceKey.Declining)]
    [InlineData(PriceAudienceKey.Stable)]
    public void BuildPriceAudience_returns_non_empty_text_for_every_audience(PriceAudienceKey key)
    {
        var input = new PriceAudienceRecommendationInputDto(key, CustomerCount: 5529, SharePercentOfAnalyzed: 23m, AverageLtv: 45436m);
        var result = PriceSegmentRecommendationTemplates.BuildPriceAudience(input);

        Assert.False(string.IsNullOrWhiteSpace(result.TriggerUa));
        Assert.False(string.IsNullOrWhiteSpace(result.ActionUa));
        Assert.False(string.IsNullOrWhiteSpace(result.OfferUa));
        Assert.False(string.IsNullOrWhiteSpace(result.CautionUa));
    }

    [Fact]
    public void BuildPriceAudience_stable_gives_full_parity_text_not_a_placeholder()
    {
        // Design brief item 6: Stable gets FULL parity with the other three, not a stub —
        // sanity check it produces the real Stable-specific narrative (design doc section 3's
        // exact "залишаються у своєму звичному ціновому сегменті" framing), not generic filler.
        // Deliberately not asserting on the formatted customer count here — ToString("N0") is
        // culture-sensitive (thousands separator varies by thread culture).
        var input = new PriceAudienceRecommendationInputDto(PriceAudienceKey.Stable, 9230, 37.8m, 28000m);
        var result = PriceSegmentRecommendationTemplates.BuildPriceAudience(input);
        Assert.Contains("звичному ціновому сегменті", result.TriggerUa, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PriceSegmentKey.Tier1)]
    [InlineData(PriceSegmentKey.Tier2)]
    [InlineData(PriceSegmentKey.Tier3)]
    [InlineData(PriceSegmentKey.Tier4)]
    [InlineData(PriceSegmentKey.Tier5)]
    [InlineData(PriceSegmentKey.Tier6)]
    [InlineData(PriceSegmentKey.Tier7)]
    public void BuildAllTimeSegment_returns_non_empty_text_for_every_tier(PriceSegmentKey key)
    {
        var input = new AllTimeSegmentRecommendationInputDto(key, RangeLabelUa: "120-190 грн", CustomerCount: 21988, AverageLtv: 17929m);
        var result = PriceSegmentRecommendationTemplates.BuildAllTimeSegment(input);

        Assert.False(string.IsNullOrWhiteSpace(result.TriggerUa));
        Assert.False(string.IsNullOrWhiteSpace(result.ActionUa));
        Assert.False(string.IsNullOrWhiteSpace(result.OfferUa));
        Assert.False(string.IsNullOrWhiteSpace(result.CautionUa));
        Assert.Contains("120-190 грн", result.TriggerUa, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FrequencyAudienceKey.Sleeping)]
    [InlineData(FrequencyAudienceKey.Declining)]
    [InlineData(FrequencyAudienceKey.Growing)]
    [InlineData(FrequencyAudienceKey.Other)]
    public void BuildFrequencyAudience_returns_non_empty_text_for_every_audience(FrequencyAudienceKey key)
    {
        var input = new FrequencyAudienceRecommendationInputDto(
            key, CustomerCount: 6235, SharePercent: 16.8m, AverageLtv: 17305m,
            AverageFrequencyPrevious: 2.2m, AverageFrequencyCurrent: 0m);
        var result = PriceSegmentRecommendationTemplates.BuildFrequencyAudience(input);

        Assert.False(string.IsNullOrWhiteSpace(result.TriggerUa));
        Assert.False(string.IsNullOrWhiteSpace(result.ActionUa));
        Assert.False(string.IsNullOrWhiteSpace(result.OfferUa));
        Assert.False(string.IsNullOrWhiteSpace(result.CautionUa));
    }
}
