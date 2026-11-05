using NSubstitute;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Application.Features.Transfers;
using ShelfGuard.Application.Features.Transfers.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Production;

internal sealed class FakeProductionRequestRepo : IProductionRequestRepository
{
    public List<ProductionRequest> Requests { get; } = [];
    public List<Location> Locations { get; } = [];
    public List<Recipe> Recipes { get; } = [];
    public List<Item> Items { get; } = [];
    public List<ProductionOrderRequestLine> Links { get; } = [];
    public List<ProductionOrder> Orders { get; } = [];
    public List<(Guid LocationId, Guid ItemId)> LockedKeys { get; } = [];
    public HashSet<Guid> LinesInActiveOrders { get; } = [];
    public int Saves { get; private set; }

    public Task<ProductionRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Hydrate(Requests.FirstOrDefault(r => r.Id == id)));

    public Task<List<ProductionRequest>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult(Requests.Where(r => ids.Contains(r.Id)).Select(r => Hydrate(r)!).ToList());

    public Task<List<ProductionRequest>> ListAsync(
        Guid? storeLocationId, Guid? productionLocationId, IReadOnlyCollection<string>? statuses,
        DateOnly? neededFrom, DateOnly? neededTo, CancellationToken ct = default) =>
        Task.FromResult(Requests
            .Where(r => (storeLocationId is null || r.StoreLocationId == storeLocationId)
                        && (productionLocationId is null || r.ProductionLocationId == productionLocationId)
                        && (statuses is null || statuses.Contains(r.Status)))
            .Select(r => Hydrate(r)!).ToList());

    public Task AddAsync(ProductionRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }

    public Task AddLinesAsync(IEnumerable<ProductionRequestLine> lines, CancellationToken ct = default)
    {
        foreach (var l in lines)
            Requests.First(r => r.Id == l.RequestId).Lines.Add(l);
        return Task.CompletedTask;
    }

    public void RemoveLines(IEnumerable<ProductionRequestLine> lines)
    {
        foreach (var l in lines.ToList())
            Requests.First(r => r.Id == l.RequestId).Lines.Remove(l);
    }

    public Task<Location?> GetLocationAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Locations.FirstOrDefault(l => l.Id == id));

    public Task<Recipe?> GetActiveRecipeForOutputAsync(Guid itemId, CancellationToken ct = default) =>
        Task.FromResult(Recipes.Where(r => r.IsActive && r.OutputItemId == itemId)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefault());

    public Task<HashSet<Guid>> GetItemIdsWithActiveRecipeAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default) =>
        Task.FromResult(Recipes.Where(r => r.IsActive && itemIds.Contains(r.OutputItemId)).Select(r => r.OutputItemId).ToHashSet());

    public Task<HashSet<Guid>> GetLineIdsInActiveOrdersAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken ct = default) =>
        Task.FromResult(lineIds.Where(LinesInActiveOrders.Contains).ToHashSet());

    public Task AddOrderLinksAsync(IEnumerable<ProductionOrderRequestLine> links, CancellationToken ct = default)
    {
        Links.AddRange(links);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default) => work();

    public Task LockOrderKeysAsync(IEnumerable<(Guid LocationId, Guid ItemId)> keys, CancellationToken ct = default)
    {
        LockedKeys.AddRange(keys);
        return Task.CompletedTask;
    }

    private IEnumerable<ProductionRequestLine> AllLines => Requests.SelectMany(r => r.Lines);

    private List<ProductionOrderRequestLine> Hydrated(IEnumerable<ProductionOrderRequestLine> links)
    {
        var list = links.ToList();
        foreach (var l in list) l.RequestLine ??= AllLines.FirstOrDefault(x => x.Id == l.RequestLineId);
        return list;
    }

    public Task<List<ProductionOrderRequestLine>> GetLinksForLinesAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken ct = default) =>
        Task.FromResult(Hydrated(Links.Where(l => lineIds.Contains(l.RequestLineId))));

    public Task<List<ProductionOrderRequestLine>> GetLinksForRequestsAsync(IReadOnlyCollection<Guid> requestIds, CancellationToken ct = default)
    {
        var lineIds = AllLines.Where(l => requestIds.Contains(l.RequestId)).Select(l => l.Id).ToHashSet();
        return Task.FromResult(Hydrated(Links.Where(l => lineIds.Contains(l.RequestLineId))));
    }

    public Task<List<ProductionOrderRequestLine>> GetLinksForOrderAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(Hydrated(Links.Where(l => l.ProductionOrderId == orderId)));

    public void RemoveOrderLinks(IEnumerable<ProductionOrderRequestLine> links)
    {
        foreach (var l in links.ToList()) Links.Remove(l);
    }

    public Task AddOrderAsync(ProductionOrder order, CancellationToken ct = default)
    {
        Orders.Add(order);
        return Task.CompletedTask;
    }

    public Task<ProductionOrder?> FindPlannedOrderAsync(Guid locationId, Guid itemId, DateOnly neededBy, CancellationToken ct = default) =>
        Task.FromResult(Orders
            .Where(o => o.Status == ProductionOrderStatus.Planned && o.LocationId == locationId && o.NeededBy == neededBy
                        && Recipes.Any(r => r.Id == o.RecipeId && r.OutputItemId == itemId))
            .OrderBy(o => o.CreatedAt).FirstOrDefault());

    private ProductionRequest? Hydrate(ProductionRequest? r)
    {
        if (r is null) return null;
        r.StoreLocation = Locations.FirstOrDefault(l => l.Id == r.StoreLocationId);
        r.ProductionLocation = Locations.FirstOrDefault(l => l.Id == r.ProductionLocationId);
        foreach (var l in r.Lines) l.Item ??= Items.FirstOrDefault(i => i.Id == l.ItemId);
        return r;
    }
}

public sealed class ProductionRequestServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly FakeProductionRequestRepo _repo = new();
    private readonly IProductionRepository _production = Substitute.For<IProductionRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly ITransferService _transfers = Substitute.For<ITransferService>();
    private readonly Location _store;
    private readonly Location _prod;
    private readonly Item _bread;

    public ProductionRequestServiceTests()
    {
        _store = new Location { TenantId = TenantId, Name = "Магазин 1", Type = "shop" };
        _prod = new Location { TenantId = TenantId, Name = "Цех", Type = "production" };
        _repo.Locations.AddRange([_store, _prod]);
        _bread = new Item { TenantId = TenantId, Name = "Хліб", ShelfLifeDays = 3 };
        _repo.Items.Add(_bread);
        _production.GetItemByIdAsync(_bread.Id, Arg.Any<CancellationToken>()).Returns(_bread);
    }

    private ProductionRequestService Svc() => new(_repo, _production, _notifications, _transfers,
        new ProductionOrderSyncService(_repo, _notifications));

    private static DateOnly Tomorrow => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    private ProductionRequestCreateDto CreateDto(bool submit = false, decimal qty = 10m) => new(
        _store.Id, _prod.Id, Tomorrow, null,
        [new ProductionRequestLineCreateDto(_bread.Id, qty)], submit);

    private async Task<ProductionRequestDetailDto> SubmittedAsync(decimal qty = 10m)
    {
        var (r, err, _) = await Svc().CreateAsync(CreateDto(submit: true, qty: qty), TenantId, UserId);
        Assert.Null(err);
        return r!;
    }

    // ── Create / validation ──────────────────────────────────────────────────

    [Fact]
    public async Task Create_ValidDraft_PersistsLinesAndDoesNotNotify()
    {
        var (r, err, code) = await Svc().CreateAsync(CreateDto(), TenantId, UserId);

        Assert.Null(err);
        Assert.Null(code);
        Assert.Equal("draft", r!.Status);
        Assert.Equal("manual", r.Source);
        Assert.Single(r.Lines);
        await _notifications.DidNotReceive().EnqueueAsync(Arg.Any<NotificationQueue>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_DestinationNotProductionLocation_Returns422()
    {
        var other = new Location { TenantId = TenantId, Name = "Інший магазин", Type = "shop" };
        _repo.Locations.Add(other);
        var dto = CreateDto() with { ProductionLocationId = other.Id };

        var (r, err, code) = await Svc().CreateAsync(dto, TenantId, UserId);

        Assert.Null(r);
        Assert.Equal(422, code);
        Assert.NotNull(err);
    }

    [Fact]
    public async Task Create_NoLines_Returns400()
    {
        var dto = CreateDto() with { Lines = [] };
        var (_, _, code) = await Svc().CreateAsync(dto, TenantId, UserId);
        Assert.Equal(400, code);
    }

    [Fact]
    public async Task Create_NeededByInPast_Returns400()
    {
        var dto = CreateDto() with { NeededBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)) };
        var (_, _, code) = await Svc().CreateAsync(dto, TenantId, UserId);
        Assert.Equal(400, code);
    }

    [Fact]
    public async Task Create_DuplicateItems_AreMerged()
    {
        var dto = CreateDto() with
        {
            Lines = [new(_bread.Id, 4m), new(_bread.Id, 6m)],
        };
        var (r, _, _) = await Svc().CreateAsync(dto, TenantId, UserId);
        Assert.Equal(10m, Assert.Single(r!.Lines).RequestedQty);
    }

    // ── Submit + notification ────────────────────────────────────────────────

    [Fact]
    public async Task Submit_EnqueuesNotificationForProductionLocation()
    {
        NotificationQueue? sent = null;
        await _notifications.EnqueueAsync(Arg.Do<NotificationQueue>(n => sent = n), Arg.Any<CancellationToken>());

        var r = await SubmittedAsync();

        Assert.Equal("submitted", r.Status);
        Assert.NotNull(sent);
        Assert.Equal(ProductionRequestService.SubmittedEventType, sent!.EventType);
        Assert.Equal(_prod.Id, sent.StoreId);
        Assert.Equal(TenantId, sent.TenantId);
        Assert.Equal("system", sent.Channel);
        Assert.Equal("pending", sent.Status);
    }

    [Fact]
    public async Task Submit_AlreadySubmitted_Returns409()
    {
        var r = await SubmittedAsync();
        var (_, _, code) = await Svc().SubmitAsync(r.Id);
        Assert.Equal(409, code);
    }

    [Fact]
    public async Task Update_AfterSubmit_Returns409()
    {
        var r = await SubmittedAsync();
        var (_, _, code) = await Svc().UpdateAsync(r.Id, new ProductionRequestUpdateDto(null, "x", null));
        Assert.Equal(409, code);
    }

    [Fact]
    public async Task Cancel_Accepted_Returns409()
    {
        var r = await SubmittedAsync();
        await Svc().ReviewAsync(r.Id, new ProductionRequestReviewDto([], null), UserId);

        var (_, _, code) = await Svc().CancelAsync(r.Id);

        Assert.Equal(409, code);
    }

    // ── Review ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Review_NoOverrides_AcceptsEverything()
    {
        var r = await SubmittedAsync(10m);
        var (res, err, _) = await Svc().ReviewAsync(r.Id, new ProductionRequestReviewDto([], null), UserId);

        Assert.Null(err);
        Assert.Equal("accepted", res!.Status);
        Assert.Equal(10m, res.Lines[0].AcceptedQty);
    }

    [Fact]
    public async Task Review_ReducedQtyWithReason_IsPartial()
    {
        var r = await SubmittedAsync(10m);
        var (res, err, _) = await Svc().ReviewAsync(r.Id, new ProductionRequestReviewDto(
            [new(r.Lines[0].Id, 6m, "Не вистачає борошна")], "частково"), UserId);

        Assert.Null(err);
        Assert.Equal("partial", res!.Status);
        Assert.Equal(6m, res.Lines[0].AcceptedQty);
        Assert.Equal("Не вистачає борошна", res.Lines[0].Reason);
    }

    [Fact]
    public async Task Review_ReducedQtyWithoutReason_Returns400()
    {
        var r = await SubmittedAsync(10m);
        var (_, err, code) = await Svc().ReviewAsync(r.Id, new ProductionRequestReviewDto(
            [new(r.Lines[0].Id, 6m, null)], null), UserId);

        Assert.Equal(400, code);
        Assert.NotNull(err);
    }

    [Fact]
    public async Task Review_AcceptedAboveRequested_Returns400()
    {
        var r = await SubmittedAsync(10m);
        var (_, _, code) = await Svc().ReviewAsync(r.Id, new ProductionRequestReviewDto(
            [new(r.Lines[0].Id, 11m, null)], null), UserId);
        Assert.Equal(400, code);
    }

    [Fact]
    public async Task Review_AllZero_IsRejected()
    {
        var r = await SubmittedAsync(10m);
        var (res, _, _) = await Svc().ReviewAsync(r.Id, new ProductionRequestReviewDto(
            [new(r.Lines[0].Id, 0m, "Немає потужностей")], null), UserId);
        Assert.Equal("rejected", res!.Status);
    }

    [Fact]
    public async Task Reject_RequiresReason_AndZeroesLines()
    {
        var r = await SubmittedAsync();
        var (_, _, bad) = await Svc().RejectAsync(r.Id, "  ", UserId);
        Assert.Equal(400, bad);

        var (res, _, _) = await Svc().RejectAsync(r.Id, "Не виготовляємо", UserId);
        Assert.Equal("rejected", res!.Status);
        Assert.Equal(0m, res.Lines[0].AcceptedQty);
        Assert.Equal("Не виготовляємо", res.ReviewNote);
    }

    [Fact]
    public async Task Review_Draft_Returns409()
    {
        var (r, _, _) = await Svc().CreateAsync(CreateDto(), TenantId, UserId);
        var (_, _, code) = await Svc().ReviewAsync(r!.Id, new ProductionRequestReviewDto([], null), UserId);
        Assert.Equal(409, code);
    }

    // ── Orders from requests ─────────────────────────────────────────────────

    private async Task<ProductionRequestDetailDto> AcceptedAsync(Location store, decimal qty)
    {
        var (created, _, _) = await Svc().CreateAsync(
            new ProductionRequestCreateDto(store.Id, _prod.Id, Tomorrow, null,
                [new(_bread.Id, qty)], true), TenantId, UserId);
        var (res, err, _) = await Svc().ReviewAsync(created!.Id, new ProductionRequestReviewDto([], null), UserId);
        Assert.Null(err);
        return res!;
    }

    [Fact]
    public async Task CreateOrders_AggregatesByItemAcrossRequests_AndLinksLines()
    {
        var store2 = new Location { TenantId = TenantId, Name = "Магазин 2", Type = "shop" };
        _repo.Locations.Add(store2);
        var a = await AcceptedAsync(_store, 10m);
        var b = await AcceptedAsync(store2, 5m);
        _repo.Recipes.Add(new Recipe { TenantId = TenantId, Name = "Хліб", OutputItemId = _bread.Id, OutputQty = 1m, IsActive = true });
        ProductionOrder? created = null;
        await _production.AddOrderAsync(Arg.Do<ProductionOrder>(o => created = o), Arg.Any<CancellationToken>());

        var (orders, err, _) = await Svc().CreateOrdersFromRequestsAsync(
            new ProductionOrdersFromRequestsDto([a.Id, b.Id], null, null), TenantId, UserId);

        Assert.Null(err);
        var o1 = Assert.Single(orders!);
        Assert.Equal(15m, o1.PlannedQty);
        Assert.Equal(2, o1.RequestLineCount);
        Assert.Equal(_prod.Id, created!.LocationId);
        Assert.Equal(Tomorrow, created.NeededBy);
        Assert.Equal(2, _repo.Links.Count);
        Assert.Equal(15m, _repo.Links.Sum(l => l.Qty));
    }

    [Fact]
    public async Task CreateOrders_UsesAcceptedQtyNotRequested()
    {
        var r = await SubmittedAsync(10m);
        await Svc().ReviewAsync(r.Id, new ProductionRequestReviewDto([new(r.Lines[0].Id, 4m, "ліміт")], null), UserId);
        _repo.Recipes.Add(new Recipe { TenantId = TenantId, Name = "Хліб", OutputItemId = _bread.Id, OutputQty = 1m, IsActive = true });

        var (orders, _, _) = await Svc().CreateOrdersFromRequestsAsync(
            new ProductionOrdersFromRequestsDto([r.Id], null, null), TenantId, UserId);

        Assert.Equal(4m, orders!.Single().PlannedQty);
    }

    [Fact]
    public async Task CreateOrders_ItemWithoutRecipe_Returns422_AndCreatesNothing()
    {
        var a = await AcceptedAsync(_store, 10m);

        var (orders, err, code) = await Svc().CreateOrdersFromRequestsAsync(
            new ProductionOrdersFromRequestsDto([a.Id], null, null), TenantId, UserId);

        Assert.Null(orders);
        Assert.Equal(422, code);
        Assert.Contains("Хліб", err);
        await _production.DidNotReceive().AddOrderAsync(Arg.Any<ProductionOrder>(), Arg.Any<CancellationToken>());
        Assert.Empty(_repo.Links);
    }

    [Fact]
    public async Task CreateOrders_UnreviewedRequest_Returns409()
    {
        var r = await SubmittedAsync();
        var (_, _, code) = await Svc().CreateOrdersFromRequestsAsync(
            new ProductionOrdersFromRequestsDto([r.Id], null, null), TenantId, UserId);
        Assert.Equal(409, code);
    }

    [Fact]
    public async Task CreateOrders_LinesAlreadyInActiveOrder_Returns409()
    {
        var a = await AcceptedAsync(_store, 10m);
        _repo.Recipes.Add(new Recipe { TenantId = TenantId, Name = "Хліб", OutputItemId = _bread.Id, OutputQty = 1m, IsActive = true });
        _repo.LinesInActiveOrders.Add(a.Lines[0].Id);

        var (_, _, code) = await Svc().CreateOrdersFromRequestsAsync(
            new ProductionOrdersFromRequestsDto([a.Id], null, null), TenantId, UserId);

        Assert.Equal(409, code);
    }

    [Fact]
    public async Task Summary_AggregatesByItem_ExcludesLinesAlreadyInOrders()
    {
        var store2 = new Location { TenantId = TenantId, Name = "Магазин 2", Type = "shop" };
        _repo.Locations.Add(store2);
        var a = await AcceptedAsync(_store, 10m);
        await AcceptedAsync(store2, 5m);
        _repo.LinesInActiveOrders.Add(a.Lines[0].Id);
        _repo.Recipes.Add(new Recipe { TenantId = TenantId, Name = "Хліб", OutputItemId = _bread.Id, OutputQty = 1m, IsActive = true });

        var summary = await Svc().GetSummaryAsync(_prod.Id, null);

        var row = Assert.Single(summary);
        Assert.Equal(2, row.RequestCount);
        Assert.Equal(15m, row.RequestedQty);
        Assert.Equal(5m, row.QtyToProduce);
        Assert.True(row.HasRecipe);
    }

    // ── Ship ─────────────────────────────────────────────────────────────────

    private ProductStock Batch(decimal qty, int days) => new()
    {
        TenantId = TenantId, ProductId = _bread.Id, StoreId = _prod.Id,
        Quantity = qty, QuantityInitial = qty,
        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(days)),
    };

    [Fact]
    public async Task Ship_UsesFefoBatches_SkipsExpired_AndLinksTransfer()
    {
        var r = await AcceptedAsync(_store, 10m);
        var expired = Batch(100m, -1);
        var near = Batch(6m, 1);
        var far = Batch(20m, 5);
        _production.GetFefoOrderedAsync(_bread.Id, _prod.Id, Arg.Any<CancellationToken>())
            .Returns([expired, near, far]);

        CreateTransferRequest? sent = null;
        var transferDto = new TransferDto(Guid.NewGuid(), _prod.Id, "", _store.Id, "", "production_to_store",
            "in_transit", null, DateTime.UtcNow, []);
        _transfers.CreateAsync(TenantId, UserId, Arg.Do<CreateTransferRequest>(x => sent = x), Arg.Any<CancellationToken>())
            .Returns((transferDto, (string?)null));

        var (res, err, _) = await Svc().ShipAsync(r.Id, TenantId, UserId);

        Assert.Null(err);
        Assert.Equal("fulfilled", res!.Status);
        Assert.Equal(transferDto.Id, res.ShipmentTransferId);
        Assert.Equal("production_to_store", sent!.TransferType);
        Assert.Equal(_prod.Id, sent.FromStoreId);
        Assert.Equal(_store.Id, sent.ToStoreId);
        // 6 from the nearest-expiry batch, 4 from the next; the expired batch is never used.
        Assert.Equal(2, sent.Items.Count);
        Assert.Equal(near.Id, sent.Items[0].ProductStockId);
        Assert.Equal(6m, sent.Items[0].Quantity);
        Assert.Equal(far.Id, sent.Items[1].ProductStockId);
        Assert.Equal(4m, sent.Items[1].Quantity);
    }

    [Fact]
    public async Task Ship_InsufficientStock_Returns422_AndStaysAccepted()
    {
        var r = await AcceptedAsync(_store, 10m);
        _production.GetFefoOrderedAsync(_bread.Id, _prod.Id, Arg.Any<CancellationToken>())
            .Returns([Batch(3m, 5)]);

        var (_, err, code) = await Svc().ShipAsync(r.Id, TenantId, UserId);

        Assert.Equal(422, code);
        Assert.NotNull(err);
        Assert.Equal("accepted", _repo.Requests.Single().Status);
        await _transfers.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateTransferRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ship_TransferFails_RestoresStatus()
    {
        var r = await AcceptedAsync(_store, 10m);
        _production.GetFefoOrderedAsync(_bread.Id, _prod.Id, Arg.Any<CancellationToken>())
            .Returns([Batch(50m, 5)]);
        _transfers.CreateAsync(TenantId, UserId, Arg.Any<CreateTransferRequest>(), Arg.Any<CancellationToken>())
            .Returns(((TransferDto?)null, "boom"));

        var (_, err, code) = await Svc().ShipAsync(r.Id, TenantId, UserId);

        Assert.Equal(422, code);
        Assert.Equal("boom", err);
        Assert.Equal("accepted", _repo.Requests.Single().Status);
        Assert.Null(_repo.Requests.Single().ShipmentTransferId);
    }

    [Fact]
    public async Task Ship_NotAccepted_Returns409()
    {
        var r = await SubmittedAsync();
        var (_, _, code) = await Svc().ShipAsync(r.Id, TenantId, UserId);
        Assert.Equal(409, code);
    }
}
