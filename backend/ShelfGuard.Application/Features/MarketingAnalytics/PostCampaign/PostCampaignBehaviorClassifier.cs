using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics.PostCampaign;

/// <summary>
/// Pure, unit-tested classification of one segment member's before/after behavior (TASK-472,
/// `docs/uployal/AUDIENCE_ANALYSIS.md` §11.1-§11.6/§36.4/§36.7). Zero DB/EF/HTTP dependencies —
/// the caller (PostCampaignService) already knows each customer's purchase count in the before
/// and after windows; this class only applies the business rule on top of those two numbers.
///
/// Every rate method returns <c>null</c>, never <c>0m</c>, when its denominator is zero — the
/// source doc's explicit §36.7 requirement ("Не можна безумовно показувати 0,0% там, де
/// математично результат undefined"). Callers must render null as "н/д"/"—", never as "0%".
/// </summary>
public static class PostCampaignBehaviorClassifier
{
    /// <summary>
    /// (reactivated, retained, dropped, not_returned) always partition the full matched segment —
    /// callers assert <c>reactivated + retained + dropped + notReturned == MatchedCount</c> as a
    /// balance identity (brief's §36.4 requirement), which holds by construction here since the
    /// 2x2 truth table below is exhaustive and mutually exclusive.
    /// </summary>
    public static PostCampaignBehaviorStatus Classify(int purchaseCountBefore, int purchaseCountAfter) =>
        (purchaseCountBefore > 0, purchaseCountAfter > 0) switch
        {
            (false, true) => PostCampaignBehaviorStatus.Reactivated,
            (true, true) => PostCampaignBehaviorStatus.Retained,
            (true, false) => PostCampaignBehaviorStatus.Dropped,
            (false, false) => PostCampaignBehaviorStatus.NotReturned,
        };

    /// <summary>reactivated / inactive_before, where inactive_before = reactivated + not_returned
    /// (source doc §11.2). Null when there were no inactive-before customers at all — never 0%.</summary>
    public static decimal? ReactivationRatePercent(int reactivatedCount, int inactiveBeforeCount) =>
        inactiveBeforeCount <= 0 ? null : RoundPercent(reactivatedCount, inactiveBeforeCount);

    /// <summary>retained / active_before, where active_before = retained + dropped (source doc
    /// §11.3). Null when there were no active-before customers at all — never 0%.</summary>
    public static decimal? RetentionRatePercent(int retainedCount, int activeBeforeCount) =>
        activeBeforeCount <= 0 ? null : RoundPercent(retainedCount, activeBeforeCount);

    /// <summary>dropped / active_before — the churn-rate KPI the source doc explicitly flags as
    /// MISSING from the competitor's top cards (§31.9); added here as a deliberate improvement.</summary>
    public static decimal? ChurnRatePercent(int droppedCount, int activeBeforeCount) =>
        activeBeforeCount <= 0 ? null : RoundPercent(droppedCount, activeBeforeCount);

    /// <summary>(moneyAfter / moneyBefore - 1) * 100 (source doc §11.1/§20). Null when
    /// moneyBefore is zero — never 0%, and never a divide-by-zero.</summary>
    public static decimal? MoneyDeltaPercent(decimal moneyBefore, decimal moneyAfter) =>
        moneyBefore <= 0m ? null : Math.Round((moneyAfter / moneyBefore - 1m) * 100m, 2);

    /// <summary>Generic (after/before - 1) * 100 for any other before/after KPI pair (checks,
    /// turnover, average check, recency) — same null-not-zero rule as <see cref="MoneyDeltaPercent"/>.</summary>
    public static decimal? PercentDelta(decimal? before, decimal? after)
    {
        if (before is null || after is null || before.Value <= 0m) return null;
        return Math.Round((after.Value / before.Value - 1m) * 100m, 2);
    }

    private static decimal RoundPercent(int numerator, int denominator) =>
        Math.Round(numerator / (decimal)denominator * 100m, 2);
}
