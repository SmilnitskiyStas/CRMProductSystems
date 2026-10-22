using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Production;

internal sealed class FakeInsightsRepo : IProductionInsightsRepository
{
    public List<Location> Locations = [];
    public List<ProductionRequest> Requests = [];
    public List<ProductionOrderRequestLine> Links = [];
    public List<ProductionOrder> Orders = [];
    public List<Recipe> Recipes = [];
    public List<ProductStock> Stock = [];
    public List<Item> Items = [];

    public Task<List<Location>> GetProductionLocationsAsync(Guid? locationId, CancellationToken ct = default) =>
        Task.FromResult(Locations.Where(l => l.Type == "production" && l.IsActive && (locationId == null || l.Id == locationId)).ToList());

    public Task<List<ProductionRequest>> GetRequestsAsync(
        IReadOnlyCollection<Guid> ids, IReadOnlyCollection<string> statuses,
        DateTime? shippedFrom = null, DateTime? shippedToExclusive = null, CancellationToken ct = default) =>
        Task.FromResult(Requests.Where(r => ids.Contains(r.ProductionLocationId) && statuses.Contains(r.Status)
            && (shippedFrom == null || r.ShippedAt >= shippedFrom)
            && (shippedToExclusive == null || r.ShippedAt < shippedToExclusive)).ToList());

    public Task<List<ProductionRequest>> GetPipelineRequestsAsync(
        IReadOnlyCollection<Guid> ids, DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct = default)
    {
        bool In(DateTime? t) => t >= fromUtc && t < toUtcExclusive;
        return Task.FromResult(Requests.Where(r => ids.Contains(r.ProductionLocationId)
            && (r.Status is ProductionRequestStatus.Submitted or ProductionRequestStatus.Accepted or ProductionRequestStatus.Partial
                || In(r.SubmittedAt) || In(r.ReviewedAt) || In(r.ShippedAt))).ToList());
    }

    public Task<List<ProductionOrderRequestLine>> GetLinksForRequestsAsync(
        IReadOnlyCollection<Guid> requestIds, CancellationToken ct = default) =>
        Task.FromResult(Links.Where(l => Requests.Any(r => requestIds.Contains(r.Id) && r.Lines.Any(x => x.Id == l.RequestLineId))).ToList());

    public Task<List<ProductionOrder>> GetOrdersAsync(
        IReadOnlyCollection<Guid> locationIds, IReadOnlyCollection<ProductionOrderStatus> statuses, CancellationToken ct = default) =>
        Task.FromResult(Orders.Where(o => locationIds.Contains(o.LocationId) && statuses.Contains(o.Status)).ToList());

    public Task<List<Recipe>> GetActiveRecipesAsync(CancellationToken ct = default) =>
        Task.FromResult(Recipes.Where(r => r.IsActive).ToList());

    public Task<List<ProductStock>> GetStockAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct = default) =>
        Task.FromResult(Stock.Where(s => locationIds.Contains(s.StoreId) && s.Quantity > 0).ToList());

    public Task<List<Item>> GetItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default) =>
        Task.FromResult(Items.Where(i => itemIds.Contains(i.Id)).ToList());
}

/// <summary>TASK-763: production overview / plan / shipments / warehouse read models.</summary>
public sealed class ProductionInsightsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    // Wednesday 2026-10-21 12:00 UTC.
    private static readonly DateOnly Today = new(2026, 10, 21);
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    }

    private readonly FakeInsightsRepo _repo = new();
    private readonly Location _prod = new() { TenantId = Tenant, Name = "Цех 1", Type = "production" };
    private readonly Location _prod2 = new() { TenantId = Tenant, Name = "Цех 2", Type = "production" };
    private readonly Location _store = new() { TenantId = Tenant, Name = "Магазин", Type = "shop" };
    private readonly Item _bread = new() { TenantId = Tenant, Name = "Хліб", Unit = "шт" };
    private readonly Item _flour = new() { TenantId = Tenant, Name = "Борошно", Unit = "кг" };
    private readonly Item _salt = new() { TenantId = Tenant, Name = "Сіль", Unit = "кг" };
    private readonly Recipe _recipe;

    public ProductionInsightsTests()
    {
        _repo.Locations.AddRange([_prod, _prod2, _store]);
        _recipe = new Recipe { TenantId = Tenant, Name = "Хліб", OutputItemId = _bread.Id, OutputQty = 10m, OutputItem = _bread };
        _recipe.Ingredients.Add(new RecipeIngredient { RecipeId = _recipe.Id, ItemId = _flour.Id, Qty = 5m, Unit = "кг", Item = _flour });
        _recipe.Ingredients.Add(new RecipeIngredient { RecipeId = _recipe.Id, ItemId = _salt.Id, Qty = 1m, Unit = "кг", Item = _salt });
        _repo.Recipes.Add(_recipe);
    }

    private ProductionInsightsService Svc() => new(_repo, new FixedClock());
    private static DateOnly D(int offset) => Today.AddDays(offset);

    private ProductionRequest Req(string status, decimal requested, decimal? accepted, int neededIn,
        string source = ProductionRequestSource.Manual, Location? prod = null, Item? item = null)
    {
        var r = new ProductionRequest
        {
            TenantId = Tenant, StoreLocationId = _store.Id, StoreLocation = _store,
            ProductionLocationId = (prod ?? _prod).Id, NeededBy = D(neededIn), Status = status, Source = source,
            SubmittedAt = DateTime.UtcNow.AddMinutes(-neededIn),
        };
        var it = item ?? _bread;
        r.Lines.Add(new ProductionRequestLine
        {
            TenantId = Tenant, RequestId = r.Id, ItemId = it.Id, Item = it, RequestedQty = requested, AcceptedQty = accepted,
        });
        _repo.Requests.Add(r);
        return r;
    }

    private ProductionOrder Order(ProductionOrderStatus status, decimal qty, Location? loc = null)
    {
        var o = new ProductionOrder
        {
            TenantId = Tenant, RecipeId = _recipe.Id, Recipe = _recipe, LocationId = (loc ?? _prod).Id,
            PlannedQty = qty, Status = status,
            StartedAt = status == ProductionOrderStatus.InProgress ? DateTimeOffset.UtcNow : null,
        };
        _repo.Orders.Add(o);
        return o;
    }

    private void Link(ProductionRequest r, ProductionOrder o) =>
        _repo.Links.Add(new ProductionOrderRequestLine
        {
            TenantId = Tenant, ProductionOrderId = o.Id, ProductionOrder = o,
            RequestLineId = r.Lines.First().Id, Qty = r.Lines.First().AcceptedQty ?? 0,
        });

    private ProductStock Batch(Item item, decimal qty, int expiresIn, Location? loc = null, string? src = null)
    {
        var s = new ProductStock
        {
            TenantId = Tenant, ProductId = item.Id, Product = item, StoreId = (loc ?? _prod).Id, Store = loc ?? _prod,
            Quantity = qty, ExpiryDate = D(expiresIn), SourceType = src, BatchNumber = "B" + expiresIn,
        };
        _repo.Stock.Add(s);
        return s;
    }

    // ── Scope ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoProductionLocation_ReturnsEmpty()
    {
        _repo.Locations.RemoveAll(l => l.Type == "production");
        var o = await Svc().GetOverviewAsync(null);
        Assert.Equal(0, o.IncomingRequests);
        Assert.Empty(await Svc().GetStockAsync(null, null));
    }

    [Fact]
    public async Task DefaultScope_IsAllProductionLocations_AndLocationFilterNarrows()
    {
        Req(ProductionRequestStatus.Submitted, 5m, null, 1, prod: _prod);
        Req(ProductionRequestStatus.Submitted, 7m, null, 1, prod: _prod2);

        Assert.Equal(2, (await Svc().GetOverviewAsync(null)).IncomingRequests);
        Assert.Equal(1, (await Svc().GetOverviewAsync(_prod2.Id)).IncomingRequests);
        // A non-production location id yields nothing.
        Assert.Equal(0, (await Svc().GetOverviewAsync(_store.Id)).IncomingRequests);
    }

    // ── Overview ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Overview_CountsStagesOrdersExpiringAndShortages()
    {
        Req(ProductionRequestStatus.Submitted, 5m, null, 1);                    // incoming
        Req(ProductionRequestStatus.Accepted, 5m, 5m, 2);                       // no links => awaiting_order
        var ready = Req(ProductionRequestStatus.Accepted, 4m, 4m, 3);
        Link(ready, Order(ProductionOrderStatus.Done, 4m));
        var inProg = Order(ProductionOrderStatus.InProgress, 20m);
        Order(ProductionOrderStatus.Planned, 10m);

        Batch(_flour, 3m, 3);          // expiring soon, raw
        Batch(_bread, 2m, 30);         // fine
        Batch(_salt, 1m, -1);          // expired => in expiring list, not on hand

        var o = await Svc().GetOverviewAsync(null);
        Assert.Equal(1, o.IncomingRequests);
        Assert.Equal(1, o.AwaitingOrder);
        Assert.Equal(1, o.ReadyToShip);
        Assert.Equal(1, o.OrdersPlanned);
        Assert.Equal(1, o.OrdersInProgress);
        Assert.Equal(inProg.Id, Assert.Single(o.InProgressOrders).Id);
        Assert.Equal(ready.Id, Assert.Single(o.ReadyToShipList).RequestId);
        Assert.Equal(4m, o.ReadyToShipList[0].Qty);
        Assert.Equal(2, o.ExpiringSoon);
        Assert.Equal("raw", o.Expiring.First(e => e.ItemName == "Борошно").Kind);
        Assert.Equal("Сіль", o.Expiring[0].ItemName); // soonest first

        // Open orders need 30 bread => flour 15 (have 3 => miss 12), salt 3 (expired stock does not count => miss 3)
        Assert.Equal(2, o.Shortages);
        Assert.Equal("Борошно", o.ShortageList[0].ItemName);
        Assert.Equal(15m, o.ShortageList[0].Required);
        Assert.Equal(3m, o.ShortageList[0].Available);
        Assert.Equal(12m, o.ShortageList[0].Missing);
    }

    [Fact]
    public async Task Overview_NewRequests_OldestFirst_Top5()
    {
        for (var i = 1; i <= 7; i++) Req(ProductionRequestStatus.Submitted, i, null, i);
        var o = await Svc().GetOverviewAsync(null);
        Assert.Equal(7, o.IncomingRequests);
        Assert.Equal(5, o.NewRequests.Count);
        Assert.Equal(7m, o.NewRequests[0].TotalQty); // submitted longest ago (neededIn=7 => -7 min)
    }

    // ── Plan ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Plan_Defaults_14DayBuckets_AndCapsTo92Days()
    {
        var p = await Svc().GetPlanAsync(null, null, null, null);
        Assert.Equal("day", p.Granularity);
        Assert.Equal(D(0), p.From);
        Assert.Equal(D(13), p.To);
        Assert.Equal(14, p.Buckets.Count);

        var capped = await Svc().GetPlanAsync(D(0), D(400), "day", null);
        Assert.Equal(92, capped.Buckets.Count);
    }

    [Fact]
    public async Task Plan_WeekBuckets_AreIsoMondayStart_ClippedToRange()
    {
        // 2026-10-21 is a Wednesday => first bucket key Monday 2026-10-19, clipped from 10-21 to 10-25.
        var p = await Svc().GetPlanAsync(D(0), D(13), "week", null);
        Assert.Equal(new[] { "2026-10-19", "2026-10-26", "2026-11-02" }, p.Buckets.Select(b => b.Key).ToArray());
        Assert.Equal(D(0), p.Buckets[0].From);
        Assert.Equal(new DateOnly(2026, 10, 25), p.Buckets[0].To);
        Assert.Equal(D(13), p.Buckets[2].To);
    }

    [Fact]
    public async Task Plan_MonthBuckets_AreCalendarMonths()
    {
        var p = await Svc().GetPlanAsync(new DateOnly(2026, 10, 20), new DateOnly(2026, 12, 5), "month", null);
        Assert.Equal(new[] { "2026-10", "2026-11", "2026-12" }, p.Buckets.Select(b => b.Key).ToArray());
        Assert.Equal(new DateOnly(2026, 10, 31), p.Buckets[0].To);
    }

    [Fact]
    public async Task Plan_DemandPerBucket_RequestedPendingForecast_AndToProduceFormula()
    {
        Req(ProductionRequestStatus.Accepted, 10m, 8m, 1);                                   // requested 8 (day +1)
        Req(ProductionRequestStatus.Partial, 10m, 4m, 1);                                    // requested +4
        Req(ProductionRequestStatus.Submitted, 6m, null, 2);                                 // pending 6
        Req(ProductionRequestStatus.Draft, 5m, null, 3, ProductionRequestSource.AutoBuffer); // forecast 5
        Req(ProductionRequestStatus.Draft, 99m, null, 3);                                    // manual draft ignored
        Req(ProductionRequestStatus.Accepted, 50m, 50m, 40);                                 // outside range
        Batch(_bread, 2m, 10);                                                               // on hand 2
        Batch(_bread, 100m, -3);                                                             // expired ignored
        Order(ProductionOrderStatus.Planned, 3m);                                            // open 3

        var p = await Svc().GetPlanAsync(D(0), D(6), "day", null);
        var prod = Assert.Single(p.Products);
        Assert.True(prod.HasRecipe);
        Assert.Equal(12m, prod.TotalRequested);
        Assert.Equal(6m, prod.TotalPending);
        Assert.Equal(5m, prod.TotalForecast);
        Assert.Equal(2m, prod.FinishedOnHand);
        Assert.Equal(3m, prod.OpenOrderQty);
        Assert.Equal(12m, prod.ToProduce); // 12 + 5 - 2 - 3
        var day1 = prod.PerBucket.Single(b => b.BucketKey == D(1).ToString("yyyy-MM-dd"));
        Assert.Equal(12m, day1.RequestedQty);
    }

    [Fact]
    public async Task Plan_ToProduce_FlooredAtZero_AndOverdueFoldsIntoFirstBucket()
    {
        Req(ProductionRequestStatus.Accepted, 5m, 5m, -2); // overdue => first bucket
        Order(ProductionOrderStatus.InProgress, 20m);
        var p = await Svc().GetPlanAsync(D(0), D(2), "day", null);
        var prod = Assert.Single(p.Products);
        Assert.Equal(5m, prod.PerBucket[0].RequestedQty);
        Assert.Equal(0m, prod.ToProduce);
    }

    [Fact]
    public async Task Plan_Mrp_AggregatesOrdersAndToProduce_SortedByMissing_WithSupplier()
    {
        var supplier = new Supplier { Name = "Млин" };
        _repo.Items.Add(new Item
        {
            Id = _flour.Id, TenantId = Tenant, Name = "Борошно", Unit = "кг",
            DefaultSupplierId = supplier.Id, DefaultSupplier = supplier,
        });

        Order(ProductionOrderStatus.Planned, 10m);          // flour 5, salt 1
        Req(ProductionRequestStatus.Accepted, 20m, 20m, 1); // toProduce = 20 - 0 - 10 = 10 => flour 5, salt 1
        Batch(_flour, 4m, 20);
        Batch(_salt, 100m, 20);

        var p = await Svc().GetPlanAsync(D(0), D(6), "day", null);
        Assert.Equal(2, p.Ingredients.Count);
        var flour = p.Ingredients[0]; // biggest missing first
        Assert.Equal("Борошно", flour.ItemName);
        Assert.Equal(10m, flour.Required);
        Assert.Equal(4m, flour.OnHand);
        Assert.Equal(6m, flour.Missing);
        Assert.Equal(supplier.Id, flour.DefaultSupplierId);
        Assert.Equal("Млин", flour.DefaultSupplierName);
        var salt = p.Ingredients[1];
        Assert.Equal(2m, salt.Required);
        Assert.Equal(0m, salt.Missing);
    }

    [Fact]
    public async Task Plan_ProductWithoutRecipe_HasRecipeFalse_NoIngredients()
    {
        var cake = new Item { TenantId = Tenant, Name = "Торт", Unit = "шт" };
        Req(ProductionRequestStatus.Accepted, 3m, 3m, 1, item: cake);
        var p = await Svc().GetPlanAsync(D(0), D(3), "day", null);
        var prod = Assert.Single(p.Products);
        Assert.False(prod.HasRecipe);
        Assert.Equal(3m, prod.ToProduce);
        Assert.Empty(p.Ingredients);
    }

    [Fact]
    public async Task Plan_LocationFilter_ExcludesOtherLocationOrdersAndStock()
    {
        Order(ProductionOrderStatus.Planned, 10m, _prod2);
        Batch(_bread, 5m, 10, _prod2);
        Req(ProductionRequestStatus.Accepted, 10m, 10m, 1, prod: _prod);
        var p = await Svc().GetPlanAsync(D(0), D(3), "day", _prod.Id);
        var prod = Assert.Single(p.Products);
        Assert.Equal(0m, prod.OpenOrderQty);
        Assert.Equal(0m, prod.FinishedOnHand);
        Assert.Equal(10m, prod.ToProduce);
    }

    // ── Shipments ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Shipments_Ready_OnlyRequestsWhoseOrdersAreAllDone()
    {
        var readyReq = Req(ProductionRequestStatus.Accepted, 4m, 4m, 1);
        var o1 = Order(ProductionOrderStatus.Done, 4m);
        Link(readyReq, o1);
        var busyReq = Req(ProductionRequestStatus.Accepted, 4m, 4m, 1);
        Link(busyReq, Order(ProductionOrderStatus.InProgress, 4m));
        Req(ProductionRequestStatus.Accepted, 4m, 4m, 1); // unlinked => awaiting_order

        var list = await Svc().GetReadyShipmentsAsync(null);
        var s = Assert.Single(list);
        Assert.Equal(readyReq.Id, s.RequestId);
        Assert.True(s.CanShip);
        Assert.Equal("ready", s.ProductionStage);
        Assert.Equal(o1.Id, Assert.Single(s.OrderIds));
        Assert.Equal(4m, Assert.Single(s.Lines).Qty);
        Assert.Equal("Магазин", s.StoreName);
    }

    [Fact]
    public async Task Shipments_Shipped_FiltersByShippedAt_DefaultLast30Days()
    {
        var recent = Req(ProductionRequestStatus.Fulfilled, 4m, 4m, -3);
        recent.ShippedAt = DateTime.SpecifyKind(Today.AddDays(-2).ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);
        var transfer = new StockTransfer { TenantId = Tenant, Status = "confirmed" };
        recent.ShipmentTransferId = transfer.Id;
        recent.ShipmentTransfer = transfer;
        var old = Req(ProductionRequestStatus.Fulfilled, 4m, 4m, -60);
        old.ShippedAt = DateTime.SpecifyKind(Today.AddDays(-60).ToDateTime(new TimeOnly(10, 0)), DateTimeKind.Utc);

        var list = await Svc().GetShippedShipmentsAsync(null, null, null);
        var s = Assert.Single(list);
        Assert.Equal(recent.Id, s.RequestId);
        Assert.Equal("confirmed", s.TransferStatus);
        Assert.Equal(transfer.Id, s.TransferId);

        var wide = await Svc().GetShippedShipmentsAsync(D(-90), D(0), null);
        Assert.Equal(2, wide.Count);
    }

    // ── Warehouse ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stock_KindClassification_FiltersAndSortsByNearestExpiry()
    {
        var misc = new Item { TenantId = Tenant, Name = "Серветки", Unit = "шт" };
        Batch(_bread, 5m, 6, src: "production");
        Batch(_bread, 2m, 2, src: "production");
        Batch(_flour, 10m, 1);
        Batch(misc, 1m, 40);
        Batch(_salt, 0m, 5); // qty 0 excluded

        var all = await Svc().GetStockAsync(null, "all");
        Assert.Equal(new[] { "Борошно", "Хліб", "Серветки" }, all.Select(i => i.ItemName).ToArray());
        var bread = all.First(i => i.ItemName == "Хліб");
        Assert.Equal("finished", bread.Kind);
        Assert.Equal(7m, bread.TotalQty);
        Assert.Equal(D(2), bread.NearestExpiry);
        Assert.Equal(2, bread.Batches[0].DaysToExpiry);
        Assert.Equal("production", bread.Batches[0].SourceType);
        Assert.Equal("raw", all.First(i => i.ItemName == "Борошно").Kind);
        Assert.Equal("other", all.First(i => i.ItemName == "Серветки").Kind);

        Assert.Equal(new[] { "Борошно" }, (await Svc().GetStockAsync(null, "raw")).Select(i => i.ItemName).ToArray());
        Assert.Equal(new[] { "Хліб" }, (await Svc().GetStockAsync(null, "finished")).Select(i => i.ItemName).ToArray());
    }

    [Fact]
    public async Task Stock_ItemBothIngredientAndOutput_IsFinished()
    {
        var dough = new Item { TenantId = Tenant, Name = "Тісто", Unit = "кг" };
        _repo.Recipes.Add(new Recipe { TenantId = Tenant, Name = "Тісто", OutputItemId = dough.Id, OutputQty = 1m });
        _recipe.Ingredients.Add(new RecipeIngredient { RecipeId = _recipe.Id, ItemId = dough.Id, Qty = 1m, Item = dough });
        Batch(dough, 3m, 5);
        Assert.Equal("finished", Assert.Single(await Svc().GetStockAsync(null, null)).Kind);
    }

    // ── Pipeline (TASK-765) ──────────────────────────────────────────────────

    private static DateTime Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private ProductionRequest Shipped(DateTime at, decimal qty, Location? prod = null, Item? item = null)
    {
        var r = Req(ProductionRequestStatus.Fulfilled, qty, qty, -1, prod: prod, item: item);
        r.ShippedAt = at;
        return r;
    }

    [Fact]
    public async Task Pipeline_EachRequestLandsInExactlyOneStage()
    {
        var sub = Req(ProductionRequestStatus.Submitted, 5m, null, 1);
        var awaiting = Req(ProductionRequestStatus.Accepted, 5m, 5m, 1);
        var planned = Req(ProductionRequestStatus.Accepted, 4m, 4m, 1);
        Link(planned, Order(ProductionOrderStatus.Planned, 4m));
        var inProd = Req(ProductionRequestStatus.Partial, 6m, 3m, 1);
        Link(inProd, Order(ProductionOrderStatus.InProgress, 3m));
        var ready = Req(ProductionRequestStatus.Accepted, 2m, 2m, 1);
        var doneOrder = Order(ProductionOrderStatus.Done, 2m);
        Link(ready, doneOrder);
        var shipped = Shipped(Utc(2026, 10, 21, 8), 7m);
        Req(ProductionRequestStatus.Draft, 1m, null, 1);
        Req(ProductionRequestStatus.Rejected, 1m, null, 1);

        var p = await Svc().GetPipelineAsync(null, null, null);
        Assert.Equal(D(0), p.From);
        Assert.Equal(D(0), p.To);
        Assert.Equal(sub.Id, Assert.Single(p.NewRequests.Requests).Id);
        Assert.Equal(1, p.NewRequests.Count);
        Assert.Equal(5m, p.NewRequests.Requests[0].TotalQty);
        Assert.Equal(new[] { awaiting.Id, planned.Id, inProd.Id }.OrderBy(x => x).ToArray(),
            p.InWork.Requests.Select(r => r.Id).OrderBy(x => x).ToArray());
        Assert.Equal(3, p.InWork.Count);
        Assert.Equal(1, p.InWorkByStage.AwaitingOrder);
        Assert.Equal(1, p.InWorkByStage.Planned);
        Assert.Equal(1, p.InWorkByStage.InProduction);
        var ip = p.InWork.Requests.Single(r => r.Id == inProd.Id);
        Assert.Equal("in_production", ip.ProductionStage);
        Assert.Equal(3m, ip.TotalQty); // accepted, not requested
        var ord = Assert.Single(ip.Orders);
        Assert.Equal("in_progress", ord.Status);
        Assert.Equal(3m, ord.Qty);
        Assert.Equal(ready.Id, Assert.Single(p.Ready.Requests).Id);
        Assert.Equal(doneOrder.Id, Assert.Single(p.Ready.Requests[0].OrderIds));
        Assert.Equal(shipped.Id, Assert.Single(p.Shipped.Requests).Id);
        Assert.Equal(1, p.Shipped.Count);
        Assert.Equal(7m, p.Shipped.TotalQty);
    }

    [Fact]
    public async Task Pipeline_ShippedAndStats_FilterByPeriod_InLocationTimeZone()
    {
        // Kyiv is UTC+3 on 2026-10-21 (DST ends 10-25). Local day 10-21 = 10-20T21:00Z .. 10-21T21:00Z.
        Shipped(Utc(2026, 10, 20, 20, 59), 1m);         // local 10-20 23:59 -> out
        var a = Shipped(Utc(2026, 10, 20, 21, 0), 2m);  // local 10-21 00:00 -> in
        var b = Shipped(Utc(2026, 10, 21, 20, 59), 3m); // local 10-21 23:59 -> in
        Shipped(Utc(2026, 10, 21, 21, 0), 4m);          // local 10-22 00:00 -> out

        var p = await Svc().GetPipelineAsync(D(0), D(0), null);
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x).ToArray(), p.Shipped.Requests.Select(r => r.Id).OrderBy(x => x).ToArray());
        Assert.Equal(5m, p.Shipped.TotalQty);
        Assert.Equal(2, p.Stats.Shipped);
        var day = Assert.Single(p.Shipped.PerDay);
        Assert.Equal(D(0), day.Date);
        Assert.Equal(2, day.Requests);
        Assert.Equal(5m, day.Qty);
        Assert.Equal(b.Id, p.Shipped.Requests[0].Id); // newest first
    }

    [Fact]
    public async Task Pipeline_Stats_CountReceivedAcceptedRejected_ByTimestampInPeriod()
    {
        var inside = Utc(2026, 10, 21, 5);
        var outside = Utc(2026, 10, 18, 5);
        Req(ProductionRequestStatus.Submitted, 1m, null, 1).SubmittedAt = inside;
        Req(ProductionRequestStatus.Submitted, 1m, null, 1).SubmittedAt = outside;
        var acc = Req(ProductionRequestStatus.Accepted, 1m, 1m, 1); acc.SubmittedAt = outside; acc.ReviewedAt = inside;
        var par = Req(ProductionRequestStatus.Partial, 2m, 1m, 1); par.SubmittedAt = inside; par.ReviewedAt = inside;
        var rej = Req(ProductionRequestStatus.Rejected, 1m, null, 1); rej.SubmittedAt = inside; rej.ReviewedAt = inside;
        var rejOld = Req(ProductionRequestStatus.Rejected, 1m, null, 1); rejOld.SubmittedAt = outside; rejOld.ReviewedAt = outside;

        var p = await Svc().GetPipelineAsync(D(0), D(0), null);
        Assert.Equal(3, p.Stats.Received); // 1 submitted + par + rej
        Assert.Equal(2, p.Stats.Accepted); // acc + par
        Assert.Equal(1, p.Stats.Rejected);
        Assert.Equal(0, p.Stats.Shipped);
        Assert.Equal(2, p.NewRequests.Count); // current snapshot ignores the period
    }

    [Fact]
    public async Task Pipeline_PerDay_ZeroFilled_AndByItemByStoreAggregated()
    {
        Shipped(Utc(2026, 10, 19, 9), 2m);
        Shipped(Utc(2026, 10, 19, 12), 3m);
        Shipped(Utc(2026, 10, 21, 9), 4m, item: _flour);
        var p = await Svc().GetPipelineAsync(D(-3), D(0), null);
        Assert.Equal(new[] { D(-3), D(-2), D(-1), D(0) }, p.Shipped.PerDay.Select(d => d.Date).ToArray());
        Assert.Equal(new[] { 0, 2, 0, 1 }, p.Shipped.PerDay.Select(d => d.Requests).ToArray());
        Assert.Equal(new[] { 0m, 5m, 0m, 4m }, p.Shipped.PerDay.Select(d => d.Qty).ToArray());
        var bread = p.Shipped.ByItem.Single(i => i.ItemName == "Хліб");
        Assert.Equal(5m, bread.Qty);
        Assert.Equal("шт", bread.Unit);
        Assert.Equal(4m, p.Shipped.ByItem.Single(i => i.ItemName == "Борошно").Qty);
        var store = Assert.Single(p.Shipped.ByStore);
        Assert.Equal(3, store.Requests);
        Assert.Equal(9m, store.Qty);
    }

    [Fact]
    public async Task Pipeline_ReadyByItem_Aggregates()
    {
        var r1 = Req(ProductionRequestStatus.Accepted, 2m, 2m, 1);
        Link(r1, Order(ProductionOrderStatus.Done, 2m));
        var r2 = Req(ProductionRequestStatus.Accepted, 5m, 5m, 1);
        Link(r2, Order(ProductionOrderStatus.Done, 5m));
        var p = await Svc().GetPipelineAsync(null, null, null);
        Assert.Equal(2, p.Ready.Count);
        Assert.Equal(7m, Assert.Single(p.Ready.ByItem).Qty);
    }

    [Fact]
    public async Task Pipeline_Validation_ThrowsOnReversedOrTooLongRange()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Svc().GetPipelineAsync(D(1), D(0), null));
        await Assert.ThrowsAsync<ArgumentException>(() => Svc().GetPipelineAsync(D(-366), D(0), null)); // 367 days
        var ok = await Svc().GetPipelineAsync(D(-365), D(0), null); // 366 days
        Assert.Equal(366, ok.Shipped.PerDay.Count);
    }

    [Fact]
    public async Task Pipeline_LocationFilter_NarrowsAllStages()
    {
        Req(ProductionRequestStatus.Submitted, 1m, null, 1, prod: _prod);
        Req(ProductionRequestStatus.Submitted, 1m, null, 1, prod: _prod2);
        Shipped(Utc(2026, 10, 21, 9), 2m, prod: _prod2);

        Assert.Equal(2, (await Svc().GetPipelineAsync(null, null, null)).NewRequests.Count);
        var one = await Svc().GetPipelineAsync(null, null, _prod.Id);
        Assert.Equal(1, one.NewRequests.Count);
        Assert.Equal(0, one.Shipped.Count);
        Assert.Equal(1, (await Svc().GetPipelineAsync(null, null, _prod2.Id)).Shipped.Count);
        var none = await Svc().GetPipelineAsync(null, null, _store.Id);
        Assert.Equal(0, none.NewRequests.Count);
        Assert.Single(none.Shipped.PerDay);
    }
}
