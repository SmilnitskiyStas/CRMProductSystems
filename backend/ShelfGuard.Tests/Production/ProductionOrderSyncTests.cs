using NSubstitute;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Application.Features.Transfers;
using ShelfGuard.Application.Features.Transfers.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Production;

/// <summary>TASK-761: accepted store requests are auto-planned into Planned production orders.</summary>
public sealed class ProductionOrderSyncTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly FakeProductionRequestRepo _repo = new();
    private readonly IProductionRepository _production = Substitute.For<IProductionRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly ITransferService _transfers = Substitute.For<ITransferService>();
    private readonly Location _store1;
    private readonly Location _store2;
    private readonly Location _prod;
    private readonly Item _bread;
    private readonly Recipe _recipe;

    public ProductionOrderSyncTests()
    {
        _store1 = new Location { TenantId = TenantId, Name = "Магазин 1", Type = "shop" };
        _store2 = new Location { TenantId = TenantId, Name = "Магазин 2", Type = "shop" };
        _prod = new Location { TenantId = TenantId, Name = "Цех", Type = "production" };
        _repo.Locations.AddRange([_store1, _store2, _prod]);
        _bread = new Item { TenantId = TenantId, Name = "Хліб", Unit = "шт", ShelfLifeDays = 3 };
        _repo.Items.Add(_bread);
        _production.GetItemByIdAsync(_bread.Id, Arg.Any<CancellationToken>()).Returns(_bread);
        _recipe = new Recipe { TenantId = TenantId, Name = "Хліб", OutputItemId = _bread.Id, OutputQty = 1m, IsActive = true };
        _repo.Recipes.Add(_recipe);
    }

    private ProductionRequestService Svc() => new(_repo, _production, _notifications, _transfers, Sync());
    private ProductionOrderSyncService Sync() => new(_repo, _notifications);

    private static DateOnly Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(offset));

    private async Task<ProductionRequestDetailDto> SubmittedAsync(Location store, decimal qty, int neededInDays = 2)
    {
        var (r, err, _) = await Svc().CreateAsync(
            new ProductionRequestCreateDto(store.Id, _prod.Id, Day(neededInDays), null, [new(_bread.Id, qty)], true),
            TenantId, UserId);
        Assert.Null(err);
        return r!;
    }

    private async Task<ProductionRequestDetailDto> AcceptedAsync(Location store, decimal qty, int neededInDays = 2)
    {
        var r = await SubmittedAsync(store, qty, neededInDays);
        var (res, err, _) = await Svc().ReviewAsync(r.Id, new ProductionRequestReviewDto([], null), UserId);
        Assert.Null(err);
        return res!;
    }

    private Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> ReReview(
        ProductionRequestDetailDto r, decimal accepted) =>
        Svc().ReviewAsync(r.Id, new ProductionRequestReviewDto(
            [new(r.Lines[0].Id, accepted, accepted < r.Lines[0].RequestedQty ? "ліміт" : null)], null), UserId);

    // ── Rule 1: find-or-create ───────────────────────────────────────────────

    [Fact]
    public async Task Review_CreatesPlannedOrderAndLink()
    {
        var r = await AcceptedAsync(_store1, 10m);

        var order = Assert.Single(_repo.Orders);
        Assert.Equal(ProductionOrderStatus.Planned, order.Status);
        Assert.Equal(10m, order.PlannedQty);
        Assert.Equal(_prod.Id, order.LocationId);
        Assert.Equal(_recipe.Id, order.RecipeId);
        Assert.Equal(Day(2), order.NeededBy);
        Assert.Null(order.PlannedDate);
        var link = Assert.Single(_repo.Links);
        Assert.Equal(10m, link.Qty);
        Assert.Equal(r.Lines[0].Id, link.RequestLineId);
        Assert.Contains((_prod.Id, _bread.Id), _repo.LockedKeys);

        var line = Assert.Single(r.Lines);
        Assert.True(line.HasRecipe);
        var linked = Assert.Single(line.LinkedOrders!);
        Assert.Equal(order.Id, linked.OrderId);
        Assert.Equal("planned", linked.Status);
        Assert.Equal(10m, linked.Qty);
        Assert.Equal("planned", r.ProductionStage);
    }

    [Fact]
    public async Task Review_SameKey_MergesIntoExistingPlannedOrder()
    {
        await AcceptedAsync(_store1, 10m);
        await AcceptedAsync(_store2, 5m);

        var order = Assert.Single(_repo.Orders);
        Assert.Equal(15m, order.PlannedQty);
        Assert.Equal(2, _repo.Links.Count);
        Assert.Equal(15m, _repo.Links.Sum(l => l.Qty));
    }

    [Fact]
    public async Task Review_DifferentNeededBy_CreatesSeparateOrder()
    {
        await AcceptedAsync(_store1, 10m, neededInDays: 2);
        await AcceptedAsync(_store2, 5m, neededInDays: 3);

        Assert.Equal(2, _repo.Orders.Count);
        Assert.Equal([10m, 5m], _repo.Orders.OrderBy(o => o.NeededBy).Select(o => o.PlannedQty).ToArray());
    }

    [Fact]
    public async Task Review_ExistingOrderInProgress_IsNeverModified_NewPlannedOrderCreated()
    {
        await AcceptedAsync(_store1, 10m);
        var first = _repo.Orders.Single();
        first.Status = ProductionOrderStatus.InProgress;

        await AcceptedAsync(_store2, 5m);

        Assert.Equal(2, _repo.Orders.Count);
        Assert.Equal(10m, first.PlannedQty);
        var second = _repo.Orders.Single(o => o.Id != first.Id);
        Assert.Equal(ProductionOrderStatus.Planned, second.Status);
        Assert.Equal(5m, second.PlannedQty);
    }

    [Fact]
    public async Task Review_SeveralActiveRecipes_PicksMostRecentlyCreated()
    {
        var newer = new Recipe
        {
            TenantId = TenantId, Name = "Хліб v2", OutputItemId = _bread.Id, OutputQty = 1m, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(5),
        };
        _repo.Recipes.Add(newer);

        await AcceptedAsync(_store1, 4m);

        Assert.Equal(newer.Id, _repo.Orders.Single().RecipeId);
    }

    // ── Rule 3: sync up / down / reject ──────────────────────────────────────

    [Fact]
    public async Task ReReview_QtyDown_ShrinksPlannedOrderAndLink()
    {
        var r = await AcceptedAsync(_store1, 10m);

        var (res, err, _) = await ReReview(r, 6m);

        Assert.Null(err);
        Assert.Equal("partial", res!.Status);
        Assert.Equal(6m, _repo.Orders.Single().PlannedQty);
        Assert.Equal(6m, _repo.Links.Single().Qty);
    }

    [Fact]
    public async Task ReReview_QtyUp_GrowsPlannedOrderAndLink()
    {
        var r = await AcceptedAsync(_store1, 10m);
        var (down, _, _) = await ReReview(r, 4m);

        var (up, err, _) = await ReReview(down!, 9m);

        Assert.Null(err);
        Assert.Equal(9m, _repo.Orders.Single().PlannedQty);
        var link = Assert.Single(_repo.Links);
        Assert.Equal(9m, link.Qty);
        Assert.Equal(9m, up!.Lines[0].LinkedOrders!.Single().Qty);
    }

    [Fact]
    public async Task ReReview_IsIdempotent()
    {
        var r = await AcceptedAsync(_store1, 10m);
        await Svc().ReviewAsync(r.Id, new ProductionRequestReviewDto([], null), UserId);
        await Sync().SyncAsync(_repo.Requests.Single(), UserId);

        Assert.Single(_repo.Orders);
        Assert.Equal(10m, _repo.Orders.Single().PlannedQty);
        Assert.Single(_repo.Links);
    }

    [Fact]
    public async Task ReReview_QtyDown_WithMergedOrder_KeepsOrderForOtherRequest()
    {
        var a = await AcceptedAsync(_store1, 10m);
        await AcceptedAsync(_store2, 5m);

        await ReReview(a, 7m);

        var order = Assert.Single(_repo.Orders);
        Assert.Equal(ProductionOrderStatus.Planned, order.Status);
        Assert.Equal(12m, order.PlannedQty);
    }

    [Fact]
    public async Task ReReview_AllZero_RejectsAndCancelsEmptyOrder()
    {
        var r = await AcceptedAsync(_store1, 10m);

        var (res, _, _) = await ReReview(r, 0m);

        Assert.Equal("rejected", res!.Status);
        var order = Assert.Single(_repo.Orders);
        Assert.Equal(ProductionOrderStatus.Cancelled, order.Status);
        Assert.Equal(0m, order.PlannedQty);
        Assert.Contains("автоматично", order.Notes);
        Assert.Empty(_repo.Links);
        Assert.Equal("none", res.ProductionStage);
    }

    [Fact]
    public async Task ReReview_QtyDownBelowInProgressOrder_LeavesOrderUntouched()
    {
        var r = await AcceptedAsync(_store1, 10m);
        _repo.Orders.Single().Status = ProductionOrderStatus.InProgress;

        await ReReview(r, 4m);

        Assert.Equal(10m, _repo.Orders.Single().PlannedQty);
        Assert.Equal(10m, _repo.Links.Single().Qty);
    }

    [Fact]
    public async Task ReReview_QtyUpWhileOrderInProgress_AddsRemainderToNewPlannedOrder()
    {
        var r = await AcceptedAsync(_store1, 10m);
        var (down, _, _) = await ReReview(r, 6m);
        _repo.Orders.Single().Status = ProductionOrderStatus.InProgress;

        await ReReview(down!, 10m);

        Assert.Equal(2, _repo.Orders.Count);
        var planned = _repo.Orders.Single(o => o.Status == ProductionOrderStatus.Planned);
        Assert.Equal(4m, planned.PlannedQty);
        Assert.Equal(6m, _repo.Orders.Single(o => o.Status == ProductionOrderStatus.InProgress).PlannedQty);
        Assert.Equal(10m, _repo.Links.Sum(l => l.Qty));
    }

    [Fact]
    public async Task Review_ShippedRequest_IsFinal()
    {
        var r = await AcceptedAsync(_store1, 10m);
        _repo.Requests.Single().Status = ProductionRequestStatus.Fulfilled;

        var (_, _, code) = await ReReview(r, 3m);

        Assert.Equal(409, code);
    }

    // ── Rule 2: no recipe ────────────────────────────────────────────────────

    [Fact]
    public async Task Review_NoRecipe_AcceptsWithoutOrder_AwaitingOrder()
    {
        _repo.Recipes.Clear();

        var r = await AcceptedAsync(_store1, 10m);

        Assert.Equal("accepted", r.Status);
        Assert.Empty(_repo.Orders);
        Assert.Empty(_repo.Links);
        Assert.False(r.Lines[0].HasRecipe);
        Assert.Empty(r.Lines[0].LinkedOrders!);
        Assert.Equal("awaiting_order", r.ProductionStage);
    }

    [Fact]
    public async Task SyncEndpoint_AfterRecipeAppears_CreatesOrder()
    {
        _repo.Recipes.Clear();
        var r = await AcceptedAsync(_store1, 10m);
        _repo.Recipes.Add(_recipe);

        var (res, err, _) = await Svc().SyncOrdersAsync(r.Id, UserId);

        Assert.Null(err);
        Assert.Equal("planned", res!.ProductionStage);
        Assert.Equal(10m, _repo.Orders.Single().PlannedQty);
    }

    [Fact]
    public async Task SyncEndpoint_OnSubmittedRequest_Returns409()
    {
        var r = await SubmittedAsync(_store1, 3m);
        var (_, _, code) = await Svc().SyncOrdersAsync(r.Id, UserId);
        Assert.Equal(409, code);
    }

    // ── Rule 4: cancelling an order unlinks its lines ───────────────────────

    [Fact]
    public async Task CancelOrder_UnlinksLines_StageAwaitingOrder_ExplicitSyncRecreates()
    {
        var r = await AcceptedAsync(_store1, 10m);
        var order = _repo.Orders.Single();
        _production.GetOrderByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var productionSvc = new ProductionService(_production, Sync());

        var (_, err, _) = await productionSvc.CancelOrderAsync(order.Id);

        Assert.Null(err);
        Assert.Equal(ProductionOrderStatus.Cancelled, order.Status);
        Assert.Empty(_repo.Links);
        var detail = await Svc().GetByIdAsync(r.Id);
        Assert.Equal("awaiting_order", detail!.ProductionStage);
        Assert.Empty(detail.Lines[0].LinkedOrders!);

        var (re, _, _) = await Svc().SyncOrdersAsync(r.Id, UserId);

        Assert.Equal("planned", re!.ProductionStage);
        Assert.Equal(2, _repo.Orders.Count);
        Assert.Equal(10m, _repo.Orders.Single(o => o.Status == ProductionOrderStatus.Planned).PlannedQty);
    }

    // ── Rule 5: stage derivation ─────────────────────────────────────────────

    private static ProductionRequestLine Line(decimal accepted) =>
        new() { TenantId = TenantId, RequestedQty = accepted, AcceptedQty = accepted };

    private static ProductionOrderRequestLine Link(ProductionRequestLine l, ProductionOrderStatus st, decimal? qty = null) => new()
    {
        TenantId = TenantId,
        RequestLineId = l.Id,
        Qty = qty ?? l.AcceptedQty!.Value,
        ProductionOrder = new ProductionOrder { Status = st },
    };

    [Theory]
    [InlineData("draft")]
    [InlineData("submitted")]
    [InlineData("rejected")]
    [InlineData("cancelled")]
    public void Stage_NonAcceptedStatuses_AreNone(string status) =>
        Assert.Equal("none", ProductionRequestStages.Derive(status, [Line(5m)], []));

    [Fact]
    public void Stage_Fulfilled_IsShipped() =>
        Assert.Equal("shipped", ProductionRequestStages.Derive("fulfilled", [Line(5m)], []));

    [Fact]
    public void Stage_AllStages()
    {
        var a = Line(5m);
        var b = Line(3m);
        Assert.Equal("awaiting_order", ProductionRequestStages.Derive("accepted", [a, b], [Link(a, ProductionOrderStatus.Planned)]));
        Assert.Equal("awaiting_order", ProductionRequestStages.Derive("accepted", [a], [Link(a, ProductionOrderStatus.Cancelled)]));
        Assert.Equal("awaiting_order", ProductionRequestStages.Derive("partial", [a], [Link(a, ProductionOrderStatus.Planned, 2m)]));
        Assert.Equal("planned", ProductionRequestStages.Derive("accepted", [a, b],
            [Link(a, ProductionOrderStatus.Planned), Link(b, ProductionOrderStatus.Planned)]));
        Assert.Equal("in_production", ProductionRequestStages.Derive("accepted", [a, b],
            [Link(a, ProductionOrderStatus.InProgress), Link(b, ProductionOrderStatus.Planned)]));
        Assert.Equal("in_production", ProductionRequestStages.Derive("accepted", [a, b],
            [Link(a, ProductionOrderStatus.Done), Link(b, ProductionOrderStatus.Planned)]));
        Assert.Equal("ready", ProductionRequestStages.Derive("accepted", [a, b],
            [Link(a, ProductionOrderStatus.Done), Link(b, ProductionOrderStatus.Done)]));
    }

    [Fact]
    public void Stage_ZeroAcceptedLines_AreIgnored()
    {
        var a = Line(5m);
        var zero = Line(0m);
        Assert.Equal("planned", ProductionRequestStages.Derive("partial", [a, zero], [Link(a, ProductionOrderStatus.Planned)]));
    }

    [Fact]
    public async Task List_CarriesProductionStage()
    {
        await AcceptedAsync(_store1, 10m);
        var list = await Svc().ListAsync(null, null, null, null, null);
        Assert.Equal("planned", Assert.Single(list).ProductionStage);
    }

    // ── Rule 6: ship guard + batch ───────────────────────────────────────────

    private void FefoStock(decimal qty) =>
        _production.GetFefoOrderedAsync(_bread.Id, _prod.Id, Arg.Any<CancellationToken>())
            .Returns([new ProductStock
            {
                TenantId = TenantId, ProductId = _bread.Id, StoreId = _prod.Id, Quantity = qty, QuantityInitial = qty,
                ExpiryDate = Day(5),
            }]);

    private TransferDto Transfer() =>
        new(Guid.NewGuid(), _prod.Id, "", _store1.Id, "", "production_to_store", "in_transit", null, DateTime.UtcNow, []);

    [Fact]
    public async Task Ship_WhileOrderNotDone_Returns409()
    {
        var r = await AcceptedAsync(_store1, 10m);
        FefoStock(100m);

        var (_, err, code) = await Svc().ShipAsync(r.Id, TenantId, UserId);

        Assert.Equal(409, code);
        Assert.Equal("Виробництво ще не завершене.", err);
        await _transfers.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateTransferRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ship_WhenOrderDone_Works()
    {
        var r = await AcceptedAsync(_store1, 10m);
        _repo.Orders.Single().Status = ProductionOrderStatus.Done;
        FefoStock(100m);
        var t = Transfer();
        _transfers.CreateAsync(TenantId, UserId, Arg.Any<CreateTransferRequest>(), Arg.Any<CancellationToken>())
            .Returns((t, (string?)null));

        var (res, err, _) = await Svc().ShipAsync(r.Id, TenantId, UserId);

        Assert.Null(err);
        Assert.Equal("fulfilled", res!.Status);
        Assert.Equal("shipped", res.ProductionStage);
    }

    [Fact]
    public async Task ShipOrderRequests_OrderNotDone_Returns409()
    {
        await AcceptedAsync(_store1, 10m);
        var order = _repo.Orders.Single();
        _production.GetOrderByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (_, _, code) = await Svc().ShipOrderRequestsAsync(order.Id, TenantId, UserId);

        Assert.Equal(409, code);
    }

    [Fact]
    public async Task ShipOrderRequests_OneFailure_DoesNotAbortBatch()
    {
        var a = await AcceptedAsync(_store1, 10m);
        var b = await AcceptedAsync(_store2, 5m);
        var order = _repo.Orders.Single();
        order.Status = ProductionOrderStatus.Done;
        _production.GetOrderByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        FefoStock(100m);
        var good = Transfer();
        _transfers.CreateAsync(TenantId, UserId,
                Arg.Is<CreateTransferRequest>(x => x.ToStoreId == _store1.Id), Arg.Any<CancellationToken>())
            .Returns(((TransferDto?)null, "boom"));
        _transfers.CreateAsync(TenantId, UserId,
                Arg.Is<CreateTransferRequest>(x => x.ToStoreId == _store2.Id), Arg.Any<CancellationToken>())
            .Returns((good, (string?)null));

        var (results, err, _) = await Svc().ShipOrderRequestsAsync(order.Id, TenantId, UserId);

        Assert.Null(err);
        Assert.Equal(2, results!.Count);
        var bad = results.Single(x => x.RequestId == a.Id);
        Assert.False(bad.Ok);
        Assert.Equal("boom", bad.Error);
        Assert.Equal("Магазин 1", bad.StoreLocationName);
        var ok = results.Single(x => x.RequestId == b.Id);
        Assert.True(ok.Ok);
        Assert.Equal(good.Id, ok.TransferId);
        Assert.Equal("accepted", _repo.Requests.Single(x => x.Id == a.Id).Status);
        Assert.Equal("fulfilled", _repo.Requests.Single(x => x.Id == b.Id).Status);
    }

    [Fact]
    public async Task ShipOrderRequests_RequestWithOtherOpenOrder_IsReportedNotShipped()
    {
        var a = await AcceptedAsync(_store1, 10m);
        var order = _repo.Orders.Single();
        order.Status = ProductionOrderStatus.Done;
        // a second (still planned) order feeds the same request
        var other = new ProductionOrder { TenantId = TenantId, Status = ProductionOrderStatus.Planned, LocationId = _prod.Id };
        _repo.Orders.Add(other);
        _repo.Links.Add(new ProductionOrderRequestLine
        {
            TenantId = TenantId, ProductionOrderId = other.Id, RequestLineId = a.Lines[0].Id, Qty = 1m, ProductionOrder = other,
        });
        _production.GetOrderByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (results, _, _) = await Svc().ShipOrderRequestsAsync(order.Id, TenantId, UserId);

        var r = Assert.Single(results!);
        Assert.False(r.Ok);
        await _transfers.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateTransferRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Rule 9: ready notification ───────────────────────────────────────────

    [Fact]
    public async Task OrderCompleted_AllOrdersDone_EnqueuesReadyOncePerRequest()
    {
        await AcceptedAsync(_store1, 10m);
        await AcceptedAsync(_store2, 5m);
        var order = _repo.Orders.Single();
        order.Status = ProductionOrderStatus.Done;
        var sent = new List<NotificationQueue>();
        await _notifications.EnqueueAsync(Arg.Do<NotificationQueue>(sent.Add), Arg.Any<CancellationToken>());
        sent.Clear(); // drop the 'submitted' notifications

        await Sync().NotifyOrderCompletedAsync(order.Id);

        Assert.Equal(2, sent.Count);
        Assert.All(sent, n => Assert.Equal("production_request.ready", n.EventType));
        Assert.Equal([_store1.Id, _store2.Id], sent.Select(n => n.StoreId!.Value).OrderBy(x => x == _store1.Id ? 0 : 1).ToArray());
    }

    [Fact]
    public async Task OrderCompleted_RequestStillHasOpenOrder_NoNotification()
    {
        var a = await AcceptedAsync(_store1, 10m);
        var order = _repo.Orders.Single();
        order.Status = ProductionOrderStatus.Done;
        var other = new ProductionOrder { TenantId = TenantId, Status = ProductionOrderStatus.Planned, LocationId = _prod.Id };
        _repo.Orders.Add(other);
        _repo.Links.Add(new ProductionOrderRequestLine
        {
            TenantId = TenantId, ProductionOrderId = other.Id, RequestLineId = a.Lines[0].Id, Qty = 1m, ProductionOrder = other,
        });
        _notifications.ClearReceivedCalls();

        await Sync().NotifyOrderCompletedAsync(order.Id);

        await _notifications.DidNotReceive().EnqueueAsync(Arg.Any<NotificationQueue>(), Arg.Any<CancellationToken>());
    }
}
