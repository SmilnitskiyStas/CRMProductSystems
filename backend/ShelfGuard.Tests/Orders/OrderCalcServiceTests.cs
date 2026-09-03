using NSubstitute;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Orders.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Orders;

/// <summary>
/// Phase 4 (plan 1-partitioned-book.md, D5) — <c>OrderCalcService.CalculateAsync</c> now folds
/// open B2B marketplace orders into the single <c>InTransit</c> term the order formula already
/// subtracts (the double-order fix), and breaks the marketplace slice out as
/// <c>InTransitFromMarketplace</c> for the order-review tooltip. Formula ladder itself is
/// covered by <c>OrderFormulaTests</c>; the repo query by
/// <c>OrderCalcRepositoryOpenMarketplaceInTransitTests</c>.
/// </summary>
public sealed class OrderCalcServiceTests
{
    private readonly IOrderCalcRepository _repo = Substitute.For<IOrderCalcRepository>();
    private readonly IEventRepository _events = Substitute.For<IEventRepository>();
    private readonly IWeatherRepository _weather = Substitute.For<IWeatherRepository>();
    private readonly ICannibalizationRepository _promo = Substitute.For<ICannibalizationRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly OrderCalcService _sut;

    private readonly Guid _storeId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public OrderCalcServiceTests()
    {
        _sut = new OrderCalcService(_repo, _events, _weather, _promo, _tenant);

        _tenant.TenantId.Returns(_tenantId);
        _repo.StoreExistsAsync(_storeId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetBuffersAsync(_storeId, Arg.Any<CancellationToken>()).Returns(new List<ProductBuffer>
        {
            new()
            {
                TenantId = _tenantId,
                StoreId = _storeId,
                ProductId = _productId,
                BufferTotal = 100m,
                Product = new Item { TenantId = _tenantId, Name = "Товар", ManagementType = "MTS", SafetyBuffer = 0m },
            },
        });
        _repo.GetStockOnHandAsync(_storeId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [_productId] = 20m });
        _repo.GetInTransitAsync(_storeId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());
        _repo.GetOpenMarketplaceInTransitAsync(
                _storeId, Arg.Any<IReadOnlyCollection<Guid>>(), _tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());
        _repo.GetMoqUsqAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, (decimal, decimal)> { [_productId] = (1m, 1m) });

        _events.GetCandidatesForDateAsync(_storeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<DemandEvent>());
        _weather.GetForDateAsync(_storeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((WeatherData?)null);
        _weather.GetCoefficientsAsync(Arg.Any<CancellationToken>()).Returns(new List<WeatherCoefficient>());
        _promo.GetActivePromoCoefficientsAsync(_storeId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());
    }

    private async Task<OrderLineDto> LineAsync()
    {
        var (result, error) = await _sut.CalculateAsync(_storeId);
        Assert.Null(error);
        return Assert.Single(result!.Lines);
    }

    [Fact]
    public async Task Open_marketplace_order_lowers_raw()
    {
        _repo.GetOpenMarketplaceInTransitAsync(
                _storeId, Arg.Any<IReadOnlyCollection<Guid>>(), _tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [_productId] = 30m });

        var line = await LineAsync();

        // Raw = Buffer 100 + BB 0 − Stock 20 − InTransit 30 = 50 (was 80 with no marketplace order).
        Assert.Equal(50m, line.QuantityRaw);
        Assert.Equal(30m, line.InTransit);
        Assert.Equal(30m, line.InTransitFromMarketplace);
    }

    [Fact]
    public async Task Draft_receipts_and_open_marketplace_order_both_count()
    {
        _repo.GetInTransitAsync(_storeId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [_productId] = 5m });
        _repo.GetOpenMarketplaceInTransitAsync(
                _storeId, Arg.Any<IReadOnlyCollection<Guid>>(), _tenantId, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [_productId] = 10m });

        var line = await LineAsync();

        // Additive: 5 draft + 10 marketplace = 15 combined; Raw = 100 − 20 − 15 = 65.
        Assert.Equal(15m, line.InTransit);
        Assert.Equal(10m, line.InTransitFromMarketplace);
        Assert.Equal(65m, line.QuantityRaw);
    }

    [Fact]
    public async Task No_marketplace_order_leaves_breakdown_at_zero()
    {
        var line = await LineAsync();

        Assert.Equal(0m, line.InTransit);
        Assert.Equal(0m, line.InTransitFromMarketplace);
        Assert.Equal(80m, line.QuantityRaw);
    }

    [Fact]
    public async Task Missing_tenant_context_skips_the_marketplace_query()
    {
        _tenant.TenantId.Returns((Guid?)null);

        var line = await LineAsync();

        Assert.Equal(80m, line.QuantityRaw);
        await _repo.DidNotReceive().GetOpenMarketplaceInTransitAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
