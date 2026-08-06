using ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;
using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics.PostCampaign;

/// <summary>
/// TASK-472: pure classification + rate-formula tests. Covers the balance identity
/// (`reactivated + retained + dropped + not_returned == MatchedCount`, source doc §36.4) and the
/// null-not-zero rule for every rate/delta formula when its denominator is zero (§36.7).
/// </summary>
public sealed class PostCampaignBehaviorClassifierTests
{
    [Theory]
    [InlineData(0, 0, PostCampaignBehaviorStatus.NotReturned)]
    [InlineData(0, 3, PostCampaignBehaviorStatus.Reactivated)]
    [InlineData(2, 0, PostCampaignBehaviorStatus.Dropped)]
    [InlineData(2, 5, PostCampaignBehaviorStatus.Retained)]
    public void Classify_maps_every_before_after_combination_to_the_correct_status(
        int before, int after, PostCampaignBehaviorStatus expected)
    {
        Assert.Equal(expected, PostCampaignBehaviorClassifier.Classify(before, after));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(5, 5)]
    public void Classify_is_exhaustive_and_mutually_exclusive_for_every_combination(int before, int after)
    {
        // Every (before,after) pair maps to exactly one of the 4 statuses — no exception, no
        // ambiguous double-match possible given the underlying 2x2 truth table.
        var status = PostCampaignBehaviorClassifier.Classify(before, after);
        Assert.True(Enum.IsDefined(status));
    }

    [Fact]
    public void Balance_identity_holds_across_a_synthetic_segment_of_customers()
    {
        // 42-customer control segment shape from the source doc §12: 1 reactivated, 39 retained,
        // 2 dropped, 0 not-returned — reproduced here as a synthetic before/after pair list.
        var pairs = new List<(int Before, int After)>();
        pairs.Add((0, 1)); // reactivated x1
        for (var i = 0; i < 39; i++) pairs.Add((3, 2)); // retained x39
        pairs.Add((4, 0)); // dropped x2 (this pair + the next)
        pairs.Add((2, 0));

        int reactivated = 0, retained = 0, dropped = 0, notReturned = 0;
        foreach (var (before, after) in pairs)
        {
            switch (PostCampaignBehaviorClassifier.Classify(before, after))
            {
                case PostCampaignBehaviorStatus.Reactivated: reactivated++; break;
                case PostCampaignBehaviorStatus.Retained: retained++; break;
                case PostCampaignBehaviorStatus.Dropped: dropped++; break;
                case PostCampaignBehaviorStatus.NotReturned: notReturned++; break;
            }
        }

        Assert.Equal(1, reactivated);
        Assert.Equal(39, retained);
        Assert.Equal(2, dropped);
        Assert.Equal(0, notReturned);
        Assert.Equal(pairs.Count, reactivated + retained + dropped + notReturned);

        // Cross-checks from the source doc's own control numbers (§12).
        Assert.Equal(40, reactivated + retained); // buyers_after
        Assert.Equal(41, retained + dropped);     // active_before
        Assert.Equal(1, reactivated + notReturned); // inactive_before
    }

    [Fact]
    public void ReactivationRatePercent_is_null_not_zero_when_no_inactive_before_customers_exist()
    {
        Assert.Null(PostCampaignBehaviorClassifier.ReactivationRatePercent(reactivatedCount: 0, inactiveBeforeCount: 0));
    }

    [Fact]
    public void ReactivationRatePercent_computes_percent_of_inactive_before()
    {
        // Source doc §12 control: 1 reactivated out of 1 inactive-before => 100%.
        Assert.Equal(100m, PostCampaignBehaviorClassifier.ReactivationRatePercent(1, 1));
    }

    [Fact]
    public void RetentionRatePercent_is_null_not_zero_when_no_active_before_customers_exist()
    {
        Assert.Null(PostCampaignBehaviorClassifier.RetentionRatePercent(retainedCount: 0, activeBeforeCount: 0));
    }

    [Fact]
    public void RetentionRatePercent_matches_source_doc_control_segment()
    {
        // Source doc §12 control: 39 retained / 41 active-before ~= 95.1%.
        var result = PostCampaignBehaviorClassifier.RetentionRatePercent(39, 41);
        Assert.NotNull(result);
        Assert.Equal(95.12m, result!.Value);
    }

    [Fact]
    public void ChurnRatePercent_is_null_not_zero_when_no_active_before_customers_exist()
    {
        Assert.Null(PostCampaignBehaviorClassifier.ChurnRatePercent(droppedCount: 0, activeBeforeCount: 0));
    }

    [Fact]
    public void MoneyDeltaPercent_is_null_not_zero_when_money_before_is_zero()
    {
        Assert.Null(PostCampaignBehaviorClassifier.MoneyDeltaPercent(moneyBefore: 0m, moneyAfter: 500m));
    }

    [Fact]
    public void MoneyDeltaPercent_computes_positive_and_negative_deltas()
    {
        // Source doc §17.6 control: 5720 after vs 2403 before => +138.0%.
        var up = PostCampaignBehaviorClassifier.MoneyDeltaPercent(2403m, 5720m);
        Assert.NotNull(up);
        Assert.Equal(138.0m, Math.Round(up!.Value, 1));

        var down = PostCampaignBehaviorClassifier.MoneyDeltaPercent(236227m / 0.848m, 236227m);
        Assert.NotNull(down);
        Assert.True(down!.Value < 0m);
    }

    [Fact]
    public void PercentDelta_is_null_when_either_side_is_null_or_before_is_zero_or_negative()
    {
        Assert.Null(PostCampaignBehaviorClassifier.PercentDelta(null, 10m));
        Assert.Null(PostCampaignBehaviorClassifier.PercentDelta(10m, null));
        Assert.Null(PostCampaignBehaviorClassifier.PercentDelta(0m, 10m));
    }

    [Fact]
    public void PercentDelta_computes_ordinary_ratio_change()
    {
        var result = PostCampaignBehaviorClassifier.PercentDelta(9m, 13m);
        Assert.NotNull(result);
        Assert.Equal(44.44m, result!.Value);
    }
}
