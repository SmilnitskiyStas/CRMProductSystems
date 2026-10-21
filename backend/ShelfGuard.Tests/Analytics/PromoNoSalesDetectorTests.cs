using ShelfGuard.Application.Features.Analytics.PromoNoSales;
using Xunit;

namespace ShelfGuard.Tests.Analytics;

public sealed class PromoNoSalesDetectorTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    private static PromoWindow Promo(int startedDaysAgo, int? endsInDays = null) =>
        new("discount", Guid.NewGuid(), "promo", Today.AddDays(-startedDaysAgo),
            endsInDays is { } d ? Today.AddDays(d) : null);

    private static Dictionary<DateOnly, decimal> Sales(params (int daysAgo, decimal qty)[] rows) =>
        rows.ToDictionary(r => Today.AddDays(-r.daysAgo), r => r.qty);

    [Fact]
    public void NoSales_WithBaselineAndStock_IsFlaggedAsNoSales()
    {
        var v = PromoNoSalesDetector.Evaluate(Promo(5), Today, adu: 2m, storeStock: 10m, Sales());

        Assert.NotNull(v);
        Assert.Equal(PromoNoSalesReasons.NoSales, v!.Reason);
        Assert.Equal(5, v.Days);          // 5 full days: today-5 .. yesterday
        Assert.Equal(10m, v.ExpectedUnits);
    }

    [Fact]
    public void FewSales_BelowRatio_IsFlaggedAsLowSales()
    {
        // expected 2 x 5 = 10; 1 sold < 10 x 0.2 = 2
        var v = PromoNoSalesDetector.Evaluate(Promo(5), Today, 2m, 10m, Sales((2, 1m)));

        Assert.NotNull(v);
        Assert.Equal(PromoNoSalesReasons.LowSales, v!.Reason);
    }

    [Fact]
    public void SalesAtOrAboveRatio_IsNotFlagged()
    {
        // 2 sold == 10 x 0.2
        Assert.Null(PromoNoSalesDetector.Evaluate(Promo(5), Today, 2m, 10m, Sales((1, 2m))));
    }

    [Fact]
    public void NoStockInStore_IsNotFlagged()
    {
        Assert.Null(PromoNoSalesDetector.Evaluate(Promo(5), Today, 2m, 0m, Sales()));
        Assert.Null(PromoNoSalesDetector.Evaluate(Promo(5), Today, 2m, 0.5m, Sales()));
    }

    [Fact]
    public void PromoShorterThanMinDays_IsNotFlagged()
    {
        // started 2 days ago => only 2 full days (< MinPromoDays = 3)
        Assert.Null(PromoNoSalesDetector.Evaluate(Promo(2), Today, 5m, 10m, Sales()));
    }

    [Fact]
    public void ExpectedVolumeTooSmall_IsNotFlagged()
    {
        // adu 0.1 x 5 days = 0.5 < MinExpectedUnits
        Assert.Null(PromoNoSalesDetector.Evaluate(Promo(5), Today, 0.1m, 10m, Sales()));
    }

    [Fact]
    public void NoBaseline_FlaggedOnlyAfterSevenDaysWithoutAnySale()
    {
        Assert.Null(PromoNoSalesDetector.Evaluate(Promo(5), Today, null, 10m, Sales()));
        Assert.Null(PromoNoSalesDetector.Evaluate(Promo(8), Today, null, 10m, Sales((3, 1m))));

        var v = PromoNoSalesDetector.Evaluate(Promo(8), Today, null, 10m, Sales());
        Assert.NotNull(v);
        Assert.Equal(PromoNoSalesReasons.NoSales, v!.Reason);
        Assert.Null(v.ExpectedUnits);
    }

    [Fact]
    public void SalesBeforePromoStart_AreIgnored()
    {
        // Big sales 10 days ago are outside the 5-day promo window.
        var v = PromoNoSalesDetector.Evaluate(Promo(5), Today, 2m, 10m, Sales((10, 50m)));

        Assert.NotNull(v);
        Assert.Equal(0m, v!.UnitsSold);
    }

    [Fact]
    public void EndedPromo_IsJudgedOnlyOverItsOwnDays()
    {
        // Ran 10..6 days ago (5 days), ended; no sales.
        var promo = new PromoWindow("campaign", Guid.NewGuid(), "c", Today.AddDays(-10), Today.AddDays(-6));
        var v = PromoNoSalesDetector.Evaluate(promo, Today, 2m, 10m, Sales());

        Assert.NotNull(v);
        Assert.Equal(5, v!.Days);
    }

    [Fact]
    public void LongRunningPromo_IsCappedToLookbackWindow()
    {
        var v = PromoNoSalesDetector.Evaluate(Promo(90), Today, 1m, 10m, Sales());

        Assert.NotNull(v);
        Assert.Equal(PromoNoSalesThresholds.LookbackDays, v!.Days);
    }

    [Fact]
    public void PromoKey_CombinesSourceAndId()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"rule:{id}", new PromoWindow("rule", id, "n", Today, null).Key);
    }
}
