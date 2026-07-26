using ShelfGuard.Application.Features.MarketingAnalytics;
using ShelfGuard.Domain.Constants;
using Xunit;

namespace ShelfGuard.Tests.MarketingAnalytics;

/// <summary>
/// TASK-406: one test per priority-table rule (docs/uployal/RFM_ANALYSIS.md §6.3 + the
/// approved plan's numbered table) plus the catch-all and the cross-rule priority-order
/// interactions the plan calls out explicitly ("перше правило, що підійшло, виграє").
/// </summary>
public sealed class RfmSegmentClassifierTests
{
    private static readonly DateOnly PeriodEnd = new(2026, 7, 26);

    // A lifetime profile that never accidentally satisfies New (#5) or Lost (#10) unless a
    // test explicitly wants it to — reused as the default "neutral" lifetime shape.
    private static readonly DateOnly OldFirstPurchase = PeriodEnd.AddDays(-400);
    private static readonly DateOnly RecentLastPurchase = PeriodEnd.AddDays(-5);
    private const int ManyLifetimeReceipts = 5;

    private static RfmSegmentKey Classify(
        int r, int f, int m,
        DateOnly? firstPurchaseEver = null,
        int lifetimeReceiptCount = ManyLifetimeReceipts,
        DateOnly? lastPurchaseEver = null,
        DateOnly? periodEnd = null) =>
        RfmSegmentClassifier.Classify(
            r, f, m,
            firstPurchaseEver ?? OldFirstPurchase,
            lifetimeReceiptCount,
            lastPurchaseEver ?? RecentLastPurchase,
            periodEnd ?? PeriodEnd);

    // ── #1 Champions ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(4, 4, 4)]
    [InlineData(5, 5, 5)]
    [InlineData(4, 5, 4)]
    public void Champions_when_r_f_m_all_at_least_4(int r, int f, int m) =>
        Assert.Equal(RfmSegmentKey.Champions, Classify(r, f, m));

    [Fact]
    public void Not_Champions_when_any_score_below_4()
    {
        Assert.NotEqual(RfmSegmentKey.Champions, Classify(3, 5, 5));
        Assert.NotEqual(RfmSegmentKey.Champions, Classify(5, 3, 5));
        Assert.NotEqual(RfmSegmentKey.Champions, Classify(5, 5, 3));
    }

    // ── #2 Loyal ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(3, 4, 3)]
    [InlineData(3, 5, 5)]
    public void Loyal_when_r_ge3_f_ge4_m_ge3(int r, int f, int m) =>
        Assert.Equal(RfmSegmentKey.Loyal, Classify(r, f, m));

    [Fact]
    public void Not_Loyal_when_frequency_below_4() =>
        Assert.NotEqual(RfmSegmentKey.Loyal, Classify(3, 3, 5));

    // ── #3 CannotLoseThem ────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 4, 4)]
    [InlineData(2, 5, 5)]
    public void CannotLoseThem_when_r_le2_f_ge4_m_ge4(int r, int f, int m) =>
        Assert.Equal(RfmSegmentKey.CannotLoseThem, Classify(r, f, m));

    [Fact]
    public void Not_CannotLoseThem_when_recency_above_2() =>
        // r=3 with f/m=4/4 would hit Loyal (#2) first anyway — confirms it never falls to #3.
        Assert.Equal(RfmSegmentKey.Loyal, Classify(3, 4, 4));

    // ── #4 AtRisk ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 3, 3)]
    [InlineData(2, 3, 4)]
    public void AtRisk_when_r_le2_f_ge3_m_ge3(int r, int f, int m) =>
        Assert.Equal(RfmSegmentKey.AtRisk, Classify(r, f, m));

    [Fact]
    public void CannotLoseThem_takes_priority_over_AtRisk_when_both_conditions_match() =>
        // r=1,f=4,m=4 matches both #3 (CannotLoseThem) and #4 (AtRisk) — #3 is checked first.
        Assert.Equal(RfmSegmentKey.CannotLoseThem, Classify(1, 4, 4));

    // ── #5 New (date override) ───────────────────────────────────────────────

    [Fact]
    public void New_when_first_purchase_within_30_days_even_with_poor_scores()
    {
        // r=1,f=1,m=1 would otherwise land on Hibernating (#9) — New (#5) must win because it
        // is checked first.
        var result = Classify(1, 1, 1, firstPurchaseEver: PeriodEnd.AddDays(-10));
        Assert.Equal(RfmSegmentKey.New, result);
    }

    [Fact]
    public void New_boundary_is_inclusive_at_exactly_30_days()
    {
        var result = Classify(1, 1, 1, firstPurchaseEver: PeriodEnd.AddDays(-30));
        Assert.Equal(RfmSegmentKey.New, result);
    }

    [Fact]
    public void Not_New_at_31_days_falls_through_to_a_later_rule()
    {
        // r=3,f=1,m=anything at 31 days-old-first-purchase lands on AboutToSleep (#8) — proves
        // New did NOT match at the 31-day mark (one day past the inclusive 30-day boundary).
        var result = Classify(3, 1, 5, firstPurchaseEver: PeriodEnd.AddDays(-31));
        Assert.Equal(RfmSegmentKey.AboutToSleep, result);
    }

    [Fact]
    public void Champions_takes_priority_over_New_when_both_conditions_match() =>
        // A brand-new customer who already scores top R/F/M is still Champions — #1-4 are
        // checked before the #5 date override, per the plan's explicit priority note.
        Assert.Equal(RfmSegmentKey.Champions, Classify(5, 5, 5, firstPurchaseEver: PeriodEnd.AddDays(-1)));

    // ── #6 PotentialLoyalist ─────────────────────────────────────────────────

    [Theory]
    [InlineData(3, 2, 2)]
    [InlineData(4, 3, 5)]
    public void PotentialLoyalist_when_r_ge3_f_in_2_or_3_m_ge2(int r, int f, int m) =>
        Assert.Equal(RfmSegmentKey.PotentialLoyalist, Classify(r, f, m));

    [Fact]
    public void Not_PotentialLoyalist_when_monetary_below_2() =>
        // r=3,f=2,m=1 must NOT be PotentialLoyalist — falls through to AboutToSleep (#8).
        Assert.Equal(RfmSegmentKey.AboutToSleep, Classify(3, 2, 1));

    // ── #7 Promising ─────────────────────────────────────────────────────────

    // f must be 1 (not 2) here: f=2 with m>=2 would satisfy PotentialLoyalist (#6, f∈{2,3} AND
    // m>=2), which is checked first and would win — f<=2 alone is not enough to isolate #7.
    [Theory]
    [InlineData(4, 1, 1)]
    [InlineData(5, 1, 2)]
    public void Promising_when_r_ge4_f_le2_m_le2(int r, int f, int m) =>
        Assert.Equal(RfmSegmentKey.Promising, Classify(r, f, m));

    // ── #8 AboutToSleep ──────────────────────────────────────────────────────

    // f must be 1 (not 2) at r=3 here for the same reason as Promising above (f=2 with m>=2
    // would hand it to PotentialLoyalist first); see the dedicated r=3,f=2,m=1 case in
    // Not_PotentialLoyalist_when_monetary_below_2 below for that specific boundary instead.
    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    public void AboutToSleep_when_r_in_2_or_3_f_le2(int r, int f) =>
        Assert.Equal(RfmSegmentKey.AboutToSleep, Classify(r, f, 5));

    // ── #9 Hibernating ───────────────────────────────────────────────────────

    // r must be 1 (not 2) here: whenever r=2 and f<=2 (Hibernating's own prerequisite),
    // AboutToSleep's condition (r∈{2,3} AND f<=2) is ALSO true and is checked first (#8 before
    // #9) — so r=2 can never actually reach Hibernating. Only r=1 avoids AboutToSleep's r∈{2,3}.
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 2, 2)]
    public void Hibernating_when_r_le2_f_le2_m_le2(int r, int f, int m) =>
        Assert.Equal(RfmSegmentKey.Hibernating, Classify(r, f, m));

    [Fact]
    public void Hibernating_takes_priority_over_Lost_when_both_conditions_match()
    {
        // r=1,f=1,m=1 with a single lifetime receipt >6 months old matches BOTH Hibernating
        // (#9) and Lost (#10) — Hibernating wins because it's checked first. This is a direct
        // consequence of the plan's literal table order, documented on the classifier itself.
        var result = Classify(1, 1, 1, lifetimeReceiptCount: 1, lastPurchaseEver: PeriodEnd.AddMonths(-7));
        Assert.Equal(RfmSegmentKey.Hibernating, result);
    }

    // ── #10 Lost ─────────────────────────────────────────────────────────────

    [Fact]
    public void Lost_when_exactly_one_lifetime_receipt_and_over_6_months_old()
    {
        // r=4,f=1,m=3 deliberately avoids every rule #1-#9 (see class-level derivation in the
        // task log) so Lost is reachable at all.
        var result = Classify(4, 1, 3, lifetimeReceiptCount: 1, lastPurchaseEver: PeriodEnd.AddMonths(-7));
        Assert.Equal(RfmSegmentKey.Lost, result);
    }

    [Fact]
    public void Lost_boundary_excludes_exactly_6_months()
    {
        // ">6 months" is a strict inequality — exactly 6 months ago must NOT count yet.
        var result = Classify(4, 1, 3, lifetimeReceiptCount: 1, lastPurchaseEver: PeriodEnd.AddMonths(-6));
        Assert.Equal(RfmSegmentKey.NeedsAttention, result);
    }

    [Fact]
    public void Lost_boundary_includes_one_day_past_6_months()
    {
        var result = Classify(4, 1, 3, lifetimeReceiptCount: 1, lastPurchaseEver: PeriodEnd.AddMonths(-6).AddDays(-1));
        Assert.Equal(RfmSegmentKey.Lost, result);
    }

    [Fact]
    public void Not_Lost_when_more_than_one_lifetime_receipt()
    {
        var result = Classify(4, 1, 3, lifetimeReceiptCount: 2, lastPurchaseEver: PeriodEnd.AddMonths(-7));
        Assert.Equal(RfmSegmentKey.NeedsAttention, result);
    }

    // ── #11 NeedsAttention (catch-all) ───────────────────────────────────────

    [Fact]
    public void NeedsAttention_is_the_catch_all_for_every_customer_that_matches_nothing_else()
    {
        // r=3,f=3,m=1: fails Champions/Loyal (f<4), CannotLoseThem/AtRisk (r>2), New (old first
        // purchase), PotentialLoyalist (m<2), Promising (r<4), AboutToSleep (f=3 not <=2),
        // Hibernating (r>2), Lost (many lifetime receipts) — nothing left but the catch-all.
        var result = Classify(3, 3, 1);
        Assert.Equal(RfmSegmentKey.NeedsAttention, result);
    }

    // ── Input validation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Classify_throws_on_out_of_range_recency_score(int badScore) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Classify(badScore, 3, 3));

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Classify_throws_on_out_of_range_frequency_score(int badScore) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Classify(3, badScore, 3));

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Classify_throws_on_out_of_range_monetary_score(int badScore) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Classify(3, 3, badScore));

    [Fact]
    public void Classify_throws_when_lifetime_receipt_count_is_zero() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Classify(3, 3, 3, lifetimeReceiptCount: 0));
}
