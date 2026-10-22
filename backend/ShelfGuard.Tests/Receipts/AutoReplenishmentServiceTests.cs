using NSubstitute;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Orders.Dtos;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Receipts.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Receipts;

/// <summary>TASK-755: automatic supplier-order drafts - grouping, exclusions, idempotent refresh.</summary>
public sealed class AutoReplenishmentServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IReceiptRepository _receipts = Substitute.For<IReceiptRepository>();
    private readonly IProductionDemandRepository _stores = Substitute.For<IProductionDemandRepository>();
    private readonly IOrderCalcService _orderCalc = Substitute.For<IOrderCalcService>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IReceiptService _receiptService = Substitute.For<IReceiptService>();

    private readonly Location _store = new() { TenantId = TenantId, Name = "Магазин 1", Type = "shop" };
    private readonly Guid _supA = Guid.NewGuid();
    private readonly Guid _supB = Guid.NewGuid();
    private readonly Guid _milk = Guid.NewGuid();
    private readonly Guid _bread = Guid.NewGuid();   // supplier B
    private readonly Guid _salt = Guid.NewGuid();    // no supplier
    private readonly Guid _dumplings = Guid.NewGuid(); // self-produced

    private readonly List<StockReceipt> _db = [];     // "the table"
    private readonly List<NotificationQueue> _outbox = [];
    private List<OrderLineDto> _lines = [];
    private HashSet<Guid> _selfProduced = [];

    public AutoReplenishmentServiceTests()
    {
        _stores.GetActiveStoresAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ => [_store]);
        _stores.GetSelfProducedItemIdsAsync(Arg.Any<CancellationToken>()).Returns(_ => _selfProduced);

        _orderCalc.CalculateAsync(_store.Id, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(_ => (new OrderCalcResult(_store.Id, DateTime.UtcNow, _lines.Count, _lines.Count, _lines), (string?)null));

        _receipts.GetUntakenAutoDraftsAsync(_store.Id, Arg.Any<CancellationToken>()).Returns(_ => _db
            .Where(r => r.Status == "draft" && r.Source == ReceiptSource.AutoReplenishment && r.TakenByUserId == null)
            .OrderByDescending(r => r.CreatedAt).ToList());
        _receipts.AddAsync(Arg.Any<StockReceipt>(), Arg.Any<CancellationToken>())
            .Returns(ci => { _db.Add(ci.Arg<StockReceipt>()); return Task.CompletedTask; });
        _receipts.AddItemAsync(Arg.Any<StockReceiptItem>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var item = ci.Arg<StockReceiptItem>();
                _db.Single(r => r.Id == item.ReceiptId).Items.Add(item);
                return Task.CompletedTask;
            });
        _receipts.When(r => r.RemoveItems(Arg.Any<IEnumerable<StockReceiptItem>>())).Do(ci =>
        {
            var gone = ci.Arg<IEnumerable<StockReceiptItem>>().ToList();
            foreach (var r in _db) foreach (var g in gone) r.Items.Remove(g);
        });
        _notifications.When(n => n.EnqueueAsync(Arg.Any<NotificationQueue>(), Arg.Any<CancellationToken>()))
            .Do(ci => _outbox.Add(ci.Arg<NotificationQueue>()));
        _receiptService.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var r = _db.Single(x => x.Id == ci.Arg<Guid>());
            return new ReceiptDto(r.Id, r.SupplierId, null, r.DestinationStoreId, _store.Name, false, r.Status, null, null,
                r.Notes, r.CreatedAt, r.Items.Select(i => new ReceiptItemDto(i.Id, i.ProductId, "p", null, i.QuantityOrdered,
                    null, i.PricePurchase, null, null, null, false)).ToList(), Source: r.Source);
        });

        _lines =
        [
            Line(_milk, 10m, _supA, 12m),
            Line(_bread, 5m, _supB, 20m),
            Line(_salt, 3m, null, null),
            Line(_dumplings, 7m, _supA, null),
        ];
        _selfProduced = [_dumplings];
    }

    private static OrderLineDto Line(Guid id, decimal toOrder, Guid? supplier, decimal? price) => new(
        id, "x", null, 0, 0, 0, 0, 0m, 0m, 0m, 0m, 1m, 1m, 1m, toOrder, 1m, 1m, "none",
        SupplierId: supplier, PricePurchase: price);

    private AutoReplenishmentService Svc() =>
        new(_receipts, _stores, _orderCalc, _notifications, _receiptService);

    [Fact]
    public async Task GroupsPerSupplier_NoSupplierGroup_ExcludesSelfProduced()
    {
        var (drafts, error, _) = await Svc().GenerateAsync(TenantId, null);

        Assert.Null(error);
        Assert.Equal(3, drafts!.Count);          // supplier A, supplier B, "without supplier"
        Assert.Equal(3, _db.Count);
        Assert.All(_db, r =>
        {
            Assert.Equal("draft", r.Status);
            Assert.Equal(ReceiptSource.AutoReplenishment, r.Source);
            Assert.Equal(_store.Id, r.DestinationStoreId);
            Assert.False(r.ViaCentralStore);
            Assert.Null(r.CreatedBy);
            Assert.Null(r.TakenByUserId);
            Assert.Equal(AutoReplenishmentService.AutoNotes, r.Notes);
            Assert.Equal(TenantId, r.TenantId);
        });

        var a = _db.Single(r => r.SupplierId == _supA);
        var onlyMilk = Assert.Single(a.Items);               // dumplings (self-produced) excluded
        Assert.Equal(_milk, onlyMilk.ProductId);
        Assert.Equal(10m, onlyMilk.QuantityOrdered);
        Assert.Equal(12m, onlyMilk.PricePurchase);

        Assert.Equal(_bread, Assert.Single(_db.Single(r => r.SupplierId == _supB).Items).ProductId);
        Assert.Equal(_salt, Assert.Single(_db.Single(r => r.SupplierId == null).Items).ProductId);
        Assert.DoesNotContain(_db.SelectMany(r => r.Items), i => i.ProductId == _dumplings);
    }

    [Fact]
    public async Task Calculation_IgnoresOwnUntakenAutoDrafts()
    {
        await Svc().GenerateAsync(TenantId, null);

        await _orderCalc.Received(1).CalculateAsync(
            _store.Id, Arg.Any<CancellationToken>(), excludeAutoUntakenDrafts: true);
    }

    [Fact]
    public async Task LinesWithZeroNeed_AreSkipped()
    {
        _lines = [Line(_milk, 0m, _supA, 1m)];

        var (drafts, _, _) = await Svc().GenerateAsync(TenantId, null);

        Assert.Empty(drafts!);
        Assert.Empty(_db);
        Assert.Empty(_outbox);
    }

    [Fact]
    public async Task NewDraft_EnqueuesOrderAutoDraft_OncePerStore_NotReceiptCreated()
    {
        await Svc().GenerateAsync(TenantId, null);

        var n = Assert.Single(_outbox);                       // 3 drafts, ONE notification
        Assert.Equal("order.auto_draft", n.EventType);
        Assert.Equal(_store.Id, n.StoreId);
        Assert.Equal("system", n.Channel);
        Assert.Equal("pending", n.Status);
        Assert.DoesNotContain(_outbox, x => x.EventType == "receipt.created");
    }

    [Fact]
    public async Task SecondRun_Unchanged_IsIdempotent_NoDuplicatesNoNotification()
    {
        await Svc().GenerateAsync(TenantId, null);
        _outbox.Clear();

        var (drafts, error, _) = await Svc().GenerateAsync(TenantId, null);

        Assert.Null(error);
        Assert.Empty(drafts!);
        Assert.Equal(3, _db.Count);
        Assert.Empty(_outbox);
    }

    [Fact]
    public async Task SecondRun_ChangedNeed_RewritesUntakenDraftInPlace()
    {
        await Svc().GenerateAsync(TenantId, null);
        var milkDraft = _db.Single(r => r.SupplierId == _supA);
        var milkItemId = milkDraft.Items.Single().Id;
        _outbox.Clear();

        _lines =
        [
            Line(_milk, 16m, _supA, 12m),                      // qty changed
            Line(_dumplings, 7m, _supA, null),
            Line(_bread, 5m, _supB, 20m),
            Line(_salt, 3m, null, null),
            Line(Guid.Parse("11111111-1111-1111-1111-111111111111"), 4m, _supA, 2m), // new line on same draft
        ];

        var (drafts, _, _) = await Svc().GenerateAsync(TenantId, null);

        Assert.Equal(3, _db.Count);                            // same drafts, no new ones
        Assert.Same(milkDraft, _db.Single(r => r.SupplierId == _supA));
        Assert.Equal(2, milkDraft.Items.Count);
        Assert.Equal(16m, milkDraft.Items.Single(i => i.ProductId == _milk).QuantityOrdered);
        Assert.Equal(milkItemId, milkDraft.Items.Single(i => i.ProductId == _milk).Id);
        Assert.Single(drafts!);                                // only the changed draft is reported
        Assert.Empty(_outbox);                                 // refresh is not a NEW draft
    }

    [Fact]
    public async Task SecondRun_LineNoLongerNeeded_IsRemoved()
    {
        _lines = [Line(_milk, 10m, _supA, 12m), Line(Guid.NewGuid(), 4m, _supA, 2m)];
        await Svc().GenerateAsync(TenantId, null);
        var draft = _db.Single();
        Assert.Equal(2, draft.Items.Count);

        _lines = [Line(_milk, 10m, _supA, 12m)];
        await Svc().GenerateAsync(TenantId, null);

        Assert.Equal(_milk, Assert.Single(draft.Items).ProductId);
        Assert.Equal("draft", draft.Status);
    }

    [Fact]
    public async Task NeedDisappears_UntakenAutoDraftIsCancelled()
    {
        await Svc().GenerateAsync(TenantId, null);

        _lines = [Line(_bread, 5m, _supB, 20m), Line(_salt, 3m, null, null)]; // supplier A fully covered now
        var (drafts, _, _) = await Svc().GenerateAsync(TenantId, null);

        Assert.Equal("cancelled", _db.Single(r => r.SupplierId == _supA).Status);
        Assert.Equal("draft", _db.Single(r => r.SupplierId == _supB).Status);
        Assert.Contains(drafts!, d => d.Status == "cancelled");
    }

    [Fact]
    public async Task TakenDraft_IsNeverModified_AndManualReceiptIsNeverTouched()
    {
        await Svc().GenerateAsync(TenantId, null);
        var taken = _db.Single(r => r.SupplierId == _supA);
        taken.TakenByUserId = Guid.NewGuid();
        taken.TakenAt = DateTime.UtcNow;
        var snapshotQty = taken.Items.Single().QuantityOrdered;

        var manual = new StockReceipt { TenantId = TenantId, DestinationStoreId = _store.Id, SupplierId = _supA };
        manual.Items.Add(new StockReceiptItem { ReceiptId = manual.Id, ProductId = _milk, QuantityOrdered = 99m });
        _db.Add(manual);

        // need for supplier A changes AND later disappears: the taken draft must stay as the manager left it
        _lines = [Line(_milk, 40m, _supA, 12m), Line(_bread, 5m, _supB, 20m), Line(_salt, 3m, null, null)];
        await Svc().GenerateAsync(TenantId, null);
        _lines = [Line(_bread, 5m, _supB, 20m), Line(_salt, 3m, null, null)];
        await Svc().GenerateAsync(TenantId, null);

        Assert.Equal("draft", taken.Status);
        Assert.Equal(snapshotQty, taken.Items.Single().QuantityOrdered);
        Assert.Equal("draft", manual.Status);
        Assert.Equal(99m, manual.Items.Single().QuantityOrdered);
    }

    [Fact]
    public async Task TakenDraft_DoesNotBlockNewUntakenDraftForRemainingNeed()
    {
        await Svc().GenerateAsync(TenantId, null);
        var taken = _db.Single(r => r.SupplierId == _supA);
        taken.TakenByUserId = Guid.NewGuid();

        // The (mocked) calculation says supplier A still needs 4 after the taken draft was counted in transit.
        _lines = [Line(_milk, 4m, _supA, 12m)];
        await Svc().GenerateAsync(TenantId, null);

        var untakenA = _db.Where(r => r.SupplierId == _supA && r.TakenByUserId == null && r.Status == "draft").ToList();
        Assert.Single(untakenA);
        Assert.Equal(4m, untakenA[0].Items.Single().QuantityOrdered);
        Assert.Equal(10m, taken.Items.Single().QuantityOrdered);
    }

    [Fact]
    public async Task UnknownStore_Returns404()
    {
        _stores.GetActiveStoresAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ => new List<Location>());

        var (drafts, error, code) = await Svc().GenerateAsync(TenantId, Guid.NewGuid());

        Assert.Null(drafts);
        Assert.NotNull(error);
        Assert.Equal(404, code);
    }
}

/// <summary>TASK-755: the in-transit figure must only skip own untaken drafts when asked to.</summary>
public sealed class OrderCalcExcludeAutoDraftsTests
{
    private readonly IOrderCalcRepository _repo = Substitute.For<IOrderCalcRepository>();
    private readonly IEventRepository _events = Substitute.For<IEventRepository>();
    private readonly IWeatherRepository _weather = Substitute.For<IWeatherRepository>();
    private readonly ICannibalizationRepository _promo = Substitute.For<ICannibalizationRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly Guid _storeId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public OrderCalcExcludeAutoDraftsTests()
    {
        _repo.StoreExistsAsync(_storeId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetBuffersAsync(_storeId, Arg.Any<CancellationToken>()).Returns(
        [
            new ProductBuffer
            {
                StoreId = _storeId, ProductId = _productId, BufferTotal = 100m,
                Product = new Item { Name = "Milk" },
            },
        ]);
        _repo.GetStockOnHandAsync(_storeId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());
        // Without the flag every draft counts (40 = own untaken auto draft 30 + taken/manual 10);
        // with it only taken/manual drafts count (10).
        _repo.GetInTransitAsync(_storeId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>(), false)
            .Returns(new Dictionary<Guid, decimal> { [_productId] = 40m });
        _repo.GetInTransitAsync(_storeId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>(), true)
            .Returns(new Dictionary<Guid, decimal> { [_productId] = 10m });
        _repo.GetMoqUsqAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, (decimal, decimal)>());
        _repo.GetOpenMarketplaceInTransitAsync(_storeId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());
        _events.GetCandidatesForDateAsync(_storeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<DemandEvent>());
        _weather.GetForDateAsync(_storeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((WeatherData?)null);
        _weather.GetCoefficientsAsync(Arg.Any<CancellationToken>()).Returns(new List<WeatherCoefficient>());
        _promo.GetActivePromoCoefficientsAsync(_storeId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());
        _tenant.TenantId.Returns(Guid.NewGuid());
    }

    private OrderCalcService Sut() => new(_repo, _events, _weather, _promo, _tenant);

    [Fact]
    public async Task Default_CountsEveryDraft_ManualCalculateUnchanged()
    {
        var (result, _) = await Sut().CalculateAsync(_storeId);

        var line = Assert.Single(result!.Lines);
        Assert.Equal(40m, line.InTransit);
        Assert.Equal(60m, line.QuantityToOrder);   // 100 - 0 - 40
    }

    [Fact]
    public async Task ExcludeFlag_IgnoresOwnUntakenAutoDrafts_SoNeedIsNotHidden()
    {
        var (result, _) = await Sut().CalculateAsync(_storeId, default, excludeAutoUntakenDrafts: true);

        var line = Assert.Single(result!.Lines);
        Assert.Equal(10m, line.InTransit);          // taken + manual drafts still counted
        Assert.Equal(90m, line.QuantityToOrder);    // 100 - 0 - 10
    }
}
