using NSubstitute;
using ShelfGuard.Application.Features.Catalog;
using ShelfGuard.Application.Features.Catalog.Dtos;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Features.SupplierInventory;
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
    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ISupplierStockRepository _supplierStock = Substitute.For<ISupplierStockRepository>();
    private readonly MarketplaceOrderService _sut;

    private readonly Guid _supplierId = Guid.NewGuid();        // public marketplace supplier id
    private readonly Guid _supplierTenantId = Guid.NewGuid();
    private readonly Guid _clientTenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _supplierUserId = Guid.NewGuid();    // TASK-693: supplier-side actor for status changes

    public MarketplaceOrderServiceTests()
    {
        _sut = new MarketplaceOrderService(
            _orders, _agreements, _marketplace, _tenantNames, _notifications, _tenantSessionOverride,
            _items, _itemService, _locations, _users, _tenants, _supplierStock);

        _marketplace.GetSupplierTenantIdAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(_supplierTenantId);

        // Phase 3 (plan D4): supplier_inventory is provider-granted and default-OFF, so the
        // legacy confirmed→shipped flow every pre-existing test exercises must keep working with
        // no warehouse module at all. Dedicated shipping tests below opt in per case.
        _tenants.GetByIdAsync(_supplierTenantId, Arg.Any<CancellationToken>())
            .Returns(Tenant.Create("Постачальник", "supplier-tenant"));

        // #4: CreateOrderAsync snapshots the placing user's display name onto the order.
        _users.GetByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(User.Create(_clientTenantId, "buyer@example.com", "Олена Замовниця", "hash", "store_manager"));

        // TASK-693 (Phase 7): UpdateOrderStatusAsync / ShipOrderAsync snapshot the supplier-side
        // acting user's display name onto the order (ConfirmedByUserName / ShippedByUserName).
        _users.GetByIdAsync(_supplierUserId, Arg.Any<CancellationToken>())
            .Returns(User.Create(_supplierTenantId, "petro@supplier.com", "Петро Постачальник", "hash", "supplier_admin"));
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

        // TASK-645 C1: NextOrderNumberAsync now counts the supplier's orders under the SUPPLIER
        // tenant's RLS context (marketplace_orders' tenant_isolation is OR-based, so a client
        // session would only see its own orders and MP-{yyyy}-{NNN} would restart per client).
        // Same pass-through convention, different generic argument.
        _tenantSessionOverride
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Func<Task<string>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<string>>>()());

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

        // TASK-645 C1: the per-supplier sequence must be counted under the SUPPLIER tenant's RLS
        // context, not the calling client's. (The real cross-client collision this prevents is
        // only observable against a live DB — see MarketplaceProviderBypassScopeRlsIntegrationTests.)
        await _tenantSessionOverride.Received(1).ExecuteAsync(
            _supplierTenantId, Arg.Any<Func<Task<string>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_SnapshotsCreatorNameAndNotifiesSupplierTenant()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 12m);
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        MarketplaceOrder? created = null;
        _orders.AddAsync(Arg.Do<MarketplaceOrder>(o => created = o), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(created);
        Assert.Equal("Олена Замовниця", created!.CreatedByUserName);
        Assert.Equal(_userId, dto!.CreatedByUserId);
        Assert.Equal("Олена Замовниця", dto.CreatedByUserName);

        // #3: the "new order" outbox row targets the SUPPLIER tenant (not the client), and the
        // enqueue runs under an explicit override of the supplier tenant's RLS context.
        await _tenantSessionOverride.Received(1).ExecuteAsync(
            _supplierTenantId, Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationQueue>(n =>
                n.TenantId == _supplierTenantId &&
                n.UserId == null &&
                n.Channel == "system" &&
                n.Status == "pending" &&
                n.EventType == "marketplace_order.created"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_UnknownCreator_LeavesCreatorNameNull()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));
        _users.GetByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var item = CatalogItem(price: 12m);
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        MarketplaceOrder? created = null;
        _orders.AddAsync(Arg.Do<MarketplaceOrder>(o => created = o), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (_, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(created);
        Assert.Null(created!.CreatedByUserName);
        Assert.Equal(_userId, created.CreatedByUserId);
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

    // ── TASK-650: destination region snapshot ─────────────────────────────────

    [Fact]
    public async Task CreateOrder_SnapshotsDestinationRegionCodeFromLocation()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var destinationStoreId = Guid.NewGuid();
        _locations.GetByIdAsync(destinationStoreId, Arg.Any<CancellationToken>())
            .Returns(new Location { Id = destinationStoreId, TenantId = _clientTenantId, RegionCode = "UA-30" });

        MarketplaceOrder? created = null;
        _orders.AddAsync(Arg.Do<MarketplaceOrder>(o => created = o), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, destinationStoreId),
            _userId);

        Assert.Null(error);
        Assert.NotNull(created);
        Assert.Equal("UA-30", created!.DestinationRegionCode);
    }

    [Fact]
    public async Task CreateOrder_UnknownDestinationLocation_LeavesRegionCodeNull()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        // _locations.GetByIdAsync returns null by default (foreign/unknown id under client RLS).
        MarketplaceOrder? created = null;
        _orders.AddAsync(Arg.Do<MarketplaceOrder>(o => created = o), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var (_, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(created);
        Assert.Null(created!.DestinationRegionCode);
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
        // TASK-697: the "link" branch now loads via the no-include GetForBarcodeMergeAsync.
        _items.GetForBarcodeMergeAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(existing);

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

        // TASK-643/KI-036: this test used to stub GetByIdAsync → null on the (disproved)
        // assumption that ambient RLS scopes it to the caller's tenant. IItemRepository carries
        // no app-level TenantId filter, and under the leaked provider role a foreign-tenant row
        // DID resolve here — which is what armed the cross-tenant write vector. Stub what the
        // real repository actually returns in that situation, and assert the application-level
        // ownership check rejects it. Barcode matches on purpose: the barcode guard alone would
        // have let this through.
        var foreignItem = new Item
        {
            TenantId = Guid.NewGuid(),          // NOT _clientTenantId
            Name     = "Чужий товар",
            Barcodes = ["555"],
        };
        _items.GetForBarcodeMergeAsync(foreignItem.Id, Arg.Any<CancellationToken>()).Returns(foreignItem);

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto(
                [new CreateMarketplaceOrderItemDto(item.Id, 1, "link", foreignItem.Id)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(dto);
        // Reported exactly like "not found" — never confirms another tenant owns that id.
        Assert.Equal(MarketplaceOrderService.LinkedItemNotFoundError, error);
        Assert.Null(foreignItem.SourceSupplierItemId);
        _items.DidNotReceive().Update(Arg.Any<Item>());
        await _items.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_LinkAction_MissingLinkedItem_StillReturnsSameNotFoundError()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "5551", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var missingId = Guid.NewGuid();
        _items.GetForBarcodeMergeAsync(missingId, Arg.Any<CancellationToken>()).Returns((Item?)null);

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto(
                [new CreateMarketplaceOrderItemDto(item.Id, 1, "link", missingId)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.LinkedItemNotFoundError, error);
    }

    // ── TASK-643/KI-036: cross-tenant catalog leak — application-level defence in depth ────────

    [Fact]
    public async Task CheckCatalogConflicts_IgnoresBarcodeMatchOwnedByAnotherTenant()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "888", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        // What the real repository returned under the leaked provider role: another tenant's Item
        // sharing this EAN. The reported symptom was a "barcode already in your catalog" dialog
        // shown to a client whose own catalog was completely empty — leaking the foreign Item's
        // id, name, image and full barcode list.
        _items.GetByAnyBarcodeAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Contains("888")), Arg.Any<CancellationToken>())
            .Returns(new List<Item>
            {
                new() { TenantId = Guid.NewGuid(), Name = "Чуже молоко", Barcodes = ["888"] },
            });

        var (conflicts, error, _) = await _sut.CheckCatalogConflictsAsync(
            _clientTenantId, _supplierId, [new CreateMarketplaceOrderItemDto(item.Id, 1)]);

        Assert.Null(error);
        Assert.NotNull(conflicts);
        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task CheckCatalogConflicts_ForeignAndOwnMatch_ReportsOnlyTheOwnTenantItem()
    {
        // Over-correction guard: filtering by tenant must not swallow a genuine own-tenant
        // collision just because a foreign row happens to sort first in the result set.
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "889", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var foreign = new Item { TenantId = Guid.NewGuid(), Name = "Чуже", Barcodes = ["889"] };
        var own = new Item
        {
            TenantId = _clientTenantId,
            Name     = "Моє існуюче",
            Barcodes = ["889"],
            ImageUrl = "https://x/own.jpg",
        };
        _items.GetByAnyBarcodeAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Contains("889")), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { foreign, own });

        var (conflicts, error, _) = await _sut.CheckCatalogConflictsAsync(
            _clientTenantId, _supplierId, [new CreateMarketplaceOrderItemDto(item.Id, 1)]);

        Assert.Null(error);
        Assert.NotNull(conflicts);
        var conflict = Assert.Single(conflicts);
        Assert.Equal(own.Id, conflict.ExistingItem.Id);
        Assert.Equal("Моє існуюче", conflict.ExistingItem.Name);
    }

    [Fact]
    public async Task CreateOrder_ForeignTenantBarcodeCollision_ProceedsAndAutoCreatesItem()
    {
        // The functional half of the bug: a foreign tenant's row sharing the supplier item's EAN
        // raised a bogus BarcodeCollisionError and blocked a legitimate order outright.
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "999", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });
        _items.GetByAnyBarcodeAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Contains("999")), Arg.Any<CancellationToken>())
            .Returns(new List<Item>
            {
                new() { TenantId = Guid.NewGuid(), Name = "Чужий дублікат", Barcodes = ["999"] },
            });

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        await _itemService.Received(1).CreateAsync(
            _clientTenantId,
            Arg.Is<CreateProductRequest>(r => r.SourceSupplierItemId == item.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_ForeignLinkOnSecondLine_NeverUpdatesAnyItem()
    {
        // Pass 1 (plan every line) and pass 2 (execute) are separated by a whole loop, so a
        // failure on a later line must not leave an earlier line's write already applied — and a
        // foreign-tenant link must never reach _items.Update at all.
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var lineA = CatalogItem(price: 10m);
        lineA.Barcodes.Add(new SupplierItemBarcode { Barcode = "1001", Kind = "primary" });
        var lineB = CatalogItem(price: 20m);
        lineB.Barcodes.Add(new SupplierItemBarcode { Barcode = "1002", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { lineA, lineB });

        var ownItem = new Item { TenantId = _clientTenantId, Name = "Моє", Barcodes = ["1001"] };
        var foreignItem = new Item { TenantId = Guid.NewGuid(), Name = "Чуже", Barcodes = ["1002"] };
        _items.GetForBarcodeMergeAsync(ownItem.Id, Arg.Any<CancellationToken>()).Returns(ownItem);
        _items.GetForBarcodeMergeAsync(foreignItem.Id, Arg.Any<CancellationToken>()).Returns(foreignItem);

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto(
            [
                new CreateMarketplaceOrderItemDto(lineA.Id, 1, "link", ownItem.Id),
                new CreateMarketplaceOrderItemDto(lineB.Id, 1, "link", foreignItem.Id),
            ], null, Guid.NewGuid()),
            _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.LinkedItemNotFoundError, error);
        _items.DidNotReceive().Update(Arg.Any<Item>());
        await _items.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Null(ownItem.SourceSupplierItemId);
        Assert.Null(foreignItem.SourceSupplierItemId);
        await _orders.DidNotReceive().AddAsync(Arg.Any<MarketplaceOrder>(), Arg.Any<CancellationToken>());
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
        _items.GetForBarcodeMergeAsync(unrelated.Id, Arg.Any<CancellationToken>()).Returns(unrelated);

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

    // ── TASK-697: repeat order of an already-linked item ────────────────────────

    [Fact]
    public async Task CheckCatalogConflicts_IdenticalNameIdenticalBarcode_FirstOrder_StillReportsConflict()
    {
        // Name is deliberately not checked: even a same-name, same-barcode own Item that isn't
        // yet linked to this supplier item is a conflict — the client picks "link" once.
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "111", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var own = new Item
        {
            TenantId = _clientTenantId, Name = "Молоко 2.5%", Barcodes = ["111"], SourceSupplierItemId = null,
        };
        _items.GetByAnyBarcodeAsync(Arg.Is<IReadOnlyList<string>>(l => l.Contains("111")), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { own });

        var (conflicts, error, _) = await _sut.CheckCatalogConflictsAsync(
            _clientTenantId, _supplierId, [new CreateMarketplaceOrderItemDto(item.Id, 1)]);

        Assert.Null(error);
        Assert.NotNull(conflicts);
        var conflict = Assert.Single(conflicts);
        Assert.Equal(own.Id, conflict.ExistingItem.Id);
    }

    [Fact]
    public async Task CheckCatalogConflicts_AlreadyLinkedRepeatOrder_ReturnsZeroConflicts()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "111", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var linked = new Item
        {
            TenantId = _clientTenantId, Name = "Молоко 2.5%", Barcodes = ["111"], SourceSupplierItemId = item.Id,
        };
        _items.GetByAnyBarcodeAsync(Arg.Is<IReadOnlyList<string>>(l => l.Contains("111")), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { linked });

        var (conflicts, error, _) = await _sut.CheckCatalogConflictsAsync(
            _clientTenantId, _supplierId, [new CreateMarketplaceOrderItemDto(item.Id, 1)]);

        Assert.Null(error);
        Assert.NotNull(conflicts);
        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task CreateOrder_AlreadyLinked_SupplierAddedBarcode_MergesReordersPrimaryAndReportsChange()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "222", Kind = "primary" });
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "111", Kind = "alternate" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var linked = new Item
        {
            TenantId = _clientTenantId, Name = "Молоко 2.5%", Barcodes = ["111"], SourceSupplierItemId = item.Id,
        };
        _items.GetByAnyBarcodeAsync(Arg.Is<IReadOnlyList<string>>(l => l.Contains("222")), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { linked });

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        // New supplier primary moved to the front; the client's original barcode is kept.
        Assert.Equal(["222", "111"], linked.Barcodes);
        _items.Received(1).Update(linked);
        await _items.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _itemService.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>());

        var change = Assert.Single(dto!.CatalogChanges);
        Assert.Equal(linked.Id, change.ItemId);
        Assert.Equal(["222"], change.AddedBarcodes);
        Assert.True(change.PrimaryChanged);
        Assert.Equal("222", change.NewPrimaryBarcode);
    }

    [Fact]
    public async Task CreateOrder_AlreadyLinked_NoNewBarcodes_NoWriteNoChangeRecord()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "222", Kind = "primary" });
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "111", Kind = "alternate" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var linked = new Item
        {
            TenantId = _clientTenantId, Name = "Молоко 2.5%", Barcodes = ["222", "111"], SourceSupplierItemId = item.Id,
        };
        _items.GetByAnyBarcodeAsync(Arg.Is<IReadOnlyList<string>>(l => l.Contains("222")), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { linked });

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Empty(dto!.CatalogChanges);
        _items.DidNotReceive().Update(Arg.Any<Item>());
        await _items.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _itemService.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOrder_MergeNeverDropsExistingBarcode()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "222", Kind = "primary" });
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "111", Kind = "alternate" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        // The client had added a manual barcode "AAA" of their own — the merge must keep it.
        var linked = new Item
        {
            TenantId = _clientTenantId, Name = "Молоко 2.5%", Barcodes = ["111", "AAA"], SourceSupplierItemId = item.Id,
        };
        _items.GetByAnyBarcodeAsync(Arg.Is<IReadOnlyList<string>>(l => l.Contains("111")), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { linked });

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto([new CreateMarketplaceOrderItemDto(item.Id, 1)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(["222", "111", "AAA"], linked.Barcodes);
    }

    [Fact]
    public async Task CreateOrder_GenuinelyDifferentProduct_NotLinked_AutoAction_StillFails()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "111", Kind = "primary" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var own = new Item
        {
            TenantId = _clientTenantId, Name = "Зовсім інше", Barcodes = ["111"], SourceSupplierItemId = null,
        };
        _items.GetByAnyBarcodeAsync(Arg.Is<IReadOnlyList<string>>(l => l.Contains("111")), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { own });

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
    public async Task CreateOrder_LinkModalBranch_SetsSourceAndMergesBarcodes()
    {
        _agreements.GetForPairAsync(_supplierTenantId, _clientTenantId, Arg.Any<CancellationToken>())
            .Returns(Agreement(SupplierAgreementStatus.Active));

        var item = CatalogItem(price: 10m);
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "222", Kind = "primary" });
        item.Barcodes.Add(new SupplierItemBarcode { Barcode = "111", Kind = "alternate" });
        _marketplace.GetSupplierItemsAsync(_supplierId, Arg.Any<CancellationToken>())
            .Returns(new List<SupplierItem> { item });

        var own = new Item
        {
            TenantId = _clientTenantId, Name = "Моє молоко", Barcodes = ["111"], SourceSupplierItemId = null,
        };
        _items.GetForBarcodeMergeAsync(own.Id, Arg.Any<CancellationToken>()).Returns(own);

        var (dto, error, _) = await _sut.CreateOrderAsync(
            _clientTenantId, _supplierId,
            new CreateMarketplaceOrderDto(
                [new CreateMarketplaceOrderItemDto(item.Id, 1, "link", own.Id)], null, Guid.NewGuid()),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(item.Id, own.SourceSupplierItemId);
        Assert.Equal(["222", "111"], own.Barcodes);
        _items.Received(1).Update(own);
        await _itemService.DidNotReceive().CreateAsync(
            Arg.Any<Guid>(), Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>());

        var change = Assert.Single(dto!.CatalogChanges);
        Assert.Equal(own.Id, change.ItemId);
        Assert.Equal(["222"], change.AddedBarcodes);
        Assert.True(change.PrimaryChanged);
        Assert.Equal("222", change.NewPrimaryBarcode);
    }

    // ── TASK-697: MergeBarcodes (direct unit) ──────────────────────────────────

    [Fact]
    public void MergeBarcodes_AppendsNewSupplierBarcodesInOrder_KeepingExistingFirst()
    {
        var (merged, added, primaryChanged, changed) =
            MarketplaceOrderService.MergeBarcodes(["111"], ["111", "222", "333"], null);

        Assert.Equal(["111", "222", "333"], merged);
        Assert.Equal(["222", "333"], added);
        Assert.False(primaryChanged);
        Assert.True(changed);
    }

    [Fact]
    public void MergeBarcodes_MovesSupplierPrimaryToFront()
    {
        var (merged, added, primaryChanged, changed) =
            MarketplaceOrderService.MergeBarcodes(["111", "AAA"], ["222", "111"], "222");

        Assert.Equal(["222", "111", "AAA"], merged);
        Assert.Equal(["222"], added);
        Assert.True(primaryChanged);
        Assert.True(changed);
    }

    [Fact]
    public void MergeBarcodes_NoOp_WhenSupplierSetIsSubsetAndPrimaryAlreadyFirst()
    {
        var (merged, added, primaryChanged, changed) =
            MarketplaceOrderService.MergeBarcodes(["222", "111"], ["222", "111"], "222");

        Assert.Equal(["222", "111"], merged);
        Assert.Empty(added);
        Assert.False(primaryChanged);
        Assert.False(changed);
    }

    [Fact]
    public void MergeBarcodes_AbsentPrimary_LeavesExistingOrder()
    {
        var (merged, added, primaryChanged, changed) =
            MarketplaceOrderService.MergeBarcodes(["111", "222"], ["222"], null);

        Assert.Equal(["111", "222"], merged);
        Assert.Empty(added);
        Assert.False(primaryChanged);
        Assert.False(changed);
    }

    [Fact]
    public void MergeBarcodes_EmptyExisting_TakesSupplierSetWithPrimaryFirst()
    {
        var (merged, added, primaryChanged, changed) =
            MarketplaceOrderService.MergeBarcodes([], ["111", "222"], "222");

        Assert.Equal(["222", "111"], merged);
        Assert.Equal(["111", "222"], added);
        Assert.True(primaryChanged);
        Assert.True(changed);
    }

    [Fact]
    public void MergeBarcodes_TrimsAndDeduplicates()
    {
        var (merged, added, _, _) =
            MarketplaceOrderService.MergeBarcodes(["111"], [" 111 ", "222", "222", ""], null);

        Assert.Equal(["111", "222"], merged);
        Assert.Equal(["222"], added);
    }

    // ── Client cancellation ────────────────────────────────────────────────────

    [Theory]
    [InlineData(MarketplaceOrderStatus.New)]
    [InlineData(MarketplaceOrderStatus.Confirmed)]   // TASK-693 (Phase 7): cancellable until it ships
    public async Task CancelOrder_BeforeShipping_SetsCancelledWithReason(string status)
    {
        var order = Order(status);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.CancelOrderAsync(_clientTenantId, order.Id, "Передумали");

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(MarketplaceOrderStatus.Cancelled, order.Status);
        Assert.Equal("Передумали", order.CancelReason);
    }

    [Theory]
    [InlineData(MarketplaceOrderStatus.Shipped)]
    [InlineData(MarketplaceOrderStatus.Delivered)]
    [InlineData(MarketplaceOrderStatus.Cancelled)]
    public async Task CancelOrder_ShippedOrLater_ReturnsError(string status)
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
            new UpdateMarketplaceOrderStatusDto(to, "причина", EstimatedDeliveryDays: 3), _supplierUserId);

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
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Cancelled, "  "), _supplierUserId);

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
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Shipped, EstimatedDeliveryDays: days), _supplierUserId);

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
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Shipped, EstimatedDeliveryDays: 3), _supplierUserId);

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
            _supplierTenantId, order.Id, new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Delivered), _supplierUserId);

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
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Confirmed), _supplierUserId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OrderNotFoundError, error);
    }

    [Fact]
    public async Task UpdateOrderStatus_UnknownStatus_ReturnsError()
    {
        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            _supplierTenantId, Guid.NewGuid(), new UpdateMarketplaceOrderStatusDto("bogus"), _supplierUserId);

        Assert.Null(dto);
        Assert.Contains("Невідомий статус", error);
    }

    // ── TASK-693 (Phase 7): supplier-side actor snapshot on confirm / ship ──────

    [Fact]
    public async Task UpdateOrderStatus_Confirm_SnapshotsConfirmingSupplierUser()
    {
        var order = Order(MarketplaceOrderStatus.New);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            _supplierTenantId, order.Id,
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Confirmed), _supplierUserId);

        Assert.Null(error);
        Assert.Equal(_supplierUserId, order.ConfirmedByUserId);
        Assert.Equal("Петро Постачальник", order.ConfirmedByUserName);
        Assert.Equal(_supplierUserId, dto!.ConfirmedByUserId);
        Assert.Equal("Петро Постачальник", dto.ConfirmedByUserName);
        // TASK-695 (Phase 8): the confirm timestamp is stamped next to the actor.
        Assert.NotNull(order.ConfirmedAt);
        Assert.InRange(
            order.ConfirmedAt!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        // Confirming never touches the shipped actor.
        Assert.Null(order.ShippedByUserId);
        Assert.Null(order.ShippedByUserName);
    }

    [Fact]
    public async Task UpdateOrderStatus_Ship_LegacyPath_SnapshotsShippingSupplierUser()
    {
        var order = Order(MarketplaceOrderStatus.Confirmed);
        order.Items.Add(OrderLine(order, "Молоко 2.5%", 10m, Guid.NewGuid()));
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            _supplierTenantId, order.Id,
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Shipped, EstimatedDeliveryDays: 2),
            _supplierUserId);

        Assert.Null(error);
        Assert.Equal(MarketplaceOrderStatus.Shipped, order.Status);
        Assert.Equal(_supplierUserId, order.ShippedByUserId);
        Assert.Equal("Петро Постачальник", order.ShippedByUserName);
        Assert.Equal("Петро Постачальник", dto!.ShippedByUserName);
    }

    [Fact]
    public async Task ShipOrder_SnapshotsShippingSupplierUser()
    {
        EnableSupplierInventory();

        var supplierItemId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var order = Order(MarketplaceOrderStatus.Confirmed);
        order.Items.Add(OrderLine(order, "Молоко 2.5%", 40m, supplierItemId));
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var only = Batch(supplierItemId, warehouseId, new DateOnly(2026, 12, 1), 40m, "B-1");
        StubWarehouse(warehouseId);
        StubFefo(supplierItemId, warehouseId, only);

        var (dto, error, _) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id,
            new ShipOrderRequest(SourceWarehouseId: warehouseId, EstimatedDeliveryDays: 3),
            _supplierUserId);

        Assert.Null(error);
        Assert.Equal(_supplierUserId, order.ShippedByUserId);
        Assert.Equal("Петро Постачальник", order.ShippedByUserName);
        Assert.Equal("Петро Постачальник", dto!.ShippedByUserName);
    }

    // ── Phase 3 (plan D4): batch-consuming shipment ─────────────────────────────

    [Fact]
    public async Task ShipOrder_ModuleOff_ShipsWithoutTouchingStock()
    {
        var order = Order(MarketplaceOrderStatus.Confirmed);
        order.Items.Add(OrderLine(order, "Молоко 2.5%", 10m, Guid.NewGuid()));
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error, warnings) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id,
            new ShipOrderRequest(EstimatedDeliveryDays: 3), _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Empty(warnings);
        Assert.Equal(MarketplaceOrderStatus.Shipped, order.Status);
        Assert.Equal(3, order.EstimatedDeliveryDays);
        Assert.NotNull(order.ShippedAt);
        Assert.Null(order.SourceWarehouseId);

        // Nothing consumed, nothing allocated — the pre-Phase-3 behaviour, byte for byte.
        await _supplierStock.DidNotReceive().GetFefoOrderedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _orders.DidNotReceive().AddOrderItemBatchAsync(
            Arg.Any<MarketplaceOrderItemBatch>(), Arg.Any<CancellationToken>());
        await _supplierStock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

        // The client still gets its shipped notification, under the client RLS override.
        await _tenantSessionOverride.Received(1).ExecuteAsync(
            order.ClientTenantId, Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationQueue>(n => n.EventType == "marketplace_order.shipped"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShipOrder_ModuleOffButAllocationsSent_IsRejected()
    {
        var order = Order(MarketplaceOrderStatus.Confirmed);
        var line = OrderLine(order, "Молоко 2.5%", 10m, Guid.NewGuid());
        order.Items.Add(line);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error, _) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id,
            new ShipOrderRequest(
                SourceWarehouseId: Guid.NewGuid(),
                EstimatedDeliveryDays: 2,
                Lines: [new ShipLineDto(line.Id, [new ShipAllocationDto(Guid.NewGuid(), 5m)])]),
            _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.SupplierInventoryDisabledError, error);
        Assert.Equal(MarketplaceOrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public async Task ShipOrder_NotConfirmed_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.New);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error, _) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id, new ShipOrderRequest(EstimatedDeliveryDays: 1), _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OnlyConfirmedCanShipError, error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ShipOrder_WithoutAnyDeliveryEstimate_ReturnsError(int? days)
    {
        var order = Order(MarketplaceOrderStatus.Confirmed);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error, _) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id, new ShipOrderRequest(EstimatedDeliveryDays: days), _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.EstimatedDeliveryDaysRequiredError, error);
        Assert.Equal(MarketplaceOrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public async Task ShipOrder_ExpectedDateOnly_DerivesEstimatedDays()
    {
        var order = Order(MarketplaceOrderStatus.Confirmed);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var expected = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        var (dto, error, _) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id,
            new ShipOrderRequest(ExpectedDeliveryDate: expected), _userId);

        Assert.Null(error);
        Assert.Equal(expected, order.ExpectedDeliveryDate);
        Assert.Equal(5, order.EstimatedDeliveryDays);
        Assert.Equal(expected, dto!.ExpectedDeliveryDate);
    }

    [Fact]
    public async Task ShipOrder_ModuleOnFullCoverage_ConsumesBatchesWritesMovementsAndShips()
    {
        EnableSupplierInventory();

        var supplierItemId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var order = Order(MarketplaceOrderStatus.Confirmed);
        var line = OrderLine(order, "Молоко 2.5%", 120m, supplierItemId);
        order.Items.Add(line);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var near = Batch(supplierItemId, warehouseId, new DateOnly(2026, 12, 1), 100m, "B-1");
        var far  = Batch(supplierItemId, warehouseId, new DateOnly(2027, 2, 1), 50m, "B-2");
        StubWarehouse(warehouseId);
        StubFefo(supplierItemId, warehouseId, near, far);

        var (dto, error, warnings) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id,
            new ShipOrderRequest(SourceWarehouseId: warehouseId, EstimatedDeliveryDays: 3),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Empty(warnings);

        // FEFO: the nearest-expiry batch is drained first, the remainder comes off the next one.
        Assert.Equal(0m, near.Quantity);
        Assert.Equal(30m, far.Quantity);

        await _supplierStock.Received(2).AddMovementAsync(
            Arg.Is<SupplierStockMovement>(m =>
                m.MovementType == "ship"
                && m.FromWarehouseId == warehouseId
                && m.ReferenceType == "marketplace_order"
                && m.ReferenceId == order.Id
                && m.PerformedBy == _userId),
            Arg.Any<CancellationToken>());

        await _orders.Received(1).AddOrderItemBatchAsync(
            Arg.Is<MarketplaceOrderItemBatch>(b =>
                b.OrderItemId == line.Id
                && b.OrderId == order.Id
                && b.SupplierTenantId == _supplierTenantId
                && b.ClientTenantId == _clientTenantId
                && b.SupplierStockId == near.Id
                && b.BatchNumber == "B-1"
                && b.Qty == 100m),
            Arg.Any<CancellationToken>());
        await _orders.Received(1).AddOrderItemBatchAsync(
            Arg.Is<MarketplaceOrderItemBatch>(b => b.SupplierStockId == far.Id && b.Qty == 20m),
            Arg.Any<CancellationToken>());

        // One atomic commit for stock + movements + batches + the order's status change …
        await _supplierStock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(MarketplaceOrderStatus.Shipped, order.Status);
        Assert.Equal(warehouseId, order.SourceWarehouseId);
        Assert.NotNull(order.ShippedAt);

        // … and the outbox row separately, under the CLIENT tenant's RLS override.
        await _tenantSessionOverride.Received(1).ExecuteAsync(
            order.ClientTenantId, Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationQueue>(n =>
                n.TenantId == order.ClientTenantId && n.EventType == "marketplace_order.shipped"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShipOrder_ModuleOnShortfall_ShipsAnywayWithWarningAndPartialBatches()
    {
        EnableSupplierInventory();

        var supplierItemId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var order = Order(MarketplaceOrderStatus.Confirmed);
        order.Items.Add(OrderLine(order, "Молоко 2.5%", 120m, supplierItemId));
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var only = Batch(supplierItemId, warehouseId, new DateOnly(2026, 12, 1), 40m, "B-1");
        StubWarehouse(warehouseId);
        StubFefo(supplierItemId, warehouseId, only);

        var (dto, error, warnings) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id,
            new ShipOrderRequest(SourceWarehouseId: warehouseId, EstimatedDeliveryDays: 3),
            _userId);

        // User decision 2026-09-02: a shortfall never blocks the shipment.
        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(MarketplaceOrderStatus.Shipped, order.Status);
        Assert.Equal(0m, only.Quantity);

        var warning = Assert.Single(warnings);
        Assert.Contains("Молоко 2.5%", warning);
        Assert.Contains("40", warning);
        Assert.Contains("120", warning);

        await _orders.Received(1).AddOrderItemBatchAsync(
            Arg.Is<MarketplaceOrderItemBatch>(b => b.Qty == 40m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShipOrder_ExplicitAllocations_WinOverAutoFefo()
    {
        EnableSupplierInventory();

        var supplierItemId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var order = Order(MarketplaceOrderStatus.Confirmed);
        var line = OrderLine(order, "Молоко 2.5%", 30m, supplierItemId);
        order.Items.Add(line);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var near = Batch(supplierItemId, warehouseId, new DateOnly(2026, 12, 1), 100m, "B-1");
        var far  = Batch(supplierItemId, warehouseId, new DateOnly(2027, 2, 1), 50m, "B-2");
        StubWarehouse(warehouseId);
        StubFefo(supplierItemId, warehouseId, near, far);
        StubBatchById(near, far);

        // Supplier deliberately picks the LATER batch — the explicit plan must be honoured
        // verbatim, not silently re-FEFO'd.
        var (dto, error, warnings) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id,
            new ShipOrderRequest(
                SourceWarehouseId: warehouseId,
                EstimatedDeliveryDays: 2,
                Lines: [new ShipLineDto(line.Id, [new ShipAllocationDto(far.Id, 30m)])]),
            _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Empty(warnings);
        Assert.Equal(100m, near.Quantity);
        Assert.Equal(20m, far.Quantity);

        await _orders.Received(1).AddOrderItemBatchAsync(
            Arg.Is<MarketplaceOrderItemBatch>(b => b.SupplierStockId == far.Id && b.Qty == 30m),
            Arg.Any<CancellationToken>());
        await _supplierStock.DidNotReceive().GetFefoOrderedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShipOrder_ExplicitAllocationFromAnotherWarehouse_IsRejected()
    {
        EnableSupplierInventory();

        var supplierItemId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var order = Order(MarketplaceOrderStatus.Confirmed);
        var line = OrderLine(order, "Молоко 2.5%", 10m, supplierItemId);
        order.Items.Add(line);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var foreignWarehouseBatch = Batch(
            supplierItemId, Guid.NewGuid(), new DateOnly(2026, 12, 1), 100m, "B-9");
        StubWarehouse(warehouseId);
        StubBatchById(foreignWarehouseBatch);

        var (dto, error, _) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id,
            new ShipOrderRequest(
                SourceWarehouseId: warehouseId,
                EstimatedDeliveryDays: 2,
                Lines: [new ShipLineDto(line.Id, [new ShipAllocationDto(foreignWarehouseBatch.Id, 5m)])]),
            _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.BatchWarehouseMismatchError, error);
        Assert.Equal(MarketplaceOrderStatus.Confirmed, order.Status);
        Assert.Equal(100m, foreignWarehouseBatch.Quantity);
    }

    [Fact]
    public async Task ShipOrder_UnknownWarehouse_ReturnsError()
    {
        EnableSupplierInventory();

        var order = Order(MarketplaceOrderStatus.Confirmed);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _supplierStock.WarehouseExistsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var (dto, error, _) = await _sut.ShipOrderAsync(
            _supplierTenantId, order.Id,
            new ShipOrderRequest(SourceWarehouseId: Guid.NewGuid(), EstimatedDeliveryDays: 2),
            _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.SourceWarehouseNotFoundError, error);
    }

    [Fact]
    public async Task ShipOrder_ForeignSupplierTenant_ReturnsNotFound()
    {
        var order = Order(MarketplaceOrderStatus.Confirmed);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error, _) = await _sut.ShipOrderAsync(
            Guid.NewGuid(), order.Id, new ShipOrderRequest(EstimatedDeliveryDays: 2), _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OrderNotFoundError, error);
        Assert.Equal(MarketplaceOrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public async Task UpdateOrderStatus_Ship_RoutesThroughShipOrderAsync_LegacyBehaviourUnchanged()
    {
        // Regression guard for the one-code-path refactor: the legacy status endpoint must keep
        // behaving exactly as it did (no warehouse, no allocations, ETA required, client notified)
        // even on a tenant whose supplier_inventory module is ON.
        EnableSupplierInventory();

        var order = Order(MarketplaceOrderStatus.Confirmed);
        order.Items.Add(OrderLine(order, "Молоко 2.5%", 10m, Guid.NewGuid()));
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.UpdateOrderStatusAsync(
            _supplierTenantId, order.Id,
            new UpdateMarketplaceOrderStatusDto(MarketplaceOrderStatus.Shipped, EstimatedDeliveryDays: 4), _supplierUserId);

        Assert.Null(error);
        Assert.Equal(MarketplaceOrderStatus.Shipped, order.Status);
        Assert.Equal(4, order.EstimatedDeliveryDays);
        Assert.Null(order.SourceWarehouseId);
        Assert.Equal(4, dto!.EstimatedDeliveryDays);

        await _orders.DidNotReceive().AddOrderItemBatchAsync(
            Arg.Any<MarketplaceOrderItemBatch>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationQueue>(n => n.EventType == "marketplace_order.shipped"),
            Arg.Any<CancellationToken>());
    }

    // ── Phase 3: FEFO ship suggestion (read-only) ───────────────────────────────

    [Fact]
    public async Task GetShipSuggestion_ProposesFefoSplitAndReportsShortfall()
    {
        EnableSupplierInventory();

        var supplierItemId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var order = Order(MarketplaceOrderStatus.Confirmed);
        var line = OrderLine(order, "Молоко 2.5%", 200m, supplierItemId);
        order.Items.Add(line);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var near = Batch(supplierItemId, warehouseId, new DateOnly(2026, 12, 1), 100m, "B-1");
        var far  = Batch(supplierItemId, warehouseId, new DateOnly(2027, 2, 1), 50m, "B-2");
        StubFefo(supplierItemId, warehouseId, near, far);
        _locations.GetByIdAsync(warehouseId, Arg.Any<CancellationToken>())
            .Returns(new Location { Id = warehouseId, TenantId = _supplierTenantId, Name = "Основний", Type = "warehouse" });

        var (suggestion, error) = await _sut.GetShipSuggestionAsync(
            _supplierTenantId, order.Id, warehouseId);

        Assert.Null(error);
        Assert.NotNull(suggestion);
        Assert.Equal("Основний", suggestion!.WarehouseName);

        var proposed = Assert.Single(suggestion.Lines);
        Assert.Equal(line.Id, proposed.OrderItemId);
        Assert.Equal(150m, proposed.Covered);
        Assert.Equal(50m, proposed.Shortfall);
        Assert.Equal(2, proposed.Allocations.Count);
        Assert.Equal(near.Id, proposed.Allocations[0].SupplierStockId);
        Assert.Equal(100m, proposed.Allocations[0].Qty);
        Assert.Equal(50m, proposed.Allocations[1].Qty);
        Assert.Single(suggestion.Warnings);

        // Read-only: nothing decremented, nothing written.
        Assert.Equal(100m, near.Quantity);
        Assert.Equal(50m, far.Quantity);
        await _orders.DidNotReceive().AddOrderItemBatchAsync(
            Arg.Any<MarketplaceOrderItemBatch>(), Arg.Any<CancellationToken>());
        await _supplierStock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetShipSuggestion_ForeignWarehouse_ReadsAsNotFound()
    {
        EnableSupplierInventory();

        var order = Order(MarketplaceOrderStatus.Confirmed);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var foreignWarehouseId = Guid.NewGuid();
        _locations.GetByIdAsync(foreignWarehouseId, Arg.Any<CancellationToken>())
            .Returns(new Location { Id = foreignWarehouseId, TenantId = Guid.NewGuid(), Name = "Чужий", Type = "warehouse" });

        var (suggestion, error) = await _sut.GetShipSuggestionAsync(
            _supplierTenantId, order.Id, foreignWarehouseId);

        Assert.Null(suggestion);
        Assert.Equal(MarketplaceOrderService.SourceWarehouseNotFoundError, error);
    }

    // ── Phase 3 helpers ─────────────────────────────────────────────────────────

    private void EnableSupplierInventory()
    {
        var tenant = Tenant.Create("Постачальник", "supplier-tenant");
        tenant.UpdateModules(["marketplace_supplier", "supplier_inventory"]);
        _tenants.GetByIdAsync(_supplierTenantId, Arg.Any<CancellationToken>()).Returns(tenant);
    }

    private void StubWarehouse(Guid warehouseId) =>
        _supplierStock.WarehouseExistsAsync(_supplierTenantId, warehouseId, Arg.Any<CancellationToken>())
            .Returns(true);

    private void StubFefo(Guid supplierItemId, Guid warehouseId, params SupplierStock[] batches) =>
        _supplierStock.GetFefoOrderedAsync(
                _supplierTenantId, supplierItemId, warehouseId, Arg.Any<CancellationToken>())
            .Returns(_ => batches.OrderBy(b => b.ExpiryDate).ToList());

    private void StubBatchById(params SupplierStock[] batches)
    {
        foreach (var batch in batches)
            _supplierStock.GetByIdAsync(_supplierTenantId, batch.Id, Arg.Any<CancellationToken>())
                .Returns(batch);
    }

    private static SupplierStock Batch(
        Guid supplierItemId, Guid warehouseId, DateOnly expiry, decimal qty, string? batchNumber) => new()
    {
        SupplierItemId = supplierItemId,
        WarehouseId = warehouseId,
        ExpiryDate = expiry,
        Quantity = qty,
        QuantityInitial = qty,
        BatchNumber = batchNumber,
        Status = "safe",
    };

    private MarketplaceOrderItem OrderLine(
        MarketplaceOrder order, string name, decimal qty, Guid? supplierItemId) => new()
    {
        OrderId = order.Id,
        SupplierTenantId = _supplierTenantId,
        ClientTenantId = _clientTenantId,
        SupplierItemId = supplierItemId,
        ItemName = name,
        Unit = "шт",
        Price = 10m,
        Qty = qty,
        LineTotal = 10m * qty,
    };

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

    // ── Phase 4 (plan D5): mutable delivery-date reschedule ─────────────────────

    [Fact]
    public async Task SetExpectedDeliveryDate_ShippedOrder_SetsDateAndNotifiesClient()
    {
        var order = Order(MarketplaceOrderStatus.Shipped);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var newDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);

        var (dto, error) = await _sut.SetExpectedDeliveryDateAsync(_supplierTenantId, order.Id, newDate);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(newDate, order.ExpectedDeliveryDate);
        Assert.Equal(newDate, dto!.ExpectedDeliveryDate);

        // Same cross-tenant RLS guard as the delay-reason branch: enqueue + SaveChanges under the
        // CLIENT tenant's override.
        await _tenantSessionOverride.Received(1).ExecuteAsync(
            order.ClientTenantId, Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationQueue>(n =>
                n.TenantId == order.ClientTenantId &&
                n.UserId == null &&
                n.Channel == "system" &&
                n.Status == "pending" &&
                n.EventType == "marketplace_order.delivery_rescheduled"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(MarketplaceOrderStatus.New)]
    [InlineData(MarketplaceOrderStatus.Confirmed)]
    [InlineData(MarketplaceOrderStatus.Delivered)]
    [InlineData(MarketplaceOrderStatus.Cancelled)]
    public async Task SetExpectedDeliveryDate_NotShipped_ReturnsError(string status)
    {
        var order = Order(status);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.SetExpectedDeliveryDateAsync(
            _supplierTenantId, order.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OnlyShippedCanRescheduleError, error);
        Assert.Null(order.ExpectedDeliveryDate);
        await _notifications.DidNotReceive().EnqueueAsync(Arg.Any<NotificationQueue>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetExpectedDeliveryDate_PastDate_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.Shipped);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.SetExpectedDeliveryDateAsync(
            _supplierTenantId, order.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.RescheduleDateInPastError, error);
        Assert.Null(order.ExpectedDeliveryDate);
    }

    [Fact]
    public async Task SetExpectedDeliveryDate_ForeignSupplierTenant_ReturnsNotFound()
    {
        var order = Order(MarketplaceOrderStatus.Shipped);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.SetExpectedDeliveryDateAsync(
            Guid.NewGuid(), order.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderService.OrderNotFoundError, error);
        Assert.Null(order.ExpectedDeliveryDate);
    }

    [Fact]
    public async Task SetExpectedDeliveryDate_IsRepeatable()
    {
        var order = Order(MarketplaceOrderStatus.Shipped);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var first = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(4);
        var second = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(9);

        var (_, e1) = await _sut.SetExpectedDeliveryDateAsync(_supplierTenantId, order.Id, first);
        var (dto, e2) = await _sut.SetExpectedDeliveryDateAsync(_supplierTenantId, order.Id, second);

        Assert.Null(e1);
        Assert.Null(e2);
        Assert.Equal(second, order.ExpectedDeliveryDate);
        Assert.Equal(second, dto!.ExpectedDeliveryDate);
        await _notifications.Received(2).EnqueueAsync(
            Arg.Is<NotificationQueue>(n => n.EventType == "marketplace_order.delivery_rescheduled"),
            Arg.Any<CancellationToken>());
    }

    // ── "New order arrived" badge (supplier-portal expansion #3, Phase 6a) ─────

    [Fact]
    public async Task GetUnseenOrderCount_NullMarker_CountsEveryNonCancelledOrder()
    {
        _users.GetByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(User.Create(_supplierTenantId, "admin@supplier.com", "Адмін", "hash", "supplier_admin"));
        _orders.CountUnseenForSupplierAsync(_supplierTenantId, null, Arg.Any<CancellationToken>())
            .Returns(7);

        var count = await _sut.GetUnseenOrderCountForSupplierAsync(_supplierTenantId, _userId);

        Assert.Equal(7, count);
        await _orders.Received(1).CountUnseenForSupplierAsync(
            _supplierTenantId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUnseenOrderCount_WithMarker_PassesTheMarkerToTheRepository()
    {
        var user = User.Create(_supplierTenantId, "admin@supplier.com", "Адмін", "hash", "supplier_admin");
        user.MarkSupplierOrdersViewed();
        _users.GetByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);
        _orders.CountUnseenForSupplierAsync(
                _supplierTenantId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(2);

        var count = await _sut.GetUnseenOrderCountForSupplierAsync(_supplierTenantId, _userId);

        Assert.Equal(2, count);
        await _orders.Received(1).CountUnseenForSupplierAsync(
            _supplierTenantId,
            Arg.Is<DateTimeOffset?>(d => d != null && d.Value.UtcDateTime == user.SupplierOrdersLastViewedAt!.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkSupplierOrdersSeen_StampsTheMarkerAndSaves()
    {
        var user = User.Create(_supplierTenantId, "admin@supplier.com", "Адмін", "hash", "supplier_admin");
        _users.GetByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);

        await _sut.MarkSupplierOrdersSeenAsync(_userId);

        Assert.NotNull(user.SupplierOrdersLastViewedAt);
        _users.Received(1).Update(user);
        await _users.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkSupplierOrdersSeen_ThenUnseenCountReturnsZero()
    {
        // A real user instance whose marker MarkSupplierOrdersSeenAsync mutates; the repository
        // stub reflects "nothing created after the marker".
        var user = User.Create(_supplierTenantId, "admin@supplier.com", "Адмін", "hash", "supplier_admin");
        _users.GetByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(user);
        _orders.CountUnseenForSupplierAsync(
                _supplierTenantId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<DateTimeOffset?>(1) is null ? 3 : 0);

        Assert.Equal(3, await _sut.GetUnseenOrderCountForSupplierAsync(_supplierTenantId, _userId));

        await _sut.MarkSupplierOrdersSeenAsync(_userId);

        Assert.Equal(0, await _sut.GetUnseenOrderCountForSupplierAsync(_supplierTenantId, _userId));
    }

    [Fact]
    public async Task MarkSupplierOrdersSeen_UnknownUser_IsNoOp()
    {
        _users.GetByIdAsync(_userId, Arg.Any<CancellationToken>()).Returns((User?)null);

        await _sut.MarkSupplierOrdersSeenAsync(_userId);

        await _users.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
        0, 0, 0, null, null, null, null, null, 0, null, null, null, null, null, true, DateTime.UtcNow, null, null, "standard");
}
