using NSubstitute;
using ShelfGuard.Application.Features.AiOrders;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Orders.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.AiOrders;

/// <summary>
/// Block 7 pre-launch audit (AI Orders / AI Assistant). Covers the two behaviors found and
/// fixed during the audit: the N+1 in GetListAsync (regression guard) and graceful Claude
/// API failure handling in GenerateAsync (must not throw / must not 500 the whole endpoint).
/// </summary>
public sealed class AiOrderServiceTests
{
    private readonly IAiOrderRepository _repo = Substitute.For<IAiOrderRepository>();
    private readonly IAiOrderAdvisor _advisor = Substitute.For<IAiOrderAdvisor>();
    private readonly IOrderCalcService _orderCalc = Substitute.For<IOrderCalcService>();
    private readonly IWeatherRepository _weather = Substitute.For<IWeatherRepository>();
    private readonly IEventRepository _events = Substitute.For<IEventRepository>();
    private readonly ISupplyScheduleRepository _schedules = Substitute.For<ISupplyScheduleRepository>();
    private readonly ICannibalizationRepository _promos = Substitute.For<ICannibalizationRepository>();
    private readonly AiOrderService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public AiOrderServiceTests()
    {
        _sut = new AiOrderService(_repo, _advisor, _orderCalc, _weather, _events, _schedules, _promos);

        // Common plumbing so GenerateAsync tests don't need to repeat it.
        _repo.GetStoreNameAsync(_storeId, Arg.Any<CancellationToken>()).Returns("Store 1");
        _weather.GetForecastAsync(_storeId, Arg.Any<CancellationToken>()).Returns(new List<WeatherData>());
        _events.GetAsync(Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Is<Guid[]>(a => a.Length == 1 && a[0] == _storeId), Arg.Any<CancellationToken>())
            .Returns(new List<DemandEvent>());
        _promos.GetActivePromoCoefficientsAsync(_storeId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());
        _repo.GetAdu30Async(_storeId, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, decimal?>());
        _schedules.GetAsync(_storeId, null, Arg.Any<CancellationToken>()).Returns(new List<SupplySchedule>());
    }

    // ── GetListAsync — N+1 regression guard ──────────────────────────────────

    [Fact]
    public async Task GetListAsync_ReadsItemCountFromEagerLoadedList_NeverCallsGetByIdPerRow()
    {
        var suggestions = new List<AiOrderSuggestion>
        {
            BuildSuggestion(itemsCount: 3),
            BuildSuggestion(itemsCount: 5),
            BuildSuggestion(itemsCount: 0),
        };
        _repo.GetListAsync(null, 30, Arg.Any<CancellationToken>()).Returns(suggestions);

        var result = await _sut.GetListAsync(null);

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { 3, 5, 0 }, result.Select(r => r.ItemsCount));

        // The regression this guards: GetListAsync used to loop and call GetByIdAsync once
        // per suggestion just to read Items.Count (up to 30 extra full round-trips).
        await _repo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetListAsync_EmptyList_ReturnsEmpty()
    {
        _repo.GetListAsync(null, 30, Arg.Any<CancellationToken>()).Returns(new List<AiOrderSuggestion>());

        var result = await _sut.GetListAsync(null);

        Assert.Empty(result);
    }

    // ── GenerateAsync — Claude API failure handling (must degrade, never throw) ──

    [Fact]
    public async Task GenerateAsync_ApiKeyNotConfigured_ReturnsReadableError_DoesNotCallOrderCalc()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(false);

        var (order, error) = await _sut.GenerateAsync(_tenantId, _storeId);

        Assert.Null(order);
        Assert.NotNull(error);
        Assert.Contains("Claude API", error);
        // Should short-circuit before touching the order formula / repo at all.
        await _orderCalc.DidNotReceive().CalculateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_AdvisorThrows_ReturnsError_DoesNotThrow()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _orderCalc.CalculateAsync(_storeId, Arg.Any<CancellationToken>())
            .Returns((BuildCalcResult(), (string?)null));
        _advisor.AdviseAsync(Arg.Any<AiOrderContext>(), Arg.Any<CancellationToken>())
            .Returns<AiAdviceResult>(_ => throw new InvalidOperationException("Anthropic API error 500"));

        var (order, error) = await _sut.GenerateAsync(_tenantId, _storeId);

        Assert.Null(order);
        Assert.NotNull(error);
        Assert.Contains("AI сервіс недоступний", error);
        // No suggestion should be persisted when the AI call fails.
        await _repo.DidNotReceive().AddAsync(Arg.Any<AiOrderSuggestion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateAsync_AdvisorThrowsCreditBalanceError_ReturnsUkrainianBillingMessage()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _orderCalc.CalculateAsync(_storeId, Arg.Any<CancellationToken>())
            .Returns((BuildCalcResult(), (string?)null));
        _advisor.AdviseAsync(Arg.Any<AiOrderContext>(), Arg.Any<CancellationToken>())
            .Returns<AiAdviceResult>(_ => throw new InvalidOperationException(
                "Your credit balance is too low to access the Anthropic API"));

        var (order, error) = await _sut.GenerateAsync(_tenantId, _storeId);

        Assert.Null(order);
        Assert.Contains("кредитів", error);
    }

    [Fact]
    public async Task GenerateAsync_NothingToOrder_ReturnsError_DoesNotCallAdvisor()
    {
        _advisor.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        _orderCalc.CalculateAsync(_storeId, Arg.Any<CancellationToken>())
            .Returns((BuildCalcResult(quantityToOrder: 0), (string?)null));

        var (order, error) = await _sut.GenerateAsync(_tenantId, _storeId);

        Assert.Null(order);
        Assert.NotNull(error);
        // A store with nothing to order must not spend a Claude API call.
        await _advisor.DidNotReceive().AdviseAsync(Arg.Any<AiOrderContext>(), Arg.Any<CancellationToken>());
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private AiOrderSuggestion BuildSuggestion(int itemsCount) => new()
    {
        TenantId = _tenantId,
        StoreId = _storeId,
        OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
        AiModel = "claude-sonnet-4-6",
        Items = Enumerable.Range(0, itemsCount)
            .Select(_ => new AiOrderSuggestionItem { ProductId = Guid.NewGuid() })
            .ToList(),
    };

    private OrderCalcResult BuildCalcResult(decimal quantityToOrder = 10) => new(
        _storeId, DateTime.UtcNow, ProductsEvaluated: 1, LinesToOrder: quantityToOrder > 0 ? 1 : 0,
        Lines:
        [
            new OrderLineDto(
                _productId, "Test Product", "1234567890123",
                BufferTotal: 100, BufferGreen: 60, BufferYellow: 30, BufferRed: 10,
                SafetyBuffer: 5, StockOnHand: 20, InTransit: 0, QuantityRaw: quantityToOrder,
                EventCoefficient: 1, WeatherCoefficient: 1, PromoCoefficient: 1,
                QuantityToOrder: quantityToOrder, Moq: 1, Usq: 1, Rounding: "none"),
        ]);
}
