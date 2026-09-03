using NSubstitute;
using ShelfGuard.Application.Features.SupplierAnalytics;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// Supplier-portal expansion #7 (Phase 6b): the in-memory roll-up
/// <see cref="SupplierAnalyticsService"/> performs over the marketplace order lines the repository
/// hands it — totals, period-over-period deltas, top/slow movers, per-buyer breakdown, daily trend.
/// The "SupplierTenantId == me / not cancelled / in window" filtering is the repository's job and
/// is covered by <see cref="ShelfGuard.Tests.Infrastructure.SupplierAnalyticsRepositoryIntegrationTests"/>.
/// </summary>
public sealed class SupplierAnalyticsServiceTests
{
    private readonly ISupplierAnalyticsRepository _repo = Substitute.For<ISupplierAnalyticsRepository>();
    private readonly ISupplierChatRepository _tenantNames = Substitute.For<ISupplierChatRepository>();
    private readonly SupplierAnalyticsService _sut;

    private readonly Guid _supplierTenantId = Guid.NewGuid();
    private readonly Guid _buyerA = Guid.NewGuid();
    private readonly Guid _buyerB = Guid.NewGuid();
    private readonly Guid _itemMilk = Guid.NewGuid();
    private readonly Guid _itemBread = Guid.NewGuid();
    private readonly Guid _itemButter = Guid.NewGuid();

    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To   = new(2026, 8, 31);

    public SupplierAnalyticsServiceTests()
    {
        _sut = new SupplierAnalyticsService(_repo, _tenantNames);
        _tenantNames.GetTenantDisplayNameAsync(_buyerA, Arg.Any<CancellationToken>()).Returns("АТБ");
        _tenantNames.GetTenantDisplayNameAsync(_buyerB, Arg.Any<CancellationToken>()).Returns("Сільпо");
        _repo.GetAvailableCatalogAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SupplierCatalogRow>());
        _repo.GetOrderLinesAsync(_supplierTenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SupplierOrderLineRow>());
    }

    private SupplierOrderLineRow Line(
        Guid orderId, Guid? itemId, string name, Guid buyer, decimal qty, decimal lineTotal, int day) =>
        new(orderId, itemId, name, buyer, qty, lineTotal,
            new DateTimeOffset(2026, 8, day, 10, 0, 0, TimeSpan.Zero));

    private void SeedCurrent(params SupplierOrderLineRow[] rows) =>
        _repo.GetOrderLinesAsync(_supplierTenantId, From, To, Arg.Any<CancellationToken>()).Returns(rows);

    private void SeedPrevious(params SupplierOrderLineRow[] rows) =>
        _repo.GetOrderLinesAsync(_supplierTenantId, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31),
            Arg.Any<CancellationToken>()).Returns(rows);

    [Fact]
    public async Task Totals_SumRevenueQtyAndDistinctOrders()
    {
        var order1 = Guid.NewGuid();
        var order2 = Guid.NewGuid();
        SeedCurrent(
            Line(order1, _itemMilk, "Молоко", _buyerA, qty: 10, lineTotal: 300m, day: 3),
            Line(order1, _itemBread, "Хліб", _buyerA, qty: 5, lineTotal: 100m, day: 3),
            Line(order2, _itemMilk, "Молоко", _buyerB, qty: 4, lineTotal: 120m, day: 9));

        var result = await _sut.GetAsync(_supplierTenantId, From, To);

        Assert.Equal(520m, result.TotalRevenue);
        Assert.Equal(19m, result.ItemsSold);
        Assert.Equal(2, result.OrderCount);
        Assert.Equal(From, result.From);
        Assert.Equal(To, result.To);
    }

    [Fact]
    public async Task PeriodDeltas_CompareAgainstTheEqualLengthPrecedingWindow()
    {
        SeedCurrent(Line(Guid.NewGuid(), _itemMilk, "Молоко", _buyerA, qty: 10, lineTotal: 200m, day: 5));
        SeedPrevious(Line(Guid.NewGuid(), _itemMilk, "Молоко", _buyerA, qty: 5, lineTotal: 100m, day: 5));

        var result = await _sut.GetAsync(_supplierTenantId, From, To);

        Assert.Equal(200m, result.RevenueDelta.Current);
        Assert.Equal(100m, result.RevenueDelta.Previous);
        Assert.Equal(100m, result.RevenueDelta.PercentChange);   // +100%
        Assert.Equal(1m, result.OrderCountDelta.Current);
        Assert.Equal(1m, result.OrderCountDelta.Previous);
        Assert.Equal(10m, result.ItemsSoldDelta.Current);
        Assert.Equal(5m, result.ItemsSoldDelta.Previous);
    }

    [Fact]
    public async Task TopItems_OrderedByQtyDescending_WithPerItemOrderCount()
    {
        var o1 = Guid.NewGuid();
        var o2 = Guid.NewGuid();
        SeedCurrent(
            Line(o1, _itemMilk, "Молоко", _buyerA, qty: 3, lineTotal: 90m, day: 2),
            Line(o2, _itemMilk, "Молоко", _buyerB, qty: 30, lineTotal: 900m, day: 4),
            Line(o1, _itemBread, "Хліб", _buyerA, qty: 50, lineTotal: 500m, day: 2));

        var result = await _sut.GetAsync(_supplierTenantId, From, To);

        Assert.Equal("Хліб", result.TopItems[0].ItemName);
        Assert.Equal(50m, result.TopItems[0].QtySold);
        Assert.Equal(1, result.TopItems[0].OrderCount);
        Assert.Equal("Молоко", result.TopItems[1].ItemName);
        Assert.Equal(33m, result.TopItems[1].QtySold);
        Assert.Equal(2, result.TopItems[1].OrderCount);   // ordered in two distinct orders
    }

    [Fact]
    public async Task SlowItems_ZeroDemandAvailableItemAppearsWithZeroes()
    {
        SeedCurrent(Line(Guid.NewGuid(), _itemMilk, "Молоко", _buyerA, qty: 12, lineTotal: 360m, day: 6));
        _repo.GetAvailableCatalogAsync(_supplierTenantId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new SupplierCatalogRow(_itemMilk, "Молоко"),
            new SupplierCatalogRow(_itemButter, "Масло"),   // never ordered
        });

        var result = await _sut.GetAsync(_supplierTenantId, From, To);

        var butter = Assert.Single(result.SlowItems, i => i.SupplierItemId == _itemButter);
        Assert.Equal(0m, butter.QtySold);
        Assert.Equal(0m, butter.Revenue);
        Assert.Equal(0, butter.OrderCount);
        // Least demand first — the never-ordered item precedes the one with 12 sold.
        Assert.Equal(_itemButter, result.SlowItems[0].SupplierItemId);
    }

    [Fact]
    public async Task ByBuyer_GroupsPerClientTenant_WithResolvedName_HighestRevenueFirst()
    {
        var oA1 = Guid.NewGuid();
        var oA2 = Guid.NewGuid();
        SeedCurrent(
            Line(oA1, _itemMilk, "Молоко", _buyerA, qty: 1, lineTotal: 50m, day: 2),
            Line(oA2, _itemBread, "Хліб", _buyerA, qty: 1, lineTotal: 70m, day: 8),
            Line(Guid.NewGuid(), _itemMilk, "Молоко", _buyerB, qty: 1, lineTotal: 500m, day: 3));

        var result = await _sut.GetAsync(_supplierTenantId, From, To);

        Assert.Equal(2, result.ByBuyer.Count);
        Assert.Equal("Сільпо", result.ByBuyer[0].ClientName);   // 500 > 120
        Assert.Equal(500m, result.ByBuyer[0].Revenue);
        Assert.Equal(1, result.ByBuyer[0].OrderCount);
        Assert.Equal("АТБ", result.ByBuyer[1].ClientName);
        Assert.Equal(120m, result.ByBuyer[1].Revenue);
        Assert.Equal(2, result.ByBuyer[1].OrderCount);
    }

    [Fact]
    public async Task RevenueTrend_IsDailyAndOrderedByDate()
    {
        var day3 = Guid.NewGuid();
        SeedCurrent(
            Line(day3, _itemMilk, "Молоко", _buyerA, qty: 1, lineTotal: 100m, day: 3),
            Line(day3, _itemBread, "Хліб", _buyerA, qty: 1, lineTotal: 40m, day: 3),
            Line(Guid.NewGuid(), _itemMilk, "Молоко", _buyerB, qty: 1, lineTotal: 25m, day: 1));

        var result = await _sut.GetAsync(_supplierTenantId, From, To);

        Assert.Equal(2, result.RevenueTrend.Count);
        Assert.Equal(new DateOnly(2026, 8, 1), result.RevenueTrend[0].Date);
        Assert.Equal(25m, result.RevenueTrend[0].Revenue);
        Assert.Equal(new DateOnly(2026, 8, 3), result.RevenueTrend[1].Date);
        Assert.Equal(140m, result.RevenueTrend[1].Revenue);
        Assert.Equal(1, result.RevenueTrend[1].OrderCount);
    }

    [Fact]
    public async Task Range_WiderThan366Days_IsClampedByMovingFromForward()
    {
        var wideFrom = new DateOnly(2024, 1, 1);
        var wideTo = new DateOnly(2026, 1, 1);

        var result = await _sut.GetAsync(_supplierTenantId, wideFrom, wideTo);

        Assert.Equal(wideTo, result.To);
        Assert.Equal(wideTo.AddDays(-365), result.From);
    }
}
