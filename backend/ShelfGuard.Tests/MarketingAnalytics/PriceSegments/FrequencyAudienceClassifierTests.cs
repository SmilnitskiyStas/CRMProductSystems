using ShelfGuard.Application.Features.MarketingAnalytics.PriceSegments;
using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics.PriceSegments;

/// <summary>
/// TASK-420 design doc §1.8 / analysis doc §22.8 / QA §29.6 — every boundary case the analysis
/// doc's own QA checklist calls out by name: previous=0 -> Growing (not a div-by-zero special
/// case); decline of EXACTLY the threshold percent counts as Declining (&gt;=, never &gt;); a
/// hair under threshold does not.
/// </summary>
public sealed class FrequencyAudienceClassifierTests
{
    private const decimal DefaultThreshold = 30m;

    [Fact]
    public void Sleeping_when_previous_positive_and_current_zero() =>
        Assert.Equal(FrequencyAudienceKey.Sleeping, FrequencyAudienceClassifier.Classify(previousFrequency: 5, currentFrequency: 0, DefaultThreshold));

    [Fact]
    public void Growing_when_previous_zero_and_current_positive_not_a_divide_by_zero_special_case() =>
        Assert.Equal(FrequencyAudienceKey.Growing, FrequencyAudienceClassifier.Classify(previousFrequency: 0, currentFrequency: 52, DefaultThreshold));

    [Fact]
    public void Growing_when_current_exceeds_previous_both_positive() =>
        Assert.Equal(FrequencyAudienceKey.Growing, FrequencyAudienceClassifier.Classify(previousFrequency: 5, currentFrequency: 10, DefaultThreshold));

    [Fact]
    public void Declining_when_decline_percent_is_exactly_the_threshold()
    {
        // 10 -> 7 is exactly a 30% decline.
        var result = FrequencyAudienceClassifier.Classify(previousFrequency: 10, currentFrequency: 7, declineThresholdPercent: 30m);
        Assert.Equal(FrequencyAudienceKey.Declining, result);
    }

    [Fact]
    public void Other_when_decline_percent_is_a_hair_under_the_threshold()
    {
        // 10 -> 8 is a 20% decline (< 30% threshold) -> Other, not Declining.
        var result = FrequencyAudienceClassifier.Classify(previousFrequency: 10, currentFrequency: 8, declineThresholdPercent: 30m);
        Assert.Equal(FrequencyAudienceKey.Other, result);
    }

    [Theory]
    [InlineData(1000, 701, 30)] // 29.9% decline -> just under 30% threshold -> Other
    [InlineData(1000, 700, 30)] // exactly 30% -> Declining
    public void Declining_threshold_is_precise_to_the_tenth_of_a_percent(int previous, int current, double thresholdPercent)
    {
        var expected = current == 700 ? FrequencyAudienceKey.Declining : FrequencyAudienceKey.Other;
        Assert.Equal(expected, FrequencyAudienceClassifier.Classify(previous, current, (decimal)thresholdPercent));
    }

    [Fact]
    public void Other_when_frequency_unchanged()
    {
        var result = FrequencyAudienceClassifier.Classify(previousFrequency: 6, currentFrequency: 6, DefaultThreshold);
        Assert.Equal(FrequencyAudienceKey.Other, result);
    }

    [Fact]
    public void Declining_respects_a_custom_lower_threshold()
    {
        // 6.5 -> 2.3 style drop (design doc analysis §18 "50%" scenario) — using ints here since
        // the classifier's contract is int frequencies; a 50% decline at a 50% threshold is
        // Declining.
        var result = FrequencyAudienceClassifier.Classify(previousFrequency: 10, currentFrequency: 5, declineThresholdPercent: 50m);
        Assert.Equal(FrequencyAudienceKey.Declining, result);
    }
}
