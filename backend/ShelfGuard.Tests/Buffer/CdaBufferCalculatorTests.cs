using ShelfGuard.Application.Features.Buffer;
using ShelfGuard.Domain.Entities;
using Xunit;

namespace ShelfGuard.Tests.Buffer;

public sealed class CdaBufferCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 6, 11);

    [Fact]
    public void Zones_follow_spec_formulas()
    {
        // ADU=10, LT=1, OC=2.3, variability=0.5, safety=1.0
        var z = CdaBufferCalculator.Compute(10m, 1m, 2.3m, 0.5m);

        Assert.Equal(33.00m, z.Green);   // 10 × (1 + 2.3)
        Assert.Equal(11.50m, z.Yellow);  // 10 × 2.3 × 0.5
        Assert.Equal(10.00m, z.Red);     // 10 × 1 × 1.0
        Assert.Equal(54.50m, z.Total);
    }

    [Fact]
    public void Safety_factor_scales_red_zone_only()
    {
        var low = CdaBufferCalculator.Compute(10m, 2m, 3m, 0.5m, safetyFactor: 0.5m);
        var high = CdaBufferCalculator.Compute(10m, 2m, 3m, 0.5m, safetyFactor: 1.5m);

        Assert.Equal(10m, low.Red);   // 10 × 2 × 0.5
        Assert.Equal(30m, high.Red);  // 10 × 2 × 1.5
        Assert.Equal(low.Green, high.Green);
        Assert.Equal(low.Yellow, high.Yellow);
    }

    [Theory]
    [InlineData(new[] { 1, 3, 5 }, 2, 2.0, 2.3)] // 3 deliveries/week → OC 7/3 = 2.3
    [InlineData(new[] { 2 }, 1, 1.0, 7.0)]        // weekly → OC 7
    [InlineData(new[] { 1, 2, 3, 4, 5, 6, 7 }, null, 1.0, 1.0)] // daily → OC 1, LT default 1
    public void Cycle_derives_from_schedule(int[] days, int? leadDays, double expLt, double expOc)
    {
        var schedule = new SupplySchedule { DayOfWeek = [.. days], OrderLeadDays = leadDays };

        var (lt, oc) = CdaBufferCalculator.CycleFromSchedule(schedule);

        Assert.Equal((decimal)expLt, lt);
        Assert.Equal((decimal)expOc, oc);
    }

    [Fact]
    public void Cycle_from_misconfigured_empty_schedule_does_not_divide_by_zero()
    {
        // An active SupplySchedule row with no DayOfWeek set (data-entry gap) must fall
        // back to the weekly cycle, not throw DivideByZeroException in 7m / count.
        var schedule = new SupplySchedule { DayOfWeek = [], OrderLeadDays = null };

        var (lt, oc) = CdaBufferCalculator.CycleFromSchedule(schedule);

        Assert.Equal(1m, lt);   // default lead time
        Assert.Equal(7.0m, oc); // Math.Max(1, 0) deliveries/week → OC = 7
    }

    [Fact]
    public void Zero_effective_adu_yields_zero_buffer_not_an_exception()
    {
        // A product can reach the minimum valid-day threshold while every valid day sold
        // nothing (e.g. shelf-stocked but untouched) — AduEffective = 0, not null.
        // The buffer must resolve to all-zero zones, never throw or go negative.
        var z = CdaBufferCalculator.Compute(0m, leadTime: 2m, orderCycle: 3m, variability: 0.5m);

        Assert.Equal(0m, z.Green);
        Assert.Equal(0m, z.Yellow);
        Assert.Equal(0m, z.Red);
        Assert.Equal(0m, z.Total);
    }

    [Fact]
    public void Variability_is_cv_of_valid_day_sales()
    {
        // Sales 8 and 12 → mean 10, σ = 2 → CV = 0.2
        var sales = new List<DailySale>
        {
            Sale(1, 8), Sale(2, 12),
        };

        var v = CdaBufferCalculator.Variability(sales, productGroup: 3, Today);

        Assert.Equal(0.2m, v);
    }

    [Fact]
    public void Variability_clamps_to_floor_with_insufficient_data()
    {
        Assert.Equal(0.2m, CdaBufferCalculator.Variability([], 3, Today));
        Assert.Equal(0.2m, CdaBufferCalculator.Variability([Sale(1, 10)], 3, Today));
    }

    [Fact]
    public void Variability_excludes_promo_anomaly_and_out_of_window_days()
    {
        var sales = new List<DailySale>
        {
            Sale(1, 10), Sale(2, 10),                 // stable in-window
            Sale(3, 500, anomaly: true),              // excluded
            Sale(4, 100, promo: true),                // excluded
            Sale(40, 99),                             // outside group-3 30d window
        };

        var v = CdaBufferCalculator.Variability(sales, productGroup: 3, Today);

        Assert.Equal(0.2m, v); // only two identical days → CV 0 → clamped to floor
    }

    [Fact]
    public void Variability_caps_at_ceiling_for_chaotic_demand()
    {
        var sales = new List<DailySale> { Sale(1, 1), Sale(2, 1), Sale(3, 1), Sale(4, 500) };

        var v = CdaBufferCalculator.Variability(sales, productGroup: 3, Today);

        Assert.Equal(1.5m, v);
    }

    private static DailySale Sale(int daysAgo, decimal sold, bool promo = false, bool anomaly = false) => new()
    {
        Date = Today.AddDays(-daysAgo),
        QuantitySold = sold,
        QuantityEndOfDay = 10,
        IsPromoDay = promo,
        IsAnomaly = anomaly,
    };
}
