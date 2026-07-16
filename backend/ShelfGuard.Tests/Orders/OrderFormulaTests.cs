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
    [InlineData(22, 10, 5, 25)] // (22-10)/5 = 2.4 → ceil 3 steps → 10+15 = 25
    [InlineData(25, 10, 5, 25)] // (25-10)/5 = 3.0 → exact step → 10+15 = 25
    [InlineData(26, 10, 5, 30)] // (26-10)/5 = 3.2 → ceil 4 steps → 10+20 = 30
    public void Above_moq_rounds_up_the_moq_anchored_ladder(double raw, double moq, double usq, double expected)
    {
        // Ladder from MOQ=10, USQ=5: 10, 15, 20, 25, 30, ... — round UP to the first
        // step that covers `raw` (v1-spec §2.7).
        var r = OrderFormula.Compute((decimal)raw, 0, 0, 0, moq: (decimal)moq, usq: (decimal)usq);

        Assert.Equal((decimal)expected, r.ToOrder);
        Assert.Equal("usq_rounded", r.Rounding);
    }

    [Fact]
    public void Usq_rounding_never_dips_below_moq()
    {
        // Raw 13, MOQ 12, USQ 10 → ladder from 12 is 12, 22, 32... → first step ≥ 13 is 22.
        // (Guaranteed by construction: the ladder starts AT moq, so it can never return
        // less than moq once raw > moq.)
        var r = OrderFormula.Compute(13, 0, 0, 0, moq: 12, usq: 10);

        Assert.Equal(22m, r.ToOrder);
    }

    [Fact]
    public void Degenerate_moq_usq_default_to_one()
    {
        // moq/usq both invalid (≤0) → default to 1/1 → ladder 1,2,3,...
        // raw=7.3 → ceil((7.3-1)/1) = ceil(6.3) = 7 steps → 1+7 = 8
        var r = OrderFormula.Compute(7.3m, 0, 0, 0, moq: 0, usq: 0);

        Assert.Equal(8m, r.ToOrder);
    }

    [Fact]
    public void Rounding_ladder_is_anchored_at_moq_not_at_zero()
    {
        // v1-spec §2.7: "MOQ=12, USQ=6 → можна: 12, 18, 24, 30..." — the ladder is
        // anchored AT MOQ (MOQ + k×USQ), which the previous "round to nearest USQ
        // multiple from zero, then clamp to MOQ" implementation got wrong whenever MOQ
        // wasn't itself a USQ multiple. Fixed 2026-07-15 (confirmed with user; see task
        // log .claude/logs/tasks/355_2026-07-15_orders-adu-buffer-audit_backend-developer.md).
        //
        // MOQ=10, USQ=6 → spec ladder: 10, 16, 22, 28, ... (NOT 10, 12, 18, 24 — the old,
        // zero-anchored multiples of 6).
        var r15 = OrderFormula.Compute(15, 0, 0, 0, moq: 10, usq: 6);
        var r17 = OrderFormula.Compute(17, 0, 0, 0, moq: 10, usq: 6);

        Assert.Equal(16m, r15.ToOrder); // first ladder step ≥ 15
        Assert.Equal(22m, r17.ToOrder); // first ladder step ≥ 17
        Assert.Equal("usq_rounded", r15.Rounding);
    }

    [Fact]
    public void Full_spec_example_buffer_plus_bb_minus_stock_minus_transit()
    {
        // Buffer 51.97 + BB 5 − Stock 20 − Transit 12 = 24.97, MOQ 6, USQ 6.
        // Ladder from MOQ=6: 6, 12, 18, 24, 30, ... — round UP to the first step ≥ 24.97,
        // which is 30 ((24.97-6)/6 = 3.16 → ceil 4 steps → 6+24 = 30).
        var r = OrderFormula.Compute(51.97m, 5, 20, 12, moq: 6, usq: 6);

        Assert.Equal(24.97m, r.Raw);
        Assert.Equal(30m, r.ToOrder);
    }
}
