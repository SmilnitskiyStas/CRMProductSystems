using NSubstitute;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-317: marketplace orders — the active-agreement gate, item validation,
/// order number sequencing, and the supplier-side status transition matrix.
/// </summary>
public sealed class MarketplaceOrderServiceTests
{
    private readonly IMarketplaceOrderRepository _orders = Substitute.For<IMarketplaceOrderRepository>();
    private readonly ISupplierAgreementRepository _agreements = Substitute.For<ISupplierAgreementRepository>();
    private readonly IMarketplaceRepository _marketplace = Substitute.For<IMarketplaceRepository>();
    private readonly ISupplierChatRepository _tenantNames = Substitute.For<ISupplierChatRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly ITenantSessionOverride _tenantSessionOverride = Substitute.For<ITenantSessionOverride>();
    private readonly IItemRepository _items = Substitute.For<IItemRepository>();
    private readonly IItemService _itemService = Substitute.For<IItemService>();
    private readonly MarketplaceOrderService _sut;

    private readonly Guid _supplierId = Guid.NewGuid();        // public marketplace supplier id
    private readonly Guid _supplierTenantId = Guid.NewGuid();
    private readonly Guid _clientTenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public MarketplaceOrderServiceTests()
    {
        _sut = new MarketplaceOrderService(
            _orders, _agreements, _marketplace, _tenantNames, _notifications, _tenantSessionOverride,
            _items, _itemService);

        _marketplace.GetSupplierTenantIdAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(_supplierTenantId);
        _tenantNames.GetTenantDisplayNameAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("Tenant");

        // TASK-584: UpdateOrderStatusAsync's Shipped branch runs its notification-enqueue +
        // SaveChanges tail inside _tenantSessionOverride.ExecuteAsync — same pure pass-through
        // convention as SupplierAgreementServiceTests (TASK-582): invokes the delegate
        // immediately instead of opening a real transaction, so assertions on the resulting order
        // state still work unchanged.
        _tenantSessionOverride
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<bool>>>()());

        // TASK-598: no barcodes set up on CatalogItem() by default → PlanCatalogOutcomeAsync's
        // collision check short-circuits (empty list), so every pre-existing CreateOrderAsync test
        // below hits the auto-create path. Stub it to always succeed so those tests don't need to
        // know anything about catalog auto-provisioning; dedicated tests further down override
        // this per-case and assert on the request/received calls instead.
        _items.GetByAnyBarcodeAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Item>());
        _itemService.CreateAsync(Arg.Any<Guid>(), Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => (FakeItemDto(ci.Arg<CreateProductRequest>().Name), (string?)null));
    }

    // ── The agreement gate ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateOrder_NoAgreement_ReturnsGateViolation()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns((SupplierAgreement?)null);

        var (order, error, isGateViolation) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId, OrderRequest(), _userId);

        Assert.Null(order);
        Assert.True(isGateViolation);
        Assert.Equal(MarketplaceOrderService.AgreementRequiredError, error);
        await _orders.DidNotReceive().AddAsync(Arg.Any<MarketplaceOrder>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(SupplierAgreementStatus.Pending)]
    [InlineData(SupplierAgreementStatus.AwaitingSignature)]
    public async Task CreateOrder_AgreementNotActive_ReturnsGateViolation(string status)
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(status));

        var (order, _, isGateViolation) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId, OrderRequest(), _userId);

        Assert.Null(order);
        Assert.True(isGateViolation);
    }

    // ── Creation happy path + validation ──────────────────────────────────────

    [Fact]
    public async Task CreateOrder_ActiveAgreement_SnapshotsItemsAndSequencesNumber()
    {
        var agreement = Agreement(SupplierAgreementStatus.Active);
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(agreement);

        var item = CatalogItem(price: 25.50m, minQty: 2);
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        _orders.CountForSupplierAsync(_supplierTenantId, Arg.Any<CancellationToken>()).Returns(4);

        MarketplaceOrder? created = null;
        _orders.AddAsync(Arg.Do<MarketplaceOrder>(o => created = o), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var destinationStoreId = Guid.NewGuid();
        var request = new CreateMarketplaceOrderDto(
            [new CreateMarketplaceOrderItemDto(item.Id, 3)], "  Терміново  ", destinationStoreId);

        var (dto, error, isGateViolation) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId, request, _userId);

        Assert.Null(error);
        Assert.False(isGateViolation);
        Assert.NotNull(dto);
        Assert.NotNull(created);
        Assert.Equal($"MP-{DateTime.UtcNow.Year}-005", created!.OrderNumber);
        Assert.Equal(agreement.Id, created.AgreementId);
        Assert.Equal(MarketplaceOrderStatus.New, created.Status);
        Assert.Equal("Терміново", created.Comment);
        Assert.Equal(_userId, created.CreatedByUserId);
        Assert.Equal(destinationStoreId, created.DestinationStoreId);
        Assert.Equal(destinationStoreId, dto!.DestinationStoreId);

        var line = Assert.Single(created.Items);
        Assert.Equal("Молоко 2.5%", line.ItemName);
        Assert.Equal(25.50m, line.Price);
        Assert.Equal(3, line.Qty);
        Assert.Equal(76.50m, line.LineTotal);
        Assert.Equal(76.50m, created.TotalAmount);
        await _orders.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_QtyBelowMinQty_ReturnsValidationError()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m, minQty: 5);
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var (dto, error, isGateViolation) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 2)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(dto);
        Assert.False(isGateViolation);
        Assert.Contains("Мінімальна кількість", error);
    }

    [Fact]
    public async Task CreateOrder_QtyAboveMaxQty_ReturnsValidationError()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m, maxQty: 10);
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 50)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(dto);
        Assert.Contains("Максимальна кількість", error);
    }

    [Fact]
    public async Task CreateOrder_UnavailableItem_ReturnsValidationError()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.IsAvailable = false;
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(dto);
        Assert.Contains("недоступна", error);
    }

    [Fact]
    public async Task CreateOrder_ForeignItem_ReturnsValidationError()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem>());

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(Guid.NewGuid(), 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(dto);
        Assert.Contains("не знайдено в каталозі", error);
    }

    [Fact]
    public async Task CreateOrder_EmptyItems_ReturnsValidationError()
    {
        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId, new CreateMarketplaceOrderDto([], null), _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.EmptyOrderError, error);
    }

    // ── TASK-586: DestinationStoreId required for every new order ──────────────

    [Fact]
    public async Task CreateOrder_NoDestinationStoreId_ReturnsValidationError()
    {
        var (dto, error, isGateViolation) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId, OrderRequest() with { DestinationStoreId = null }, _userId);

        Assert.Null(dto);
        Assert.False(isGateViolation);
        Assert.Equal(MarketplaceOrderService.DestinationStoreRequiredError, error);
        await _orders.DidNotReceive().AddAsync(Arg.Any<MarketplaceOrder>(), Arg.Any<CancellationToken>());
    }

    // ── TASK-598: marketplace catalog auto-provisioning ─────────────────────────

    [Fact]
    public async Task CheckCatalogConflicts_NoCollision_ReturnsEmptyList()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "111", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });
        // No override on _items.GetByAnyBarcodeAsync — constructor default (empty) applies.

        var (conflicts, error, isGateViolation) = await _sut.CheckCatalogConflictsAsync(
            _clientTenantId, _supplierId, [new CreateMarketplaceOrderItemDto(item.Id, 1)]);

        Assert.Null(error);
        Assert.False(isGateViolation);
        Assert.NotNull(conflicts);
        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task CheckCatalogConflicts_Collision_ReturnsMatchedItemDetails()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "111", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var existing = new Item
        {
            TenantId = _clientTenantId,
            Name = "Молоко існуюче",
            Barcodes = ["111"],
            ImageUrl = "https://x/img.jpg",
        };
        _items.GetByAnyBarcodeAsync(Arg.Is<IReadOnlyList<string>>(l => l.Contains("111")), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { existing });

        var (conflicts, error, _) = await _sut.CheckCatalogConflictsAsync(
            _clientTenantId, _supplierId, [new CreateMarketplaceOrderItemDto(item.Id, 1)]);

        Assert.Null(error);
        Assert.NotNull(conflicts);
        var conflict = Assert.Single(conflicts);
        Assert.Equal(item.Id, conflict.SupplierItemId);
        Assert.Equal(existing.Id, conflict.ExistingItem.Id);
        Assert.Equal("Молоко існуюче", conflict.ExistingItem.Name);
        Assert.Equal("https://x/img.jpg", conflict.ExistingItem.ImageUrl);
        Assert.Equal(["111"], conflict.ExistingItem.Barcodes);
    }

    [Fact]
    public async Task CreateOrder_NoCollision_AutoCreatesItemWithSourceSupplierItemIdAndBarcodes()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 25.50m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "222", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        await _itemService.Received(1).CreateAsync(
            _clientTenantId,
            Arg.Is<CreateProductRequest>(r =>
                r.Name == "Молоко 2.5%" &&
                r.Barcodes != null && r.Barcodes.SequenceEqual(new[] { "222" }) &&
                r.PricePurchase == 25.50m &&
                r.ManagementType == "NA" &&
                r.SourceSupplierItemId == item.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_CollisionWithAutoAction_FailsAndCreatesNoItem()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "333", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });
        _items.GetByAnyBarcodeAsync(Arg.Is<IReadOnlyList<string>>(l => l.Contains("333")), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { new() { TenantId = _clientTenantId, Name = "Існуючий", Barcodes = ["333"] } });

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.BarcodeCollisionError, error);
        await _itemService.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>());
        await _orders.DidNotReceive().AddAsync(Arg.Any<MarketplaceOrder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_CollisionWithLinkAction_LinksExistingItemAndCreatesNoNewOne()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "444", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var existing = new Item { TenantId = _clientTenantId, Name = "Існуючий", Barcodes = ["444"] };
        _items.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto(
                [new CreateMarketplaceOrderItemDto(item.Id, 1, "link", existing.Id)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(item.Id, existing.SourceSupplierItemId);
        _items.Received(1).Update(existing);
        await _items.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _itemService.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_LinkAction_LinkedItemNotOwnedByTenant_ReturnsError()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "555", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var foreignItemId = Guid.NewGuid();
        // Ambient RLS would scope GetByIdAsync to the caller's own tenant — a foreign-tenant id
        // simply resolves to null, same as "not found".
        _items.GetByIdAsync(foreignItemId, Arg.Any<CancellationToken>()).Returns((Item?)null);

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto(
                [new CreateMarketplaceOrderItemDto(item.Id, 1, "link", foreignItemId)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.LinkedItemNotFoundError, error);
    }

    [Fact]
    public async Task CreateOrder_LinkAction_LinkedItemBarcodeMismatch_ReturnsError()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "666", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var unrelated = new Item { TenantId = _clientTenantId, Name = "Не той товар", Barcodes = ["999"] };
        _items.GetByIdAsync(unrelated.Id, Arg.Any<CancellationToken>()).Returns(unrelated);

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto(
                [new CreateMarketplaceOrderItemDto(item.Id, 1, "link", unrelated.Id)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.LinkedItemBarcodeMismatchError, error);
        Assert.Null(unrelated.SourceSupplierItemId);
    }

    [Fact]
    public async Task CreateOrder_CollisionWithCreateNewAction_CreatesDuplicateItemAnyway()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "777", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });
        _items.GetByAnyBarcodeAsync(Arg.Is<IReadOnlyList<string>>(l => l.Contains("777")), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { new() { TenantId = _clientTenantId, Name = "Існуючий", Barcodes = ["777"] } });

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto(
                [new CreateMarketplaceOrderItemDto(item.Id, 1, "create_new")], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        await _itemService.Received(1).CreateAsync(
            _clientTenantId,
            Arg.Is<CreateProductRequest>(r => r.SourceSupplierItemId == item.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_SupplierItemWithNoBarcodes_SkipsCollisionCheckAndAutoCreates()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m); // no barcodes added
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        await _items.DidNotReceive().GetByAnyBarcodeAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _itemService.Received(1).CreateAsync(
            _clientTenantId, Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Client cancellation ────────────────────────────────────────────────────

    [Fact]
    public async Task CancelOrder_NewOrder_SetsCancelledWithReason()
    {
        var order = Order(MarketplaceOrderStatus.New);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.CancelOrderAsync(_clientTenantId, order.Id, "Передумали");

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(MarketplaceOrderStatus.Cancelled, order.Status);
        Assert.Equal("Передумали", order.CancelReason);
    }

    [Theory]
    [InlineData(MarketplaceOrderStatus.Confirmed)]
    [InlineData(MarketplaceOrderStatus.Shipped)]
    [InlineData(MarketplaceOrderStatus.Delivered)]
    [InlineData(MarketplaceOrderStatus.Cancelled)]
    public async Task CancelOrder_NotNew_ReturnsError(string status)
    {
        var order = Order(status);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.CancelOrderAsync(_clientTenantId, order.Id, "Причина");

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OnlyNewCancellableError, error);
    }

    [Fact]
    public async Task CancelOrder_ForeignClientTenant_ReturnsNotFound()
    {
        var order = Order(MarketplaceOrderStatus.New);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.CancelOrderAsync(Guid.NewGuid(), order.Id, "Причина");

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OrderNotFoundError, error);
    }

    // ── Supplier-side status transition matrix ─────────────────────────────────

    [Theory]
    [InlineData(MarketplaceOrderStatus.New,       MarketplaceOrderStatus.Confirmed, true)]
    [InlineData(MarketplaceOrderStatus.New,       MarketplaceOrderStatus.Cancelled, true)]
    [InlineData(MarketplaceOrderStatus.New,       MarketplaceOrderStatus.Shipped,   false)]
    [InlineData(MarketplaceOrderStatus.New,       MarketplaceOrderStatus.Delivered, false)]
    [InlineData(MarketplaceOrderStatus.Confirmed, MarketplaceOrderStatus.Shipped,   true)]
    [InlineData(MarketplaceOrderStatus.Confirmed, MarketplaceOrderStatus.Cancelled, true)]
    [InlineData(MarketplaceOrderStatus.Confirmed, MarketplaceOrderStatus.Delivered, false)]
    [InlineData(MarketplaceOrderStatus.Confirmed, MarketplaceOrderStatus.New,       false)]
    // TASK-586, ADR-033 Decision 4: Shipped has no supplier-initiated transition any more —
    // Delivered is now set exclusively by MarketplaceOrderReceiptService's receiving flow.
    [InlineData(MarketplaceOrderStatus.Shipped,   MarketplaceOrderStatus.Delivered, false)]
    [InlineData(MarketplaceOrderStatus.Shipped,   MarketplaceOrderStatus.Cancelled, false)]
    [InlineData(MarketplaceOrderStatus.Delivered, MarketplaceOrderStatus.Cancelled, false)]
    [InlineData(MarketplaceOrderStatus.Cancelled, MarketplaceOrderStatus.Confirmed, false)]
    public async Task UpdateOrderStatus_TransitionMatrix(string from, string to, bool allowed)
    {
        var order = Order(from);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        // EstimatedDeliveryDays is only consulted on the Shipped branch, but supplying it
        // unconditionally keeps this matrix test focused on the transition check itself.
        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            _supplierTenantId, order.Id,
            new UpdateMarketplaceOrderStatusDto(to, "причина", EstimatedDeliveryDays: 3));

        if (allowed)
        {
            Assert.Null(error);
            Assert.NotNull(dto);
            Assert.Equal(to, order.Status);
        }
        else
        {
            Assert.Null(dto);
            Assert.NotNull(error);
            Assert.Equal(from, order.Status);
        }
    }

    [Fact]
    public async Task UpdateOrderStatus_CancelWithoutReason_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.New);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            _supplierTenantId, order.Id,
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Cancelled, "  "));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.CancelReasonRequiredError, error);
        Assert.Equal(MarketplaceOrderStatus.New, order.Status);
    }

    // ── TASK-584: shipping requires an ETA + fires a client notification ────────

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UpdateOrderStatus_ShipWithoutValidEstimatedDeliveryDays_ReturnsError(int? days)
    {
        var order = Order(MarketplaceOrderStatus.Confirmed);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            _supplierTenantId, order.Id,
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Shipped, EstimatedDeliveryDays: days));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.EstimatedDeliveryDaysRequiredError, error);
        Assert.Equal(MarketplaceOrderStatus.Confirmed, order.Status);
        Assert.Null(order.ShippedAt);
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOrderStatus_ShipWithValidEstimatedDeliveryDays_SetsShippedAtAndNotifiesClient()
    {
        var order = Order(MarketplaceOrderStatus.Confirmed);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            _supplierTenantId, order.Id,
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Shipped, EstimatedDeliveryDays: 3));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(MarketplaceOrderStatus.Shipped, order.Status);
        Assert.Equal(3, order.EstimatedDeliveryDays);
        Assert.NotNull(order.ShippedAt);
        Assert.Equal(3, dto!.EstimatedDeliveryDays);
        Assert.NotNull(dto.ShippedAt);

        // The enqueue + SaveChanges must run under the CLIENT tenant's RLS override, never the
        // ambient (supplier) session — the TASK-582 regression this guards against.
        await _tenantSessionOverride.Received(1).ExecuteAsync(
            order.ClientTenantId, Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationQueue>(n =>
                n.TenantId == order.ClientTenantId &&
                n.UserId == null &&
                n.Channel == "system" &&
                n.Status == "pending" &&
                n.EventType == "marketplace_order.shipped"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOrderStatus_Deliver_NoLongerReachable_ReturnsError()
    {
        // TASK-586, ADR-033 Decision 4: the supplier's one-click Deliver is gone — Shipped has
        // no entry in AllowedTransitions any more, so this always falls through to the generic
        // "transition not possible" error. MarketplaceOrderReceiptService.ReceiveAsync is now
        // the only code path that may set Status = Delivered.
        var order = Order(MarketplaceOrderStatus.Shipped);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            _supplierTenantId, order.Id, new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Delivered));

        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.Equal(MarketplaceOrderStatus.Shipped, order.Status);
        Assert.Null(order.DeliveredAt);
        await _tenantSessionOverride.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(), Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOrderStatus_ForeignSupplierTenant_ReturnsNotFound()
    {
        var order = Order(MarketplaceOrderStatus.New);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            Guid.NewGuid(), order.Id,
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Confirmed));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OrderNotFoundError, error);
    }

    [Fact]
    public async Task UpdateOrderStatus_UnknownStatus_ReturnsError()
    {
        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            _supplierTenantId, Guid.NewGuid(), new UpdateMarketplaceOrderStatusDto("bogus"));

        Assert.Null(dto);
        Assert.Contains("Невідомий статус", error);
    }

    // ── TASK-585: recording a shipping delay reason ─────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetDelayReason_EmptyReason_ReturnsError(string? reason)
    {
        var order = Order(MarketplaceOrderStatus.Shipped);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.SetDelayReasonAsync(_supplierTenantId, order.Id, reason!);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.DelayReasonRequiredError, error);
        Assert.Null(order.DelayReason);
        await _orders.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetDelayReason_OrderNotFound_ReturnsError()
    {
        _orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrder?)null);

        var (dto, error) = await _sut.SetDelayReasonAsync(_supplierTenantId, Guid.NewGuid(), "Затримка на митниці");

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OrderNotFoundError, error);
    }

    [Fact]
    public async Task SetDelayReason_ForeignSupplierTenant_ReturnsNotFound()
    {
        var order = Order(MarketplaceOrderStatus.Shipped);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.SetDelayReasonAsync(Guid.NewGuid(), order.Id, "Затримка на митниці");

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OrderNotFoundError, error);
        Assert.Null(order.DelayReason);
    }

    [Theory]
    [InlineData(MarketplaceOrderStatus.New)]
    [InlineData(MarketplaceOrderStatus.Confirmed)]
    [InlineData(MarketplaceOrderStatus.Delivered)]
    [InlineData(MarketplaceOrderStatus.Cancelled)]
    public async Task SetDelayReason_NotShipped_ReturnsError(string status)
    {
        var order = Order(status);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.SetDelayReasonAsync(_supplierTenantId, order.Id, "Затримка на митниці");

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OnlyShippedCanHaveDelayReasonError, error);
        Assert.Null(order.DelayReason);
    }

    [Fact]
    public async Task SetDelayReason_ShippedOrder_SetsReasonAndNotifiesClient()
    {
        var order = Order(MarketplaceOrderStatus.Shipped);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.SetDelayReasonAsync(
            _supplierTenantId, order.Id, "  Затримка на митниці  ");

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal("Затримка на митниці", order.DelayReason);
        Assert.Equal("Затримка на митниці", dto!.DelayReason);

        // Same cross-tenant RLS guard as the Shipped-notification branch (TASK-584/TASK-582):
        // the enqueue + SaveChanges must run under the CLIENT tenant's override.
        await _tenantSessionOverride.Received(1).ExecuteAsync(
            order.ClientTenantId, Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationQueue>(n =>
                n.TenantId == order.ClientTenantId &&
                n.UserId == null &&
                n.Channel == "system" &&
                n.Status == "pending" &&
                n.EventType == "marketplace_order.delay_reason_added"),
            Arg.Any<CancellationToken>());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static CreateMarketplaceOrderDto OrderRequest() =>
        new([new CreateMarketplaceOrderItemDto(Guid.NewGuid(), 1)], null, Guid.NewGuid());

    private SupplierAgreement Agreement(string status) => new()
    {
        SupplierTenantId = _supplierTenantId,
        ClientTenantId   = _clientTenantId,
        Status           = status,
    };

    private SupplierItem CatalogItem(decimal? price, int? minQty = null, int? maxQty = null) => new()
    {
        SupplierId  = _supplierId,
        TenantId    = _supplierTenantId,
        CustomName  = "Молоко 2.5%",
        Price       = price,
        MinQty      = minQty,
        MaxQty      = maxQty,
        Unit        = "л",
        IsAvailable = true,
    };

    private MarketplaceOrder Order(string status) => new()
    {
        OrderNumber      = "MP-2026-001",
        AgreementId      = Guid.NewGuid(),
        SupplierTenantId = _supplierTenantId,
        ClientTenantId   = _clientTenantId,
        Status           = status,
    };

    private static ItemDto FakeItemDto(string name, Guid? id = null) => new(
        id ?? Guid.NewGuid(), [], name, null, null, null, null, "шт", "NA", "product",
        0, 0, 0, null, null, null, null, null, 0, null, null, null, true, DateTime.UtcNow, null, null, "standard");
}
