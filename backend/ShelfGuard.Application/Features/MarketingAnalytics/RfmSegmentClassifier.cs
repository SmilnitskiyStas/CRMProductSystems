using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Application.Features.MarketingAnalytics;

/// <summary>
/// Pure, unit-tested classification of one customer into exactly one of the 11
/// <see cref="RfmSegmentKey"/> segments (TASK-406, marketing-analytics plan §"Фаза 1",
/// approved-plan priority table). Deliberately has zero DB/EF/HTTP dependencies — the R/F/M
/// quintile scores and lifetime facts are computed by <c>MarketingAnalyticsRepository</c>
/// (raw SQL, <c>NTILE(5)</c>) and handed in already-scored; this class only ever applies the
/// business rule on top of numbers it's given.
///
/// Rule evaluation order below is the classification PRIORITY, not a display order — the
/// FIRST rule (top to bottom) whose condition is true wins, exactly as the approved plan's
/// table states ("перше правило, що підійшло, виграє"). This means, for example, a customer
/// who satisfies both Hibernating's (#9) condition and Lost's (#10) condition is classified
/// Hibernating, because it is checked first — an intentional consequence of following the
/// plan's literal table order, not a bug.
/// </summary>
public static class RfmSegmentClassifier
{
    // ── Named thresholds (never magic numbers — brief's explicit requirement, so future
    // admin-tunable thresholds are a small, localized change) ──────────────────────────────

    public const int ChampionsMinRecency = 4;
    public const int ChampionsMinFrequency = 4;
    public const int ChampionsMinMonetary = 4;

    public const int LoyalMinRecency = 3;
    public const int LoyalMinFrequency = 4;
    public const int LoyalMinMonetary = 3;

    public const int CannotLoseThemMaxRecency = 2;
    public const int CannotLoseThemMinFrequency = 4;
    public const int CannotLoseThemMinMonetary = 4;

    public const int AtRiskMaxRecency = 2;
    public const int AtRiskMinFrequency = 3;
    public const int AtRiskMinMonetary = 3;

    /// <summary>"Перша покупка ≤30 днів тому" — measured against the active period's end date
    /// (the query's `to`), not real wall-clock "today", so a custom historical period stays
    /// fully deterministic and re-computable (plan: "квінтилі рахуються заново на кожну
    /// комбінацію фільтрів").</summary>
    public const int NewMaxDaysSinceFirstPurchase = 30;

    public const int PotentialLoyalistMinRecency = 3;
    public const int PotentialLoyalistMinFrequencyInclusive = 2;
    public const int PotentialLoyalistMaxFrequencyInclusive = 3;
    public const int PotentialLoyalistMinMonetary = 2;

    public const int PromisingMinRecency = 4;
    public const int PromisingMaxFrequency = 2;
    public const int PromisingMaxMonetary = 2;

    public const int AboutToSleepMinRecencyInclusive = 2;
    public const int AboutToSleepMaxRecencyInclusive = 3;
    public const int AboutToSleepMaxFrequency = 2;

    public const int HibernatingMaxRecency = 2;
    public const int HibernatingMaxFrequency = 2;
    public const int HibernatingMaxMonetary = 2;

    /// <summary>"Лише 1 чек за все життя" — Lost is defined purely on lifetime facts, not R/F/M.</summary>
    public const int LostLifetimeReceiptCount = 1;
    /// <summary>"...і він &gt;6 міс. до кінця періоду".</summary>
    public const int LostMinMonthsSinceOnlyPurchase = 6;

    private const int MinScore = 1;
    private const int MaxScore = 5;

    /// <summary>
    /// Classifies one customer. <paramref name="recencyScore"/>/<paramref name="frequencyScore"/>/
    /// <paramref name="monetaryScore"/> are the 1-5 quintile scores computed over the active,
    /// filtered population (5 = best in every case: most recent / most frequent / highest
    /// spend). <paramref name="firstPurchaseDateEver"/>/<paramref name="lifetimeReceiptCount"/>/
    /// <paramref name="lastPurchaseDateEver"/> are LIFETIME facts (never windowed by the active
    /// period filter) — this is what lets "New"/"Lost" reason about tenure/one-off purchases
    /// independent of which period happens to be selected. <paramref name="periodEnd"/> is the
    /// active period's `to` date, the reference point for both date-based rules.
    ///
    /// Callers must only invoke this for a customer known to have at least one purchase in the
    /// active period (i.e. someone who actually received R/F/M scores) — a customer with zero
    /// lifetime transactions is the separate "no purchase" card and never reaches this method.
    /// </summary>
    public static RfmSegmentKey Classify(
        int recencyScore,
        int frequencyScore,
        int monetaryScore,
        DateOnly firstPurchaseDateEver,
        int lifetimeReceiptCount,
        DateOnly lastPurchaseDateEver,
        DateOnly periodEnd)
    {
        ValidateScore(recencyScore, nameof(recencyScore));
        ValidateScore(frequencyScore, nameof(frequencyScore));
        ValidateScore(monetaryScore, nameof(monetaryScore));
        if (lifetimeReceiptCount < 1)
            throw new ArgumentOutOfRangeException(
                nameof(lifetimeReceiptCount), lifetimeReceiptCount,
                "A customer being classified must have at least one lifetime purchase.");

        // #1 Champions
        if (recencyScore >= ChampionsMinRecency && frequencyScore >= ChampionsMinFrequency && monetaryScore >= ChampionsMinMonetary)
            return RfmSegmentKey.Champions;

        // #2 Loyal
        if (recencyScore >= LoyalMinRecency && frequencyScore >= LoyalMinFrequency && monetaryScore >= LoyalMinMonetary)
            return RfmSegmentKey.Loyal;

        // #3 CannotLoseThem
        if (recencyScore <= CannotLoseThemMaxRecency && frequencyScore >= CannotLoseThemMinFrequency && monetaryScore >= CannotLoseThemMinMonetary)
            return RfmSegmentKey.CannotLoseThem;

        // #4 AtRisk
        if (recencyScore <= AtRiskMaxRecency && frequencyScore >= AtRiskMinFrequency && monetaryScore >= AtRiskMinMonetary)
            return RfmSegmentKey.AtRisk;

        // #5 New — date override, checked before rules #6-11 (but after #1-4) per the plan.
        if (firstPurchaseDateEver >= periodEnd.AddDays(-NewMaxDaysSinceFirstPurchase))
            return RfmSegmentKey.New;

        // #6 PotentialLoyalist
        if (recencyScore >= PotentialLoyalistMinRecency
            && frequencyScore >= PotentialLoyalistMinFrequencyInclusive && frequencyScore <= PotentialLoyalistMaxFrequencyInclusive
            && monetaryScore >= PotentialLoyalistMinMonetary)
            return RfmSegmentKey.PotentialLoyalist;

        // #7 Promising
        if (recencyScore >= PromisingMinRecency && frequencyScore <= PromisingMaxFrequency && monetaryScore <= PromisingMaxMonetary)
            return RfmSegmentKey.Promising;

        // #8 AboutToSleep
        if (recencyScore >= AboutToSleepMinRecencyInclusive && recencyScore <= AboutToSleepMaxRecencyInclusive
            && frequencyScore <= AboutToSleepMaxFrequency)
            return RfmSegmentKey.AboutToSleep;

        // #9 Hibernating
        if (recencyScore <= HibernatingMaxRecency && frequencyScore <= HibernatingMaxFrequency && monetaryScore <= HibernatingMaxMonetary)
            return RfmSegmentKey.Hibernating;

        // #10 Lost — lifetime facts only, no R/F/M condition. Strict "<" because the rule is
        // "MORE than 6 months ago" — a last purchase exactly 6 months before periodEnd must
        // NOT count yet (falls through to NeedsAttention/whatever else matches instead).
        if (lifetimeReceiptCount == LostLifetimeReceiptCount
            && lastPurchaseDateEver < periodEnd.AddMonths(-LostMinMonthsSinceOnlyPurchase))
            return RfmSegmentKey.Lost;

        // #11 NeedsAttention — catch-all, guarantees every customer gets exactly one segment.
        return RfmSegmentKey.NeedsAttention;
    }

    private static void ValidateScore(int score, string paramName)
    {
        if (score < MinScore || score > MaxScore)
            throw new ArgumentOutOfRangeException(paramName, score, $"RFM scores must be between {MinScore} and {MaxScore}.");
    }
}
