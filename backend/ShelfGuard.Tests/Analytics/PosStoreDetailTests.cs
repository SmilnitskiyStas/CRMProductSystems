using NSubstitute;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Application.Features.Analytics.Dtos;
using Xunit;

namespace ShelfGuard.Tests.Analytics;

public sealed class PosStoreDetailTests
{
    private static readonly DateOnly From = new(2026, 9, 21);
    private static readonly DateOnly To = new(2026, 9, 30);
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Store = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static PosStoreDetailRaw Raw() => new(
        "Пчілка",
        [new(A, "Анна", 1000m, 10), new(B, "Богдан", 3000m, 20)],
        [
            new(A, new(2026, 9, 21, 8, 0, 0), new(2026, 9, 21, 16, 0, 0)),   // 8h
            new(B, new(2026, 9, 22, 9, 0, 0), new(2026, 9, 22, 12, 30, 0)),  // 3.5h
            new(B, new(2026, 9, 30, 10, 0, 0), null),                          // open → until Now
        ],
        [new(A, 800m, 500m), new(B, 3000m, 2000m)]);

    [Fact]
    public void Build_orders_cashiers_by_revenue_and_computes_averages_and_hours()
    {
        var dto = PosStoreDetailBuilder.Build(Store, From, To, true, Now, Raw());

        Assert.Equal(B, dto.Cashiers[0].CashierId);
        Assert.Equal(150m, dto.Cashiers[0].AverageTicket);
        Assert.Equal(2, dto.Cashiers[0].ShiftCount);
        // 3.5h + open shift 2026-09-30 10:00 → 2026-10-01 12:00 = 26h
        Assert.Equal(29.5m, dto.Cashiers[0].HoursWorked);
        Assert.Equal(8m, dto.Cashiers[1].HoursWorked);
        Assert.Equal(125m, dto.Cashiers[1].RevenuePerHour);
        Assert.Equal(4000m, dto.TotalRevenue);
        Assert.Equal(30, dto.TransactionCount);
        Assert.Equal(3, dto.ShiftCount);
        Assert.Equal(37.5m, dto.HoursWorked);
    }

    [Fact]
    public void Build_profit_is_revenue_with_cost_minus_cost_and_reports_coverage()
    {
        var dto = PosStoreDetailBuilder.Build(Store, From, To, true, Now, Raw());

        var a = dto.Cashiers.Single(c => c.CashierId == A);
        Assert.Equal(300m, a.Profit);
        Assert.Equal(80m, a.CostCoveragePercent);
        Assert.Equal(1300m, dto.TotalProfit);
        Assert.Equal(2500m, dto.TotalCost);
        Assert.True(dto.CostIncluded);
    }

    [Fact]
    public void Build_hides_cost_and_profit_without_margin_permission()
    {
        var dto = PosStoreDetailBuilder.Build(Store, From, To, false, Now, Raw());

        Assert.False(dto.CostIncluded);
        Assert.Null(dto.TotalCost);
        Assert.Null(dto.TotalProfit);
        Assert.All(dto.Cashiers, c => { Assert.Null(c.Cost); Assert.Null(c.Profit); });
    }

    [Fact]
    public void Build_cashier_without_shifts_has_zero_hours_and_null_rate()
    {
        var raw = Raw() with { Shifts = [] };
        var dto = PosStoreDetailBuilder.Build(Store, From, To, true, Now, raw);

        Assert.All(dto.Cashiers, c => { Assert.Equal(0m, c.HoursWorked); Assert.Null(c.RevenuePerHour); });
    }

    [Fact]
    public async Task Service_passes_includeCost_to_repository_and_builds_dto()
    {
        var repo = Substitute.For<IAnalyticsRepository>();
        repo.GetPosStoreDetailRawAsync(null, Store, From, To, false, default).Returns(Raw());
        var sut = new AnalyticsService(repo, Substitute.For<ShelfGuard.Domain.Interfaces.IStockStatusSnapshotRepository>());

        var dto = await sut.GetPosStoreDetailAsync(null, Store, From, To, false);

        Assert.Equal("Пчілка", dto.StoreName);
        Assert.Null(dto.TotalProfit);
        await repo.Received(1).GetPosStoreDetailRawAsync(null, Store, From, To, false, default);
    }
}
