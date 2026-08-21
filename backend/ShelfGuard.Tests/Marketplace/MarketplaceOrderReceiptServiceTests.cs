using NSubstitute;
using ShelfGuard.Application.Features.Marketplace;
using ShelfGuard.Application.Features.Marketplace.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Marketplace;

/// <summary>
/// TASK-586, ADR-033: client-confirmed marketplace order receiving — draft creation/pre-population,
/// per-item scan/count update, and the finalize gate (ProductId + QuantityReceived + ExpiryDate
/// on every item) that creates ProductStock/StockMovement and sets the order to Delivered.
/// </summary>
public sealed class MarketplaceOrderReceiptServiceTests
{
    private readonly IMarketplaceOrderReceiptRepository _receipts = Substitute.For<IMarketplaceOrderReceiptRepository>();
    private readonly IMarketplaceOrderRepository _orders = Substitute.For<IMarketplaceOrderRepository>();
    private readonly IMarketplaceOrderService _orderService = Substitute.For<IMarketplaceOrderService>();
    private readonly IItemRepository _items = Substitute.For<IItemRepository>();
    private readonly MarketplaceOrderReceiptService _sut;

    private readonly Guid _supplierTenantId = Guid.NewGuid();
    private readonly Guid _clientTenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();

    public MarketplaceOrderReceiptServiceTests()
    {
        _sut = new MarketplaceOrderReceiptService(_receipts, _orders, _orderService, _items);
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

    private MarketplaceOrderReceiptItem ReceiptItem(MarketplaceOrderReceipt receipt) => new()
    {
        ReceiptId = receipt.Id,
        MarketplaceOrderItemId = Guid.NewGuid(),
        ClientTenantId = _clientTenantId,
        SupplierTenantId = _supplierTenantId,
        ItemNameSnapshot = "Молоко 2.5%",
        QuantityOrdered = 5m,
    };
}
