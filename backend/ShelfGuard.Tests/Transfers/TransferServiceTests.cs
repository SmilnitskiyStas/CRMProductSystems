using NSubstitute;
using ShelfGuard.Application.Features.Transfers;
using ShelfGuard.Application.Features.Transfers.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Transfers;

public sealed class TransferServiceTests
{
    private readonly ITransferRepository _repo = Substitute.For<ITransferRepository>();
    private readonly TransferService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _fromStore = Guid.NewGuid();
    private readonly Guid _toStore = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public TransferServiceTests() => _sut = new TransferService(_repo);

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_EmptyItems_ReturnsError()
    {
        var req = new CreateTransferRequest(_fromStore, _toStore, "store_to_store", null, []);
        var (t, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(t);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_SameSourceAndDest_ReturnsError()
    {
        var req = new CreateTransferRequest(_fromStore, _fromStore, "store_to_store", null, [
            new(Guid.NewGuid(), 10)
        ]);
        var (t, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(t);
        Assert.Contains("different", error);
    }

    [Fact]
    public async Task CreateAsync_InvalidTransferType_ReturnsError()
    {
        var req = new CreateTransferRequest(_fromStore, _toStore, "invalid_type", null, [
            new(Guid.NewGuid(), 10)
        ]);
        var (t, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(t);
        Assert.Contains("transfer type", error);
    }

    [Fact]
    public async Task CreateAsync_NullTransferType_IsValid()
    {
        var stock = BuildStock(quantity: 50);
        _repo.GetStockByIdAsync(stock.Id, Arg.Any<CancellationToken>()).Returns(stock);
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(ci => BuildTransfer(stock));

        var req = new CreateTransferRequest(_fromStore, _toStore, null, null, [
            new(stock.Id, 10)
        ]);
        var (t, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(error);
    }

    [Fact]
    public async Task CreateAsync_StockNotFound_ReturnsError()
    {
        _repo.GetStockByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns((ProductStock?)null);

        var req = new CreateTransferRequest(_fromStore, _toStore, "store_to_store", null, [
            new(Guid.NewGuid(), 10)
        ]);
        var (t, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(t);
        Assert.Contains("not found", error);
    }

    [Fact]
    public async Task CreateAsync_StockBelongsToDifferentStore_ReturnsError()
    {
        var stock = BuildStock(quantity: 50, storeId: Guid.NewGuid()); // wrong store
        _repo.GetStockByIdAsync(stock.Id, Arg.Any<CancellationToken>()).Returns(stock);

        var req = new CreateTransferRequest(_fromStore, _toStore, "store_to_store", null, [
            new(stock.Id, 10)
        ]);
        var (t, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(t);
        Assert.Contains("does not belong", error);
    }

    [Fact]
    public async Task CreateAsync_InsufficientStock_ReturnsError()
    {
        var stock = BuildStock(quantity: 5);
        _repo.GetStockByIdAsync(stock.Id, Arg.Any<CancellationToken>()).Returns(stock);

        var req = new CreateTransferRequest(_fromStore, _toStore, "store_to_store", null, [
            new(stock.Id, 10)  // requesting more than available
        ]);
        var (t, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(t);
        Assert.Contains("Insufficient", error);
    }

    [Fact]
    public async Task CreateAsync_Valid_DeductsSourceStockAndLogsMovement()
    {
        var stock = BuildStock(quantity: 50);
        _repo.GetStockByIdAsync(stock.Id, Arg.Any<CancellationToken>()).Returns(stock);
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(ci => BuildTransfer(stock));

        var req = new CreateTransferRequest(_fromStore, _toStore, "store_to_store", null, [
            new(stock.Id, 20)
        ]);
        var (t, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(error);
        Assert.Equal(30, stock.Quantity);   // 50 - 20
        _repo.Received(1).UpdateStock(stock);
        await _repo.Received(1).AddMovementAsync(
            Arg.Is<StockMovement>(m => m.MovementType == "transfer" && m.Quantity == 20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ExpiryAndBatchCopiedFromSource()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var stock = BuildStock(quantity: 50, expiryDate: expiry, batchNumber: "ORIG-001");
        _repo.GetStockByIdAsync(stock.Id, Arg.Any<CancellationToken>()).Returns(stock);
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(ci => BuildTransfer(stock, expiryDate: expiry, batchNumber: "ORIG-001"));

        var req = new CreateTransferRequest(_fromStore, _toStore, null, null, [new(stock.Id, 10)]);
        var (transfer, _) = await _sut.CreateAsync(_tenantId, _userId, req);

        // Verify items carry original expiry/batch
        await _repo.Received(1).AddAsync(
            Arg.Is<StockTransfer>(tr =>
                tr.Items.Any(i => i.ExpiryDate == expiry && i.BatchNumber == "ORIG-001")),
            Arg.Any<CancellationToken>());
    }

    // ── Confirm ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmAsync_NotFound_ReturnsError()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns((StockTransfer?)null);

        var (t, error) = await _sut.ConfirmAsync(Guid.NewGuid(), _userId);
        Assert.Null(t);
        Assert.Equal("Transfer not found.", error);
    }

    [Fact]
    public async Task ConfirmAsync_AlreadyReceived_ReturnsError()
    {
        var transfer = BuildTransfer();
        transfer.Status = "received";
        _repo.GetByIdAsync(transfer.Id, Arg.Any<CancellationToken>()).Returns(transfer);

        var (t, error) = await _sut.ConfirmAsync(transfer.Id, _userId);
        Assert.Null(t);
        Assert.Contains("already received", error);
    }

    [Fact]
    public async Task ConfirmAsync_Valid_CreatesDestinationStockAndMovements()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(8));
        var transfer = BuildTransfer(expiryDate: expiry, batchNumber: "B001");
        _repo.GetByIdAsync(transfer.Id, Arg.Any<CancellationToken>()).Returns(transfer);

        var (t, error) = await _sut.ConfirmAsync(transfer.Id, _userId);

        Assert.Null(error);
        Assert.Equal("received", transfer.Status);
        Assert.Equal(_userId, transfer.ConfirmedBy);

        await _repo.Received(1).AddStockAsync(
            Arg.Is<ProductStock>(s =>
                s.StoreId == _toStore &&
                s.ExpiryDate == expiry &&
                s.BatchNumber == "B001" &&
                s.SourceType == "transfer"),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).AddMovementAsync(
            Arg.Is<StockMovement>(m => m.MovementType == "transfer" && m.QuantityBefore == 0),
            Arg.Any<CancellationToken>());
    }

    // ── Cancel ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_ReceivedTransfer_ReturnsError()
    {
        var transfer = BuildTransfer();
        transfer.Status = "received";
        _repo.GetByIdAsync(transfer.Id, Arg.Any<CancellationToken>()).Returns(transfer);

        var (t, error) = await _sut.CancelAsync(transfer.Id);
        Assert.Null(t);
        Assert.Contains("Cannot cancel", error);
    }

    [Fact]
    public async Task CancelAsync_InTransit_RestoresSourceStock()
    {
        var stock = BuildStock(quantity: 30); // already deducted by transfer
        var transfer = BuildTransfer(stock: stock);  // item references stock.Id

        _repo.GetByIdAsync(transfer.Id, Arg.Any<CancellationToken>()).Returns(transfer);
        _repo.GetStockByIdAsync(stock.Id, Arg.Any<CancellationToken>()).Returns(stock);

        var itemQty = transfer.Items.First().Quantity;
        await _sut.CancelAsync(transfer.Id);

        Assert.Equal(30 + itemQty, stock.Quantity);
        _repo.Received(1).UpdateStock(stock);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private ProductStock BuildStock(
        decimal quantity = 50,
        Guid? storeId = null,
        DateOnly? expiryDate = null,
        string? batchNumber = null) => new()
    {
        TenantId = _tenantId,
        ProductId = _productId,
        StoreId = storeId ?? _fromStore,
        Quantity = quantity,
        QuantityInitial = quantity,
        ExpiryDate = expiryDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
        BatchNumber = batchNumber,
        Status = "safe",
        LastCheckedAt = DateTime.UtcNow,
    };

    private StockTransfer BuildTransfer(
        ProductStock? stock = null,
        DateOnly? expiryDate = null,
        string? batchNumber = null)
    {
        var transfer = new StockTransfer
        {
            TenantId = _tenantId,
            FromStoreId = _fromStore,
            ToStoreId = _toStore,
            Status = "in_transit",
            InitiatedBy = _userId,
        };

        var effectiveExpiry = expiryDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var stockId = stock?.Id ?? Guid.NewGuid();

        transfer.Items.Add(new StockTransferItem
        {
            TransferId = transfer.Id,
            ProductStockId = stockId,
            ProductId = _productId,
            Quantity = 20,
            ExpiryDate = effectiveExpiry,
            BatchNumber = batchNumber,
        });

        return transfer;
    }
}
