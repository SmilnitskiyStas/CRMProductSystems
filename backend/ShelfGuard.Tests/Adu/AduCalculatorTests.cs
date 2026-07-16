using ShelfGuard.Application.Features.Adu;
using ShelfGuard.Domain.Entities;
using Xunit;

namespace ShelfGuard.Tests.Adu;

public sealed class AduCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 6, 11);

    private static DailySale Day(int daysAgo, decimal sold, decimal? eod = null,
        bool promo = false, bool anomaly = false) => new()
    {
        StoreId = Guid.Empty,
        ProductId = Guid.Empty,
        Date = Today.AddDays(-daysAgo),
        QuantitySold = sold,
        QuantityEndOfDay = eod,
        IsPromoDay = promo,
        IsAnomaly = anomaly,
    };

    [Fact]
    public void Frequent_product_gets_group3_and_30d_window()
    {
        // 25 valid days in the last 30 → group 3, effective = ADU30
        var sales = Enumerable.Range(1, 25).Select(i => Day(i, sold: 10)).ToList();

        var r = AduCalculator.Compute(sales, Today);

        Assert.Equal((short)3, r.ProductGroup);
        Assert.Equal(10m, r.Adu30d);
        Assert.Equal(r.Adu30d, r.AduEffective);
        Assert.Equal(25, r.ValidDays30);
    }

    [Fact]
    public void Medium_product_gets_group2_and_60d_window()
    {
        // 16 valid days spread over 60 days (every ~4th day) → not enough for group 3
        var sales = Enumerable.Range(1, 16).Select(i => Day(i * 4 - 2, sold: 8)).ToList();

        var r = AduCalculator.Compute(sales, Today);

        Assert.Equal((short)2, r.ProductGroup);
        Assert.Equal(r.Adu60d, r.AduEffective);
    }

    [Fact]
    public void Rare_product_gets_group1_and_90d_window()
    {
        // 10 valid days over 90 (every 9th day)
        var sales = Enumerable.Range(1, 10).Select(i => Day(i * 9 - 4, sold: 3)).ToList();

        var r = AduCalculator.Compute(sales, Today);

        Assert.Equal((short)1, r.ProductGroup);
        Assert.Equal(r.Adu90d, r.AduEffective);
        Assert.Equal(3m, r.AduEffective);
    }

    [Fact]
    public void Insufficient_data_yields_no_group_and_null_effective()
    {
        var sales = Enumerable.Range(1, 5).Select(i => Day(i * 10, sold: 2)).ToList();

        var r = AduCalculator.Compute(sales, Today);

        Assert.Null(r.ProductGroup);
        Assert.Null(r.AduEffective);
        Assert.NotNull(r.Adu90d); // window averages still reported
    }

    [Fact]
    public void Promo_and_anomaly_days_are_excluded()
    {
        var sales = new List<DailySale>
        {
            Day(1, sold: 10),
            Day(2, sold: 500, anomaly: true),  // wholesale spike — excluded
            Day(3, sold: 50, promo: true),     // promo — excluded
            Day(4, sold: 20),
        };

        var r = AduCalculator.Compute(sales, Today);

        Assert.Equal(2, r.ValidDays30);
        Assert.Equal(15m, r.Adu30d); // (10+20)/2
    }

    [Fact]
    public void Zero_sales_day_with_stock_on_shelf_is_valid_and_lowers_adu()
    {
        // Product was on the shelf (eod > 0) but nothing sold — a real zero-demand day
        var sales = new List<DailySale>
        {
            Day(1, sold: 10),
            Day(2, sold: 0, eod: 35),
        };

        var r = AduCalculator.Compute(sales, Today);

        Assert.Equal(2, r.ValidDays30);
        Assert.Equal(5m, r.Adu30d); // (10+0)/2
    }

    [Fact]
    public void Zero_sales_day_without_stock_is_invalid()
    {
        // Nothing sold and shelf empty: out-of-stock day must not drag ADU down
        var sales = new List<DailySale>
        {
            Day(1, sold: 10),
            Day(2, sold: 0, eod: 0),
        };

        var r = AduCalculator.Compute(sales, Today);

        Assert.Equal(1, r.ValidDays30);
        Assert.Equal(10m, r.Adu30d);
    }

    [Fact]
    public void Today_is_never_counted()
    {
        var sales = new List<DailySale> { Day(0, sold: 99), Day(1, sold: 10) };

        var r = AduCalculator.Compute(sales, Today);

        Assert.Equal(1, r.ValidDays30);
        Assert.Equal(10m, r.Adu30d);
    }

    [Fact]
    public void Brand_new_product_with_no_sales_history_is_null_not_an_exception()
    {
        // No daily_sales rows at all yet (product just added to catalog) — must not
        // divide by zero and must report "insufficient data", not throw or default to 0.
        var r = AduCalculator.Compute([], Today);

        Assert.Null(r.ProductGroup);
        Assert.Null(r.AduEffective);
        Assert.Null(r.Adu30d);
        Assert.Null(r.Adu60d);
        Assert.Null(r.Adu90d);
        Assert.Equal(0, r.ValidDays30);
        Assert.Equal(0, r.ValidDays60);
        Assert.Equal(0, r.ValidDays90);
    }

    [Fact]
    public void Windows_are_cumulative_30_inside_60_inside_90()
    {
        // 1 valid day at age 20, one at 45, one at 80
        var sales = new List<DailySale> { Day(20, 6), Day(45, 12), Day(80, 30) };

        var r = AduCalculator.Compute(sales, Today);

        Assert.Equal(6m, r.Adu30d);          // only day 20
        Assert.Equal(9m, r.Adu60d);          // (6+12)/2
        Assert.Equal(16m, r.Adu90d);         // (6+12+30)/3
        Assert.Equal(1, r.ValidDays30);
        Assert.Equal(2, r.ValidDays60);
        Assert.Equal(3, r.ValidDays90);
    }
}
