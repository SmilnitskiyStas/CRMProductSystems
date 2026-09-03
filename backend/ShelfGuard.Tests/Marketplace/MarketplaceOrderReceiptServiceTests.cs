using NSubstitute;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-586, ADR-033: client-confirmed marketplace order receiving — draft creation/pre-population,
/// per-item scan/count update, and the finalize gate (ProductId + QuantityReceived + ExpiryDate
/// on every item) that creates ProductStock/StockMovement and sets the order to Delivered.
/// TASK-599, Wave 2: receipt-item Price/ReferenceImageUrl enrichment, and the discrepancy-notes
/// -> auto-opened supplier support ticket + notification side effect on ReceiveAsync.
/// </summary>
public sealed class MarketplaceOrderReceiptServiceTests
{
    private readonly IMarketplaceOrderReceiptRepository _receipts = Substitute.For<IMarketplaceOrderReceiptRepository>();
    private readonly IMarketplaceOrderRepository _orders = Substitute.For<IMarketplaceOrderRepository>();
    private readonly IMarketplaceOrderService _orderService = Substitute.For<IMarketplaceOrderService>();
    private readonly IItemRepository _items = Substitute.For<IItemRepository>();
    private readonly IMarketplaceRepository _marketplace = Substitute.For<IMarketplaceRepository>();
    private readonly ISupplierSupportService _supplierSupport = Substitute.For<ISupplierSupportService>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly ITenantSessionOverride _tenantSessionOverride = Substitute.For<ITenantSessionOverride>();
    private readonly MarketplaceOrderReceiptService _sut;

    private readonly Guid _supplierTenantId = Guid.NewGuid();
    private readonly Guid _clientTenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();

    public MarketplaceOrderReceiptServiceTests()
    {
        _sut = new MarketplaceOrderReceiptService(
            _receipts, _orders, _orderService, _items, _marketplace, _supplierSupport,
            _notifications, _tenantSessionOverride);

        // No images by default — tests that care about the reference-photo fallback override this.
        _marketplace.GetSupplierItemImagesByIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<SupplierItemImage>>());

        // Phase 3 (plan D4): no supplier batch allocations by default — legacy orders and
        // module-off shipments, where a draft still gets exactly one item per order line.
        _receipts.GetOrderItemBatchesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MarketplaceOrderItemBatch>());

        // Same pure pass-through convention as MarketplaceOrderServiceTests (TASK-584): invokes
        // the delegate immediately instead of opening a real transaction/RLS override.
        _tenantSessionOverride
            .ExecuteAsync(Arg.Any<Guid>(), Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task<bool>>>()());

        _supplierSupport.CreateSystemTicketAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(new SupplierSupportTicketDto(
                Guid.NewGuid(), ci.ArgAt<Guid>(1), ci.ArgAt<Guid>(0), "Supplier", "Client",
                ci.ArgAt<string>(3), SupplierSupportTicketStatus.Open, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)));
    }

    // ── ListAwaitingReceiptAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ListAwaitingReceiptAsync_DelegatesToOrderService()
    {
        var expected = new List<MarketplaceOrderDto>();
        _orderService.ListAwaitingReceiptForClientAsync(_clientTenantId, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _sut.ListAwaitingReceiptAsync(_clientTenantId);

        Assert.Same(expected, result);
    }

    // ── GetOrCreateDraftAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrCreateDraft_OrderNotFound_ReturnsError()
    {
        _orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrder?)null);

        var (dto, error) = await _sut.GetOrCreateDraftAsync(_clientTenantId, Guid.NewGuid(), _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.OrderNotFoundError, error);
    }

    [Fact]
    public async Task GetOrCreateDraft_ForeignClientTenant_ReturnsOrderNotFound()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var (dto, error) = await _sut.GetOrCreateDraftAsync(Guid.NewGuid(), order.Id, _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.OrderNotFoundError, error);
    }

    [Fact]
    public async Task GetOrCreateDraft_ExistingReceipt_ReturnsItIdempotently()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var existing = Receipt(order, "received");
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var (dto, error) = await _sut.GetOrCreateDraftAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(existing.Id, dto!.Id);
        await _receipts.DidNotReceive().AddAsync(Arg.Any<MarketplaceOrderReceipt>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateDraft_OrderNotShipped_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.Confirmed, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrderReceipt?)null);

        var (dto, error) = await _sut.GetOrCreateDraftAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.OrderNotShippedError, error);
    }

    [Fact]
    public async Task GetOrCreateDraft_NoDestinationStoreId_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, destinationStoreId: null);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrderReceipt?)null);

        var (dto, error) = await _sut.GetOrCreateDraftAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.DestinationStoreMissingError, error);
    }

    [Fact]
    public async Task GetOrCreateDraft_Shipped_PrePopulatesOneItemPerOrderLine()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        var line1 = OrderItem(order, "Молоко 2.5%", 5m);
        var line2 = OrderItem(order, "Хліб житній", 10m);
        order.Items.Add(line1);
        order.Items.Add(line2);

        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrderReceipt?)null);

        MarketplaceOrderReceipt? created = null;
        _receipts.AddAsync(Arg.Do<MarketplaceOrderReceipt>(r => created = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _receipts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => created);

        var (dto, error) = await _sut.GetOrCreateDraftAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(error);
        Assert.NotNull(created);
        Assert.Equal(order.Id, created!.MarketplaceOrderId);
        Assert.Equal(_clientTenantId, created.ClientTenantId);
        Assert.Equal(_supplierTenantId, created.SupplierTenantId);
        Assert.Equal(_storeId, created.DestinationStoreId);
        Assert.Equal("draft", created.Status);
        Assert.Equal(_userId, created.CreatedByUserId);

        Assert.Equal(2, created.Items.Count);
        var item1 = Assert.Single(created.Items, i => i.MarketplaceOrderItemId == line1.Id);
        Assert.Equal("Молоко 2.5%", item1.ItemNameSnapshot);
        Assert.Equal(5m, item1.QuantityOrdered);
        Assert.Null(item1.ProductId);
        Assert.Null(item1.QuantityReceived);
        Assert.Null(item1.ExpiryDate);
        Assert.Equal(_clientTenantId, item1.ClientTenantId);
        Assert.Equal(_supplierTenantId, item1.SupplierTenantId);

        await _receipts.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.NotNull(dto);
        Assert.Equal(2, dto!.Items.Count);
    }

    // ── Phase 3 (plan D4): 1→N prefill from the supplier's shipped batches ───────

    [Fact]
    public async Task GetOrCreateDraft_LineWithThreeBatches_CreatesThreePrefilledItems()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        var line = OrderItem(order, "Молоко 2.5%", 120m);
        order.Items.Add(line);

        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrderReceipt?)null);

        var b1 = Batch(order, line, new DateOnly(2026, 12, 1), 60m, "B-1");
        var b2 = Batch(order, line, new DateOnly(2027, 1, 15), 40m, "B-2");
        var b3 = Batch(order, line, new DateOnly(2027, 3, 1), 20m, null);
        _receipts.GetOrderItemBatchesAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns([b3, b1, b2]); // deliberately out of order — the service sorts FEFO

        MarketplaceOrderReceipt? created = null;
        _receipts.AddAsync(Arg.Do<MarketplaceOrderReceipt>(r => created = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _receipts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci => created);

        var (dto, error) = await _sut.GetOrCreateDraftAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(error);
        Assert.NotNull(created);
        Assert.Equal(3, created!.Items.Count);
        Assert.All(created.Items, i =>
        {
            Assert.Equal(line.Id, i.MarketplaceOrderItemId);
            Assert.Equal("Молоко 2.5%", i.ItemNameSnapshot);
            // The employee still scans + counts — only the expiry half of the finalize gate
            // arrives pre-answered.
            Assert.Null(i.ProductId);
            Assert.Null(i.QuantityReceived);
            Assert.NotNull(i.ExpiryDate);
            Assert.NotNull(i.SourceOrderItemBatchId);
        });

        var ordered = created.Items.OrderBy(i => i.ExpiryDate).ToList();
        Assert.Equal(new DateOnly(2026, 12, 1), ordered[0].ExpiryDate);
        Assert.Equal(60m, ordered[0].QuantityOrdered);
        Assert.Equal("B-1", ordered[0].BatchNumber);
        Assert.Equal(b1.Id, ordered[0].SourceOrderItemBatchId);
        Assert.Equal(20m, ordered[2].QuantityOrdered);
        Assert.Null(ordered[2].BatchNumber);

        Assert.NotNull(dto);
        Assert.Equal(3, dto!.Items.Count);
        Assert.All(dto.Items, i => Assert.NotNull(i.SourceOrderItemBatchId));
    }

    [Fact]
    public async Task GetOrCreateDraft_NoBatches_FallsBackToOneItemPerLine()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        var line1 = OrderItem(order, "Молоко 2.5%", 5m);
        var line2 = OrderItem(order, "Хліб житній", 10m);
        order.Items.Add(line1);
        order.Items.Add(line2);

        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrderReceipt?)null);
        // Constructor default: no batches at all (legacy / module-off shipment).

        MarketplaceOrderReceipt? created = null;
        _receipts.AddAsync(Arg.Do<MarketplaceOrderReceipt>(r => created = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _receipts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci => created);

        var (_, error) = await _sut.GetOrCreateDraftAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(error);
        Assert.Equal(2, created!.Items.Count);
        Assert.All(created.Items, i =>
        {
            Assert.Null(i.SourceOrderItemBatchId);
            Assert.Null(i.ExpiryDate);
            Assert.Null(i.BatchNumber);
        });
        Assert.Equal(5m, Assert.Single(created.Items, i => i.MarketplaceOrderItemId == line1.Id).QuantityOrdered);
    }

    [Fact]
    public async Task GetOrCreateDraft_MixedLines_PrefillsOnlyTheShippedOne()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        var shipped = OrderItem(order, "Молоко 2.5%", 100m);
        var uncovered = OrderItem(order, "Хліб житній", 20m); // shortfall — shipped with no batch
        order.Items.Add(shipped);
        order.Items.Add(uncovered);

        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrderReceipt?)null);
        _receipts.GetOrderItemBatchesAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns([Batch(order, shipped, new DateOnly(2026, 12, 1), 100m, "B-1")]);

        MarketplaceOrderReceipt? created = null;
        _receipts.AddAsync(Arg.Do<MarketplaceOrderReceipt>(r => created = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _receipts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ci => created);

        var (_, error) = await _sut.GetOrCreateDraftAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(error);
        Assert.Equal(2, created!.Items.Count);

        var prefilled = Assert.Single(created.Items, i => i.MarketplaceOrderItemId == shipped.Id);
        Assert.Equal(new DateOnly(2026, 12, 1), prefilled.ExpiryDate);
        Assert.NotNull(prefilled.SourceOrderItemBatchId);

        var blank = Assert.Single(created.Items, i => i.MarketplaceOrderItemId == uncovered.Id);
        Assert.Null(blank.ExpiryDate);
        Assert.Null(blank.SourceOrderItemBatchId);
        Assert.Equal(20m, blank.QuantityOrdered);
    }

    [Fact]
    public async Task Receive_TwoBatchSublinesOfOneOrderLine_CreatesTwoStockBatchesAndDelivers()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        var line = OrderItem(order, "Молоко 2.5%", 120m);
        order.Items.Add(line);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        // What GetOrCreateDraftAsync would have produced from two shipped batches.
        var productId = Guid.NewGuid();
        var receipt = Receipt(order, "draft");
        var sub1 = ReceiptItem(receipt, line);
        sub1.ExpiryDate = new DateOnly(2026, 12, 1);
        sub1.BatchNumber = "B-1";
        sub1.SourceOrderItemBatchId = Guid.NewGuid();
        sub1.ProductId = productId;
        sub1.QuantityReceived = 100m;
        var sub2 = ReceiptItem(receipt, line);
        sub2.ExpiryDate = new DateOnly(2027, 2, 1);
        sub2.BatchNumber = "B-2";
        sub2.SourceOrderItemBatchId = Guid.NewGuid();
        sub2.ProductId = productId;
        sub2.QuantityReceived = 20m;
        receipt.Items.Add(sub1);
        receipt.Items.Add(sub2);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (dto, error) = await _sut.ReceiveAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(error);
        Assert.NotNull(dto);

        // One ProductStock per BATCH, not per order line — the whole point of the D4 handoff.
        await _receipts.Received(2).AddStockAsync(Arg.Any<ProductStock>(), Arg.Any<CancellationToken>());
        await _receipts.Received(1).AddStockAsync(
            Arg.Is<ProductStock>(s =>
                s.ProductId == productId
                && s.Quantity == 100m
                && s.BatchNumber == "B-1"
                && s.ExpiryDate == new DateOnly(2026, 12, 1)
                && s.StoreId == _storeId),
            Arg.Any<CancellationToken>());
        await _receipts.Received(1).AddStockAsync(
            Arg.Is<ProductStock>(s =>
                s.Quantity == 20m
                && s.BatchNumber == "B-2"
                && s.ExpiryDate == new DateOnly(2027, 2, 1)),
            Arg.Any<CancellationToken>());
        await _receipts.Received(2).AddMovementAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());

        Assert.Equal("received", receipt.Status);
        Assert.Equal(MarketplaceOrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveredAt);
    }

    // ── GetAsync ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_OrderNotFound_ReturnsError()
    {
        _orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrder?)null);

        var (dto, error) = await _sut.GetAsync(_clientTenantId, Guid.NewGuid());

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.OrderNotFoundError, error);
    }

    [Fact]
    public async Task Get_NoReceiptYet_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrderReceipt?)null);

        var (dto, error) = await _sut.GetAsync(_clientTenantId, order.Id);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.ReceiptNotFoundError, error);
    }

    // ── UpdateItemAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateItem_ForeignClientTenant_ReturnsReceiptNotFound()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        var receipt = Receipt(order, "draft");
        var item = ReceiptItem(receipt);
        receipt.Items.Add(item);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (dto, error) = await _sut.UpdateItemAsync(
            Guid.NewGuid(), order.Id, item.Id,
            new UpdateMarketplaceOrderReceiptItemRequest(null, 1, null, null, null));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.ReceiptNotFoundError, error);
    }

    [Fact]
    public async Task UpdateItem_ReceiptNotDraft_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.Delivered, _storeId);
        var receipt = Receipt(order, "received");
        var item = ReceiptItem(receipt);
        receipt.Items.Add(item);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (dto, error) = await _sut.UpdateItemAsync(
            _clientTenantId, order.Id, item.Id,
            new UpdateMarketplaceOrderReceiptItemRequest(null, 1, null, null, null));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.ReceiptAlreadyReceivedError, error);
    }

    [Fact]
    public async Task UpdateItem_UnknownItemId_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        var receipt = Receipt(order, "draft");
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (dto, error) = await _sut.UpdateItemAsync(
            _clientTenantId, order.Id, Guid.NewGuid(),
            new UpdateMarketplaceOrderReceiptItemRequest(null, 1, null, null, null));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.ReceiptItemNotFoundError, error);
    }

    [Fact]
    public async Task UpdateItem_NegativeQuantity_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        var receipt = Receipt(order, "draft");
        var item = ReceiptItem(receipt);
        receipt.Items.Add(item);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (dto, error) = await _sut.UpdateItemAsync(
            _clientTenantId, order.Id, item.Id,
            new UpdateMarketplaceOrderReceiptItemRequest(null, -1, null, null, null));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.NegativeQuantityError, error);
    }

    [Fact]
    public async Task UpdateItem_UnknownProductId_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        var receipt = Receipt(order, "draft");
        var item = ReceiptItem(receipt);
        receipt.Items.Add(item);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var productId = Guid.NewGuid();
        _items.GetByIdAsync(productId, Arg.Any<CancellationToken>()).Returns((Item?)null);

        var (dto, error) = await _sut.UpdateItemAsync(
            _clientTenantId, order.Id, item.Id,
            new UpdateMarketplaceOrderReceiptItemRequest(productId, 1, null, null, null));

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.ProductNotFoundError, error);
    }

    [Fact]
    public async Task UpdateItem_ValidRequest_SetsFieldsWithMergeSemantics()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        var receipt = Receipt(order, "draft");
        var item = ReceiptItem(receipt);
        item.BatchNumber = "OLD-BATCH";
        receipt.Items.Add(item);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var productId = Guid.NewGuid();
        _items.GetByIdAsync(productId, Arg.Any<CancellationToken>()).Returns(new Item { Id = productId, Name = "Молоко" });

        var expiry = new DateOnly(2026, 12, 31);
        var (dto, error) = await _sut.UpdateItemAsync(
            _clientTenantId, order.Id, item.Id,
            new UpdateMarketplaceOrderReceiptItemRequest(productId, 4.5m, expiry, null, "Пошкоджена упаковка"));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(4.5m, item.QuantityReceived);
        Assert.Equal(expiry, item.ExpiryDate);
        // BatchNumber omitted (null in request) -> merges, keeps the existing value.
        Assert.Equal("OLD-BATCH", item.BatchNumber);
        Assert.Equal("Пошкоджена упаковка", item.DiscrepancyNotes);
        await _receipts.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── ReceiveAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Receive_OrderNotFound_ReturnsError()
    {
        _orders.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((MarketplaceOrder?)null);

        var (dto, error) = await _sut.ReceiveAsync(_clientTenantId, Guid.NewGuid(), _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.OrderNotFoundError, error);
    }

    [Fact]
    public async Task Receive_AlreadyReceived_ReturnsError()
    {
        var order = Order(MarketplaceOrderStatus.Delivered, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        var receipt = Receipt(order, "received");
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (dto, error) = await _sut.ReceiveAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.ReceiptAlreadyReceivedError, error);
    }

    [Fact]
    public async Task Receive_ItemMissingProductOrQuantityOrExpiry_ReturnsGateError()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var receipt = Receipt(order, "draft");
        var resolved = ReceiptItem(receipt);
        resolved.ProductId = Guid.NewGuid();
        resolved.QuantityReceived = 5m;
        resolved.ExpiryDate = new DateOnly(2026, 12, 31);
        var unresolved = ReceiptItem(receipt); // never scanned
        receipt.Items.Add(resolved);
        receipt.Items.Add(unresolved);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (dto, error) = await _sut.ReceiveAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(dto);
        Assert.Equal(MarketplaceOrderReceiptService.ReceiveGateError, error);
        await _receipts.DidNotReceive().AddStockAsync(Arg.Any<ProductStock>(), Arg.Any<CancellationToken>());
        Assert.Equal(MarketplaceOrderStatus.Shipped, order.Status);
    }

    [Fact]
    public async Task Receive_AllItemsResolved_CreatesStockAndMovementAndDeliversOrder()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var receipt = Receipt(order, "draft");
        var item1 = ReceiptItem(receipt);
        item1.ProductId = Guid.NewGuid();
        item1.QuantityReceived = 5m;
        item1.ExpiryDate = new DateOnly(2026, 12, 31);
        var item2 = ReceiptItem(receipt);
        item2.ProductId = Guid.NewGuid();
        item2.QuantityReceived = 3m;
        item2.ExpiryDate = new DateOnly(2026, 11, 30);
        receipt.Items.Add(item1);
        receipt.Items.Add(item2);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (dto, error) = await _sut.ReceiveAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        await _receipts.Received(2).AddStockAsync(Arg.Any<ProductStock>(), Arg.Any<CancellationToken>());
        await _receipts.Received(2).AddMovementAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
        await _receipts.Received(1).AddStockAsync(
            Arg.Is<ProductStock>(s =>
                s.ProductId == item1.ProductId
                && s.StoreId == _storeId
                && s.Quantity == 5m
                && s.QuantityInitial == 5m
                && s.TenantId == _clientTenantId
                && s.SourceType == "marketplace_order_receipt"
                && s.SourceId == receipt.Id
                && s.AddedBy == _userId),
            Arg.Any<CancellationToken>());
        await _receipts.Received(1).AddMovementAsync(
            Arg.Is<StockMovement>(m =>
                m.ProductId == item1.ProductId
                && m.MovementType == "receipt"
                && m.ReferenceType == "marketplace_order_receipt"
                && m.ReferenceId == receipt.Id
                && m.ToStoreId == _storeId
                && m.PerformedBy == _userId),
            Arg.Any<CancellationToken>());

        Assert.Equal("received", receipt.Status);
        Assert.NotNull(receipt.ReceivedAt);
        Assert.Equal(_userId, receipt.ReceivedByUserId);

        Assert.Equal(MarketplaceOrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveredAt);

        await _receipts.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Regression guard (TASK-599, Wave 2): no discrepancy notes on any item -> no ticket,
        // no notification, no tenant-session override, and exactly one SaveChangesAsync call
        // total (asserted above) — the discrepancy side effect must not add a second one.
        await _supplierSupport.DidNotReceive().CreateSystemTicketAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().EnqueueAsync(Arg.Any<NotificationQueue>(), Arg.Any<CancellationToken>());
        await _tenantSessionOverride.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(), Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_WithDiscrepancyNotes_OpensSystemTicketAndEnqueuesNotification()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var receipt = Receipt(order, "draft");
        var okItem = ReceiptItem(receipt);
        okItem.ProductId = Guid.NewGuid();
        okItem.QuantityReceived = 5m;
        okItem.ExpiryDate = new DateOnly(2026, 12, 31);
        var damagedItem = ReceiptItem(receipt);
        damagedItem.ItemNameSnapshot = "Хліб житній";
        damagedItem.ProductId = Guid.NewGuid();
        damagedItem.QuantityReceived = 3m;
        damagedItem.ExpiryDate = new DateOnly(2026, 11, 30);
        damagedItem.DiscrepancyNotes = "Пошкоджена упаковка";
        receipt.Items.Add(okItem);
        receipt.Items.Add(damagedItem);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (dto, error) = await _sut.ReceiveAsync(_clientTenantId, order.Id, _userId);

        Assert.Null(error);
        Assert.NotNull(dto);

        await _tenantSessionOverride.Received(1).ExecuteAsync(
            _supplierTenantId, Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());

        await _supplierSupport.Received(1).CreateSystemTicketAsync(
            _clientTenantId, _supplierTenantId, order.Id,
            Arg.Is<string>(s => s.Contains(order.OrderNumber)),
            Arg.Is<string>(b => b.Contains("Хліб житній") && b.Contains("Пошкоджена упаковка")),
            _userId, Arg.Any<CancellationToken>());

        await _notifications.Received(1).EnqueueAsync(
            Arg.Is<NotificationQueue>(n =>
                n.TenantId == _supplierTenantId
                && n.EventType == "supplier_support_ticket.opened"
                && n.Channel == "system"
                && n.Status == "pending"),
            Arg.Any<CancellationToken>());

        // Finalize itself must still have gone through (2 SaveChanges: the finalize commit, then
        // the discrepancy-ticket commit inside the override).
        await _receipts.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal("received", receipt.Status);
        Assert.Equal(MarketplaceOrderStatus.Delivered, order.Status);
    }

    // ── Receipt-item enrichment: Price / ReferenceImageUrl (TASK-599, Wave 2) ──────

    [Fact]
    public async Task Get_ResolvedItem_UsesItemImageUrlNotSupplierFallback()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var supplierItemId = Guid.NewGuid();
        var orderLine = OrderItem(order, "Молоко 2.5%", 5m);
        orderLine.SupplierItemId = supplierItemId;
        var product = new Item { Id = Guid.NewGuid(), Name = "Молоко 2.5%", ImageUrl = "https://cdn/item.jpg" };

        var receipt = Receipt(order, "draft");
        var item = ReceiptItem(receipt, orderLine, product);
        item.ProductId = product.Id;
        receipt.Items.Add(item);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        // Even if a supplier image happens to be registered, a resolved item must not use it.
        _marketplace.GetSupplierItemImagesByIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<SupplierItemImage>>
            {
                [supplierItemId] = new List<SupplierItemImage>
                {
                    new() { SupplierItemId = supplierItemId, Url = "https://cdn/supplier.jpg", Kind = "main", SortOrder = 0 },
                },
            });

        var (dto, error) = await _sut.GetAsync(_clientTenantId, order.Id);

        Assert.Null(error);
        var itemDto = Assert.Single(dto!.Items);
        Assert.Equal(10m, itemDto.Price); // frozen order-line price from the OrderItem helper
        Assert.Equal("https://cdn/item.jpg", itemDto.ReferenceImageUrl);
    }

    [Fact]
    public async Task Get_UnresolvedItem_FallsBackToSupplierPrimaryImage()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var supplierItemId = Guid.NewGuid();
        var orderLine = OrderItem(order, "Хліб житній", 10m);
        orderLine.SupplierItemId = supplierItemId;

        var receipt = Receipt(order, "draft");
        var item = ReceiptItem(receipt, orderLine); // ProductId still null -> not yet scanned
        receipt.Items.Add(item);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        _marketplace.GetSupplierItemImagesByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.Contains(supplierItemId)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<SupplierItemImage>>
            {
                [supplierItemId] = new List<SupplierItemImage>
                {
                    new() { SupplierItemId = supplierItemId, Url = "https://cdn/gallery.jpg", Kind = "gallery", SortOrder = 1 },
                    new() { SupplierItemId = supplierItemId, Url = "https://cdn/main.jpg", Kind = "main", SortOrder = 0 },
                },
            });

        var (dto, error) = await _sut.GetAsync(_clientTenantId, order.Id);

        Assert.Null(error);
        var itemDto = Assert.Single(dto!.Items);
        Assert.Equal(10m, itemDto.Price);
        Assert.Equal("https://cdn/main.jpg", itemDto.ReferenceImageUrl);
    }

    [Fact]
    public async Task Get_UnresolvedItemWithNoSupplierImage_ReferenceImageUrlIsNullNotThrow()
    {
        var order = Order(MarketplaceOrderStatus.Shipped, _storeId);
        _orders.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var orderLine = OrderItem(order, "Хліб житній", 10m); // no SupplierItemId at all
        var receipt = Receipt(order, "draft");
        var item = ReceiptItem(receipt, orderLine);
        receipt.Items.Add(item);
        _receipts.GetByOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (dto, error) = await _sut.GetAsync(_clientTenantId, order.Id);

        Assert.Null(error);
        var itemDto = Assert.Single(dto!.Items);
        Assert.Equal(10m, itemDto.Price);
        Assert.Null(itemDto.ReferenceImageUrl);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private MarketplaceOrder Order(string status, Guid? destinationStoreId) => new()
    {
        OrderNumber = "MP-2026-001",
        AgreementId = Guid.NewGuid(),
        SupplierTenantId = _supplierTenantId,
        ClientTenantId = _clientTenantId,
        Status = status,
        DestinationStoreId = destinationStoreId,
    };

    private MarketplaceOrderItem OrderItem(MarketplaceOrder order, string name, decimal qty) => new()
    {
        OrderId = order.Id,
        SupplierTenantId = _supplierTenantId,
        ClientTenantId = _clientTenantId,
        ItemName = name,
        Price = 10m,
        Qty = qty,
        LineTotal = 10m * qty,
    };

    private MarketplaceOrderReceipt Receipt(MarketplaceOrder order, string status) => new()
    {
        MarketplaceOrderId = order.Id,
        ClientTenantId = _clientTenantId,
        SupplierTenantId = _supplierTenantId,
        DestinationStoreId = _storeId,
        Status = status,
    };

    private MarketplaceOrderItemBatch Batch(
        MarketplaceOrder order, MarketplaceOrderItem line, DateOnly expiry, decimal qty, string? batchNumber) => new()
    {
        OrderItemId = line.Id,
        OrderId = order.Id,
        SupplierTenantId = _supplierTenantId,
        ClientTenantId = _clientTenantId,
        SupplierStockId = Guid.NewGuid(),
        ExpiryDate = expiry,
        BatchNumber = batchNumber,
        Qty = qty,
    };

    private MarketplaceOrderReceiptItem ReceiptItem(
        MarketplaceOrderReceipt receipt, MarketplaceOrderItem? orderItem = null, Item? product = null) => new()
    {
        ReceiptId = receipt.Id,
        MarketplaceOrderItemId = orderItem?.Id ?? Guid.NewGuid(),
        OrderItem = orderItem,
        Product = product,
        ClientTenantId = _clientTenantId,
        SupplierTenantId = _supplierTenantId,
        ItemNameSnapshot = "Молоко 2.5%",
        QuantityOrdered = 5m,
    };
}
