using ShelfGuard.Application.Features.Orders;
using Xunit;

namespace ShelfGuard.Tests.Orders;

public sealed class OrderFormulaTests
{
    [Fact]
    public void Covered_demand_orders_nothing()
    {
        // Buffer 50 + BB 5 − Stock 60 − Transit 0 = −5
        var r = OrderFormula.Compute(50, 5, 60, 0, moq: 10, usq: 5);

        Assert.Equal(-5m, r.Raw);
        Assert.Equal(0m, r.ToOrder);
        Assert.Equal("none", r.Rounding);
    }

    [Fact]
    public void In_transit_reduces_the_order()
    {
        // Buffer 50 + BB 0 − Stock 10 − Transit 35 = 5 → below MOQ 10 → MOQ
        var r = OrderFormula.Compute(50, 0, 10, 35, moq: 10, usq: 5);

        Assert.Equal(5m, r.Raw);
        Assert.Equal(10m, r.ToOrder);
        Assert.Equal("moq_floor", r.Rounding);
    }

    [Fact]
    public void Below_moq_orders_moq()
    {
        // Raw 7 < MOQ 12 → 12
        var r = OrderFormula.Compute(20, 2, 10, 5, moq: 12, usq: 6);

        Assert.Equal(7m, r.Raw);
        Assert.Equal(12m, r.ToOrder);
        Assert.Equal("moq_floor", r.Rounding);
    }

    [Theory]
    [InlineData(52, 5, 50)]   // 52/5 = 10.4 → 10 × 5 = 50
    [InlineData(53, 5, 55)]   // 53/5 = 10.6 → 11 × 5 = 55
    [InlineData(52.5, 5, 55)] // midpoint → away from zero → 55
    public void Above_moq_rounds_to_nearest_usq(double raw, double usq, double expected)
    {
        // buffer = raw, everything else zero, moq small
        var r = OrderFormula.Compute((decimal)raw, 0, 0, 0, moq: 1, usq: (decimal)usq);

        Assert.Equal((decimal)expected, r.ToOrder);
        Assert.Equal("usq_rounded", r.Rounding);
    }

    [Fact]
    public void Usq_rounding_never_dips_below_moq()
    {
        // Raw 13, USQ 10 → rounds to 10, but MOQ 12 → 12
        var r = OrderFormula.Compute(13, 0, 0, 0, moq: 12, usq: 10);

        Assert.Equal(12m, r.ToOrder);
    }

    [Fact]
    public void Degenerate_moq_usq_default_to_one()
    {
        var r = OrderFormula.Compute(7.3m, 0, 0, 0, moq: 0, usq: 0);

        Assert.Equal(7m, r.ToOrder); // usq=1 → round(7.3) = 7
    }

    [Fact]
    public void Full_spec_example_buffer_plus_bb_minus_stock_minus_transit()
    {
        // Buffer 51.97 + BB 5 − Stock 20 − Transit 12 = 24.97 → USQ 6 → 24.97/6 = 4.16 → 4×6 = 24
        var r = OrderFormula.Compute(51.97m, 5, 20, 12, moq: 6, usq: 6);

        Assert.Equal(24.97m, r.Raw);
        Assert.Equal(24m, r.ToOrder);
    }
}
