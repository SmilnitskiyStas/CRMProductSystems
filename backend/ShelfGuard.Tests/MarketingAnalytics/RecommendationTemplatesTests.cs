using ShelfGuard.Application.Features.MarketingAnalytics;
using ShelfGuard.Application.Features.MarketingAnalytics.Dtos;
using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics;

/// <summary>
/// TASK-406: RecommendationTemplates.Build — every segment must produce non-empty, distinct
/// copy (RFM_ANALYSIS.md §14.3 / QA checklist: "рекомендації мають відрізнятися за сегментами"),
/// and live KPIs must actually be substituted into the text (not a static, input-independent
/// string), plus a spot-check that Champions never recommends a discount (§14.1's explicit
/// "без знижки" rule) while Lost/Hibernating/AtRisk do talk about return/reactivation offers.
/// </summary>
public sealed class RecommendationTemplatesTests
{
    private static readonly RfmRecommendationInputDto SampleInput = new(
        CustomerCount: 1234,
        SharePercentOfPeriodCustomers: 24.3m,
        SharePercentOfPeriodRevenue: 67.5m,
        AverageRecencyDays: 3m,
        AverageLtv: 80320m,
        PeakDayOfWeekIso: 5,
        PeakHour: 18,
        TopProductNames: ["Банан ваговий", "Огірок колючий", "Цибуля ріпчаста"],
        TopProductName: "Банан ваговий",
        TopProductCoveragePercent: 61.3m);

    public static IEnumerable<object[]> AllSegmentKeys() =>
        RfmSegmentCatalog.AllKeysInPriorityOrder.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(AllSegmentKeys))]
    public void Build_returns_non_empty_text_in_every_block_for_every_segment(RfmSegmentKey key)
    {
        var rec = RecommendationTemplates.Build(key, SampleInput);

        Assert.False(string.IsNullOrWhiteSpace(rec.TriggerUa));
        Assert.False(string.IsNullOrWhiteSpace(rec.ActionUa));
        Assert.False(string.IsNullOrWhiteSpace(rec.OfferUa));
        Assert.False(string.IsNullOrWhiteSpace(rec.CautionUa));
        Assert.Equal(SampleInput.TopProductNames, rec.ProductsForPromo);
    }

    [Fact]
    public void Every_segment_produces_distinct_trigger_text()
    {
        var triggers = RfmSegmentCatalog.AllKeysInPriorityOrder
            .Select(k => RecommendationTemplates.Build(k, SampleInput).TriggerUa)
            .ToList();

        Assert.Equal(triggers.Count, triggers.Distinct().Count());
    }

    [Fact]
    public void Champions_recommendation_explicitly_rules_out_a_discount()
    {
        // RFM_ANALYSIS.md §14.1: Champions' offer is explicitly "без знижки" (no discount) —
        // the word itself naturally appears (to rule it out), so this checks for the explicit
        // "без знижки" phrase rather than absence of the word "знижка" altogether.
        var rec = RecommendationTemplates.Build(RfmSegmentKey.Champions, SampleInput);

        Assert.Contains("без знижки", rec.OfferUa, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RfmSegmentKey.CannotLoseThem)]
    [InlineData(RfmSegmentKey.AtRisk)]
    [InlineData(RfmSegmentKey.Lost)]
    public void Retention_segments_reference_bringing_the_customer_back(RfmSegmentKey key)
    {
        var rec = RecommendationTemplates.Build(key, SampleInput);
        var combined = rec.TriggerUa + rec.ActionUa + rec.OfferUa;

        Assert.True(
            combined.Contains("поверн", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("відтік", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("win-back", StringComparison.OrdinalIgnoreCase),
            $"Expected {key}'s recommendation to reference return/reactivation, got: {combined}");
    }

    [Fact]
    public void Live_kpis_are_actually_substituted_not_a_static_string()
    {
        var lowInput = SampleInput with { CustomerCount = 5, SharePercentOfPeriodRevenue = 1.1m };
        var highInput = SampleInput with { CustomerCount = 99999, SharePercentOfPeriodRevenue = 88.8m };

        var low = RecommendationTemplates.Build(RfmSegmentKey.Champions, lowInput);
        var high = RecommendationTemplates.Build(RfmSegmentKey.Champions, highInput);

        Assert.NotEqual(low.TriggerUa, high.TriggerUa);
    }

    [Fact]
    public void Build_throws_for_an_undefined_segment_key()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecommendationTemplates.Build((RfmSegmentKey)999, SampleInput));
    }
}
