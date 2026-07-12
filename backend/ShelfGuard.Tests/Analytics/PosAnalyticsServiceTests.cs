using NSubstitute;
using ShelfGuard.Application.Features.Analytics;
using ShelfGuard.Application.Features.Analytics.Dtos;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Analytics;

public sealed class PosAnalyticsServiceTests
{
    private readonly IAnalyticsRepository _repo = Substitute.For<IAnalyticsRepository>();
    private readonly IStockStatusSnapshotRepository _snapshots = Substitute.For<IStockStatusSnapshotRepository>();
    private readonly AnalyticsService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly From30  = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));

    public PosAnalyticsServiceTests() => _sut = new AnalyticsService(_repo, _snapshots);

    // ── GetPosSummary ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPosSummaryAsync_delegates_to_repository()
    {
        var expected = new PosAnalyticsSummaryDto(
            TotalRevenue: 0, TransactionCount: 0, AverageTicket: 0,
            CashRevenue: 0, CardRevenue: 0, ShiftCount: 0,
            From: From30, To: Today);

        _repo.GetPosSummaryAsync(_tenantId, null, From30, Today, default)
             .Returns(expected);

        var result = await _sut.GetPosSummaryAsync(_tenantId, null, From30, Today);

        Assert.Equal(expected, result);
        await _repo.Received(1).GetPosSummaryAsync(_tenantId, null, From30, Today, default);
    }

    [Fact]
    public async Task GetPosSummaryAsync_returns_zero_totals_for_empty_tenant()
    {
        var empty = new PosAnalyticsSummaryDto(
            TotalRevenue: 0, TransactionCount: 0, AverageTicket: 0,
            CashRevenue: 0, CardRevenue: 0, ShiftCount: 0,
            From: From30, To: Today);

        _repo.GetPosSummaryAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
             .Returns(empty);

        var result = await _sut.GetPosSummaryAsync(Guid.NewGuid(), null, From30, Today);

        Assert.Equal(0, result.TotalRevenue);
        Assert.Equal(0, result.TransactionCount);
        Assert.Equal(0, result.AverageTicket);
        Assert.Equal(0, result.ShiftCount);
    }

    // ── GetPosRevenueTrend ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPosRevenueTrendAsync_delegates_to_repository()
    {
        var expected = new PosRevenueTrendDto(Points: [], GroupBy: "day");

        _repo.GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "day", default)
             .Returns(expected);

        var result = await _sut.GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "day");

        Assert.Equal("day", result.GroupBy);
        await _repo.Received(1).GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "day", default);
    }

    [Fact]
    public async Task GetPosRevenueTrendAsync_week_groupBy_passes_week_to_repository()
    {
        var expected = new PosRevenueTrendDto(Points: [], GroupBy: "week");

        _repo.GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "week", default)
             .Returns(expected);

        var result = await _sut.GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "week");

        Assert.Equal("week", result.GroupBy);
        await _repo.Received(1).GetPosRevenueTrendAsync(_tenantId, null, From30, Today, "week", default);
    }

    // ── GetPosTopProducts ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPosTopProductsAsync_returns_items_sorted_by_revenue_descending()
    {
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        var expected = new PosTopProductsDto(Items: new List<TopProductDto>
        {
            new(productB, "Milk",  "1111111", TotalRevenue: 500m, TotalQuantity: 50, TransactionCount: 20),
            new(productA, "Bread", "2222222", TotalRevenue: 300m, TotalQuantity: 30, TransactionCount: 10),
        });

        _repo.GetPosTopProductsAsync(_tenantId, null, From30, Today, 10, default)
             .Returns(expected);

        var result = await _sut.GetPosTopProductsAsync(_tenantId, null, From30, Today, 10);

        Assert.Equal(2, result.Items.Count);
        Assert.True(result.Items[0].TotalRevenue >= result.Items[1].TotalRevenue,
            "Items should be ordered by revenue descending");
        Assert.Equal(productB, result.Items[0].ProductId);
    }

    [Fact]
    public async Task GetPosTopProductsAsync_empty_period_returns_empty_list()
    {
        var expected = new PosTopProductsDto(Items: []);

        _repo.GetPosTopProductsAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(expected);

        var result = await _sut.GetPosTopProductsAsync(Guid.NewGuid(), null, From30, Today, 10);

        Assert.Empty(result.Items);
    }

    // ── GetPosCashierStats ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPosCashierStatsAsync_delegates_to_repository()
    {
        var expected = new PosCashierStatsDto(Cashiers: []);

        _repo.GetPosCashierStatsAsync(_tenantId, null, From30, Today, default)
             .Returns(expected);

        var result = await _sut.GetPosCashierStatsAsync(_tenantId, null, From30, Today);

        Assert.Empty(result.Cashiers);
        await _repo.Received(1).GetPosCashierStatsAsync(_tenantId, null, From30, Today, default);
    }

    [Fact]
    public async Task GetPosCashierStatsAsync_cashier_average_ticket_is_revenue_over_transaction_count()
    {
        var cashierId = Guid.NewGuid();

        var expected = new PosCashierStatsDto(Cashiers: new List<CashierStatDto>
        {
            new(cashierId, "Ivan Koval", TotalRevenue: 1000m, TransactionCount: 10, AverageTicket: 100m, ShiftCount: 2),
        });

        _repo.GetPosCashierStatsAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
             .Returns(expected);

        var result = await _sut.GetPosCashierStatsAsync(_tenantId, null, From30, Today);

        var cashier = Assert.Single(result.Cashiers);
        Assert.Equal(100m, cashier.AverageTicket);
        Assert.Equal(2, cashier.ShiftCount);
    }
}
