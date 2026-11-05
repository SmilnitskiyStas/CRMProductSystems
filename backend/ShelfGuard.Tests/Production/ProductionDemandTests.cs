using NSubstitute;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Orders.Dtos;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;
using Input = ShelfGuard.Application.Features.Production.ProductionDemandCalculator.Input;

namespace ShelfGuard.Tests.Production;

/// <summary>Pure need formula (TASK-752).</summary>
public sealed class ProductionDemandCalculatorTests
{
    [Fact]
    public void Deficit_RoundedUpToWholeUnits()
    {
        // target 40+5 = 45, stock 12, transit 3, open 5 -> deficit 25
        var r = ProductionDemandCalculator.Compute(new Input(40, 5, 12, 3, 5));
        Assert.Equal(25m, r.Need);
        Assert.False(r.PromoDriven);
        Assert.StartsWith("Буфер", r.Reason);
    }

    [Fact]
    public void FractionalDeficit_CeilsToStep()
    {
        var r = ProductionDemandCalculator.Compute(new Input(10, 0, 5.4m, 0, 0));
        Assert.Equal(5m, r.Need); // 4.6 -> 5
    }

    [Fact]
    public void StockCoversTarget_NoNeed()
    {
        Assert.Equal(0m, ProductionDemandCalculator.Compute(new Input(10, 2, 12, 0, 0)).Need);
        Assert.Equal(0m, ProductionDemandCalculator.Compute(new Input(10, 0, 4, 3, 5)).Need);
    }

    [Fact]
    public void BelowMinimumThreshold_Skipped()
    {
        var r = ProductionDemandCalculator.Compute(new Input(10, 0, 9.5m, 0, 0)); // deficit 0.5 < 1
        Assert.Equal(0m, r.Need);
    }

    [Fact]
    public void PromoCoefficient_ScalesDeficit_AndMarksPromoDriven()
    {
        // deficit 20 x 1.5 = 30
        var r = ProductionDemandCalculator.Compute(new Input(20, 0, 0, 0, 0, PromoCoefficient: 1.5m));
        Assert.Equal(30m, r.Need);
        Assert.True(r.PromoDriven);
        Assert.StartsWith("Акція/подія", r.Reason);
        Assert.Contains("×1.5", r.Reason);
    }

    [Fact]
    public void EventCoefficient_CountsAsPromo_WeatherAlone_DoesNot()
    {
        Assert.True(ProductionDemandCalculator.Compute(new Input(20, 0, 0, 0, 0, EventCoefficient: 1.2m)).PromoDriven);

        var weather = ProductionDemandCalculator.Compute(new Input(20, 0, 0, 0, 0, WeatherCoefficient: 1.3m));
        Assert.False(weather.PromoDriven);
        Assert.Equal(26m, weather.Need);
    }

    [Fact]
    public void Multiplier_IgnoredWhenNoDeficit()
    {
        Assert.Equal(0m, ProductionDemandCalculator.Compute(new Input(10, 0, 10, 0, 0, PromoCoefficient: 2m)).Need);
    }
}

/// <summary>Auto-draft generation: idempotency, source, skip rules, notifications (TASK-752).</summary>
public sealed class ProductionDemandServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly FakeProductionRequestRepo _requests = new();
    private readonly IProductionDemandRepository _demand = Substitute.For<IProductionDemandRepository>();
    private readonly IOrderCalcService _orderCalc = Substitute.For<IOrderCalcService>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly Location _store;
    private readonly Location _prod;
    private readonly Item _dumplings;
    private readonly Item _milk; // not self-produced

    // mutable scenario state
    private decimal _bufferTotal = 40m;
    private decimal _promoCoef = 1m;
    private decimal _onHand;

    public ProductionDemandServiceTests()
    {
        _store = new Location { TenantId = TenantId, Name = "Магазин 1", Type = "shop" };
        _prod = new Location { TenantId = TenantId, Name = "Цех", Type = "production" };
        _dumplings = new Item { TenantId = TenantId, Name = "Вареники", ShelfLifeDays = 5 };
        _milk = new Item { TenantId = TenantId, Name = "Молоко", ShelfLifeDays = 5 };
        _requests.Locations.AddRange([_store, _prod]);
        _requests.Items.AddRange([_dumplings, _milk]);

        _demand.GetActiveProductionLocationsAsync(Arg.Any<CancellationToken>()).Returns(_ => [_prod]);
        _demand.GetActiveStoresAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ => [_store]);
        _demand.GetSelfProducedItemIdsAsync(Arg.Any<CancellationToken>()).Returns(_ => new HashSet<Guid> { _dumplings.Id });
        _demand.GetNonExpiredStockAsync(_store.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_ => new Dictionary<Guid, decimal> { [_dumplings.Id] = _onHand });
        _demand.GetTransferInTransitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(_ => new Dictionary<Guid, decimal>());

        _orderCalc.CalculateAsync(_store.Id, Arg.Any<CancellationToken>())
            .Returns(_ => (Calc(), (string?)null));
    }

    private OrderCalcResult Calc() => new(_store.Id, DateTime.UtcNow, 2, 2,
    [
        Line(_dumplings.Id, _bufferTotal, _promoCoef),
        Line(_milk.Id, 100m, 1m), // not self-produced -> ignored
    ]);

    private static OrderLineDto Line(Guid id, decimal buffer, decimal promo) => new(
        id, "x", null, buffer, 0, 0, 0, 0m, 0m, 0m, 0m, 1m, 1m, promo, 0m, 1m, 1m, "none");

    private ProductionDemandService Svc() => new(_demand, _requests, _orderCalc, _notifications);

    [Fact]
    public async Task NoProductionLocation_Returns422()
    {
        _demand.GetActiveProductionLocationsAsync(Arg.Any<CancellationToken>()).Returns(_ => new List<Location>());

        var (drafts, err, code) = await Svc().GenerateAsync(TenantId, UserId, null);

        Assert.Null(drafts);
        Assert.NotNull(err);
        Assert.Equal(422, code);
    }

    [Fact]
    public async Task NoSelfProducedItems_ReturnsEmpty()
    {
        _demand.GetSelfProducedItemIdsAsync(Arg.Any<CancellationToken>()).Returns(_ => new HashSet<Guid>());

        var (drafts, err, _) = await Svc().GenerateAsync(TenantId, UserId, null);

        Assert.Null(err);
        Assert.Empty(drafts!);
        Assert.Empty(_requests.Requests);
    }

    [Fact]
    public async Task Shortage_CreatesAutoBufferDraft_OnlyForSelfProducedItems_AndNotifies()
    {
        _onHand = 10m;

        var (drafts, err, _) = await Svc().GenerateAsync(TenantId, UserId, null);

        Assert.Null(err);
        var d = Assert.Single(drafts!);
        Assert.Equal("draft", d.Status);
        Assert.Equal("auto_buffer", d.Source);
        Assert.Equal(_prod.Id, d.ProductionLocationId);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(ProductionDemandCalculator.DefaultLeadDays), d.NeededBy);
        var line = Assert.Single(d.Lines);
        Assert.Equal(_dumplings.Id, line.ItemId);
        Assert.Equal(30m, line.RequestedQty);
        Assert.Contains("потреба 30", line.AutoReason);

        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationQueue>(n => n.EventType == ProductionDemandService.AutoDraftEventType
                                           && n.StoreId == _store.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PromoUplift_UsesAutoPromoSource()
    {
        _onHand = 0m;
        _promoCoef = 1.5m;

        var (drafts, _, _) = await Svc().GenerateAsync(TenantId, UserId, null);

        var d = Assert.Single(drafts!);
        Assert.Equal("auto_promo", d.Source);
        Assert.Equal(60m, d.Lines[0].RequestedQty); // 40 x 1.5
    }

    [Fact]
    public async Task StockCoversTarget_CreatesNothing()
    {
        _onHand = 50m;

        var (drafts, _, _) = await Svc().GenerateAsync(TenantId, UserId, null);

        Assert.Empty(drafts!);
        Assert.Empty(_requests.Requests);
        await _notifications.DidNotReceive().EnqueueAsync(Arg.Any<NotificationQueue>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondRun_IsIdempotent_NoDuplicateNoRewriteNoSecondNotification()
    {
        _onHand = 10m;
        await Svc().GenerateAsync(TenantId, UserId, null);

        var (drafts, err, _) = await Svc().GenerateAsync(TenantId, UserId, null);

        Assert.Null(err);
        Assert.Empty(drafts!); // unchanged -> omitted
        Assert.Single(_requests.Requests);
        await _notifications.Received(1).EnqueueAsync(Arg.Any<NotificationQueue>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangedNeed_UpdatesExistingDraftInPlace()
    {
        _onHand = 10m;
        await Svc().GenerateAsync(TenantId, UserId, null);

        _onHand = 25m;
        _promoCoef = 2m; // also flips the source
        var (drafts, _, _) = await Svc().GenerateAsync(TenantId, UserId, null);

        var request = Assert.Single(_requests.Requests);
        var d = Assert.Single(drafts!);
        Assert.Equal(request.Id, d.Id);
        Assert.Equal(30m, d.Lines[0].RequestedQty); // (40-25) x 2
        Assert.Equal("auto_promo", d.Source);
        Assert.Single(request.Lines);
        await _notifications.Received(1).EnqueueAsync(Arg.Any<NotificationQueue>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NeedDisappears_CancelsAutoDraft()
    {
        _onHand = 10m;
        await Svc().GenerateAsync(TenantId, UserId, null);

        _onHand = 60m;
        await Svc().GenerateAsync(TenantId, UserId, null);

        Assert.Equal("cancelled", Assert.Single(_requests.Requests).Status);
    }

    [Fact]
    public async Task SubmittedRequest_CountsAgainstNeed_AndDraftsDoNot()
    {
        _onHand = 10m;
        var submitted = new ProductionRequest
        {
            TenantId = TenantId, StoreLocationId = _store.Id, ProductionLocationId = _prod.Id,
            NeededBy = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            Status = ProductionRequestStatus.Submitted,
        };
        _requests.Requests.Add(submitted);
        submitted.Lines.Add(new ProductionRequestLine
        {
            TenantId = TenantId, RequestId = submitted.Id, ItemId = _dumplings.Id, RequestedQty = 20m,
        });

        var (drafts, _, _) = await Svc().GenerateAsync(TenantId, UserId, null);

        Assert.Equal(10m, Assert.Single(drafts!).Lines[0].RequestedQty); // 40 - 10 - 20
    }

    [Fact]
    public async Task ManualDraft_IsNeverTouched_AutoDraftCreatedSeparately()
    {
        _onHand = 10m;
        var manual = new ProductionRequest
        {
            TenantId = TenantId, StoreLocationId = _store.Id, ProductionLocationId = _prod.Id,
            NeededBy = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            Status = ProductionRequestStatus.Draft, Source = ProductionRequestSource.Manual,
        };
        _requests.Requests.Add(manual);
        manual.Lines.Add(new ProductionRequestLine
        {
            TenantId = TenantId, RequestId = manual.Id, ItemId = _dumplings.Id, RequestedQty = 7m,
        });

        var (drafts, _, _) = await Svc().GenerateAsync(TenantId, UserId, null);

        Assert.NotEqual(manual.Id, Assert.Single(drafts!).Id);
        Assert.Equal(7m, Assert.Single(manual.Lines).RequestedQty);
        Assert.Equal(2, _requests.Requests.Count);
    }

    [Fact]
    public async Task ItemRemovedFromNeed_LineDroppedFromExistingDraft()
    {
        var syrniki = new Item { TenantId = TenantId, Name = "Сирники", ShelfLifeDays = 3 };
        _requests.Items.Add(syrniki);
        _demand.GetSelfProducedItemIdsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new HashSet<Guid> { _dumplings.Id, syrniki.Id });
        var withBoth = true;
        _orderCalc.CalculateAsync(_store.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var lines = new List<OrderLineDto> { Line(_dumplings.Id, 40m, 1m) };
            if (withBoth) lines.Add(Line(syrniki.Id, 10m, 1m));
            return (new OrderCalcResult(_store.Id, DateTime.UtcNow, lines.Count, lines.Count, lines), (string?)null);
        });
        _demand.GetNonExpiredStockAsync(_store.Id, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_ => new Dictionary<Guid, decimal>());

        var (first, _, _) = await Svc().GenerateAsync(TenantId, UserId, null);
        Assert.Equal(2, Assert.Single(first!).Lines.Count);

        withBoth = false;
        var (second, _, _) = await Svc().GenerateAsync(TenantId, UserId, null);

        Assert.Equal(_dumplings.Id, Assert.Single(Assert.Single(second!).Lines).ItemId);
    }

    [Fact]
    public async Task UnknownStoreFilter_Returns404()
    {
        _demand.GetActiveStoresAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ => new List<Location>());

        var (_, err, code) = await Svc().GenerateAsync(TenantId, UserId, Guid.NewGuid());

        Assert.NotNull(err);
        Assert.Equal(404, code);
    }
}
