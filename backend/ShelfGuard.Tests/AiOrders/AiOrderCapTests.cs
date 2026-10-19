using NSubstitute;
using ShelfGuard.Application.Features.AiOrders;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Orders.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.AiOrders;

/// <summary>QA TASK-717: the AI order is capped at <see cref="AiOrderService.MaxPositions"/> most-urgent positions.</summary>
public sealed class AiOrderCapTests
{
    private readonly IAiOrderRepository _repo = Substitute.For<IAiOrderRepository>();
    private readonly IAiOrderAdvisor _advisor = Substitute.For<IAiOrderAdvisor>();
    private readonly IOrderCalcService _orderCalc = Substitute.For<IOrderCalcService>();
    private readonly IWeatherRepository _weather = Substitute.For<IWeatherRepository>();
    private readonly IEventRepository _events = Substitute.For<IEventRepository>();
    private readonly ISupplyScheduleRepository _schedules = Substitute.For<ISupplyScheduleRepository>();
    private readonly ICannibalizationRepository _promos = Substitute.For<ICannibalizationRepository>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();

    private AiOrderService Build()
    {
        _repo.GetStoreNameAsync(_storeId, Arg.Any<CancellationToken>()).Returns("Store");
        _weather.GetForecastAsync(_storeId, Arg.Any<CancellationToken>()).Returns(new List<WeatherData>());
        _events.GetAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(new List<DemandEvent>());
        _promos.GetActivePromoCoefficientsAsync(_storeId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());
        _repo.GetAdu30Async(_storeId, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, decimal?>());
        _schedules.GetAsync(_storeId, null, Arg.Any<CancellationToken>()).Returns(new List<SupplySchedule>());
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        return new AiOrderService(_repo, _advisor, _orderCalc, _weather, _events, _schedules, _promos);
    }

    private static OrderLineDto Line(string name, decimal stock, decimal buffer, decimal qty) => new(
        Guid.NewGuid(), name, null, BufferTotal: buffer, BufferGreen: 0, BufferYellow: 0, BufferRed: 0,
        SafetyBuffer: 0, StockOnHand: stock, InTransit: 0, QuantityRaw: qty,
        EventCoefficient: 1, WeatherCoefficient: 1, PromoCoefficient: 1,
        QuantityToOrder: qty, Moq: 1, Usq: 1, Rounding: "none");

    [Fact]
    public async Task GenerateAsync_MoreThanMaxPositions_SendsAndSavesOnlyMostUrgent()
    {
        var sut = Build();
        // 15 orderable lines: coverage = stock / buffer = i/100 → "L00" is the most urgent.
        var lines = Enumerable.Range(0, 15).Reverse()
            .Select(i => Line($"L{i:00}", stock: i, buffer: 100, qty: 5))
            .Append(Line("ZeroQty", 0, 100, 0)) // not orderable → never included
            .ToList();
        _orderCalc.CalculateAsync(_storeId, Arg.Any<CancellationToken>())
            .Returns((new OrderCalcResult(_storeId, DateTime.UtcNow, lines.Count, 15, lines), (string?)null));

        AiOrderContext? sent = null;
        _advisor.AdviseAsync(Arg.Do<AiOrderContext>(c => sent = c), Arg.Any<CancellationToken>())
            .Returns(new AiAdviceResult(Array.Empty<AiAdviceItem>(), "m", 1));
        AiOrderSuggestion? saved = null;
        await _repo.AddAsync(Arg.Do<AiOrderSuggestion>(s => saved = s), Arg.Any<CancellationToken>());
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci => saved);

        await sut.GenerateAsync(_tenantId, _storeId);

        Assert.NotNull(sent);
        Assert.Equal(AiOrderService.MaxPositions, sent!.StockLines.Count);
        Assert.Equal(AiOrderService.MaxPositions, saved!.Items.Count);
        Assert.DoesNotContain(sent.StockLines, s => s.ProductName == "ZeroQty");
        Assert.DoesNotContain(sent.StockLines, s => s.ProductName is "L10" or "L11" or "L14");
        Assert.Equal("L00", sent.StockLines[0].ProductName);
    }
}
