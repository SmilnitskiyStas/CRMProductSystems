using NSubstitute;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Receipts.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Receipts;

public sealed class ReceiptServiceTests
{
    private readonly IReceiptRepository _repo = Substitute.For<IReceiptRepository>();
    private readonly ReceiptService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public ReceiptServiceTests() => _sut = new ReceiptService(_repo);

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_EmptyItems_ReturnsError()
    {
        var req = new CreateReceiptRequest(null, _storeId, false, null, null, []);
        var (receipt, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(receipt);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_ItemWithZeroQuantity_ReturnsError()
    {
        var req = new CreateReceiptRequest(null, _storeId, false, null, null, [
            new(_productId, 0, null, null, null)
        ]);
        var (receipt, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(receipt);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_AddsReceiptWithItems()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var req = new CreateReceiptRequest(null, _storeId, false, null, null, [
            new(_productId, 50, 20m, expiry, "B001")
        ]);

        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(ci => BuildReceipt(1, expiry));

        var (receipt, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(error);
        await _repo.Received(1).AddAsync(
            Arg.Is<StockReceipt>(r =>
                r.TenantId == _tenantId &&
                r.DestinationStoreId == _storeId &&
                r.Items.Count == 1),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── UpdateItems ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateItemsAsync_ReceiptNotFound_ReturnsError()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns((StockReceipt?)null);

        var (receipt, error) = await _sut.UpdateItemsAsync(Guid.NewGuid(),
            new UpdateItemsRequest([]));

        Assert.Null(receipt);
        Assert.Equal("Receipt not found.", error);
    }

    [Fact]
    public async Task UpdateItemsAsync_ReceivedReceipt_ReturnsError()
    {
        var receipt = BuildReceipt(1);
        receipt.Status = "received";
        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (result, error) = await _sut.UpdateItemsAsync(receipt.Id,
            new UpdateItemsRequest([]));

        Assert.Null(result);
        Assert.Contains("received", error);
    }

    [Fact]
    public async Task UpdateItemsAsync_UnknownItemId_ReturnsError()
    {
        var receipt = BuildReceipt(1);
        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (result, error) = await _sut.UpdateItemsAsync(receipt.Id,
            new UpdateItemsRequest([new(Guid.NewGuid(), 10, null, null, null, null)]));

        Assert.Null(result);
        Assert.Contains("not found", error);
    }

    [Fact]
    public async Task UpdateItemsAsync_Valid_UpdatesItemsAndSaves()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20));
        var receipt = BuildReceipt(2, expiry);
        var firstItem = receipt.Items.First();

        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);
        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var payload = new UpdateItemPayload(firstItem.Id, 40, 22m, expiry, "B-NEW", null);
        var (result, error) = await _sut.UpdateItemsAsync(receipt.Id,
            new UpdateItemsRequest([payload]));

        Assert.Null(error);
        Assert.Equal(40, firstItem.QuantityReceived);
        Assert.Equal("B-NEW", firstItem.BatchNumber);
        _repo.Received(1).UpdateItem(firstItem);
    }

    // ── Receive ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReceiveAsync_NotFound_ReturnsError()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns((StockReceipt?)null);

        var (receipt, error) = await _sut.ReceiveAsync(Guid.NewGuid(), _userId);
        Assert.Null(receipt);
        Assert.Equal("Receipt not found.", error);
    }

    [Fact]
    public async Task ReceiveAsync_AlreadyReceived_ReturnsError()
    {
        var receipt = BuildReceipt(1, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)));
        receipt.Status = "received";
        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (result, error) = await _sut.ReceiveAsync(receipt.Id, _userId);
        Assert.Null(result);
        Assert.Contains("already received", error);
    }

    [Fact]
    public async Task ReceiveAsync_ItemMissingExpiryDate_ReturnsError()
    {
        var receipt = BuildReceipt(1, expiryDate: null); // no expiry
        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (result, error) = await _sut.ReceiveAsync(receipt.Id, _userId);
        Assert.Null(result);
        Assert.Contains("ExpiryDate", error);
    }

    [Fact]
    public async Task ReceiveAsync_ValidReceipt_CreatesStockAndMovements()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(15));
        var receipt = BuildReceipt(2, expiry);

        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (result, error) = await _sut.ReceiveAsync(receipt.Id, _userId);

        Assert.Null(error);
        Assert.Equal("received", receipt.Status);
        Assert.NotNull(receipt.ReceivedAt);
        Assert.Equal(_userId, receipt.ReceivedBy);

        // 2 items → 2 stock entries + 2 movements
        await _repo.Received(2).AddStockAsync(
            Arg.Is<ProductStock>(s => s.TenantId == _tenantId && s.ExpiryDate == expiry),
            Arg.Any<CancellationToken>());
        await _repo.Received(2).AddMovementAsync(
            Arg.Is<StockMovement>(m => m.MovementType == "receipt"),
            Arg.Any<CancellationToken>());
    }

    // ── Cancel ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_AlreadyCancelled_ReturnsError()
    {
        var receipt = BuildReceipt(1, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)));
        receipt.Status = "cancelled";
        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (result, error) = await _sut.CancelAsync(receipt.Id);
        Assert.Null(result);
        Assert.Contains("already cancelled", error);
    }

    [Fact]
    public async Task CancelAsync_ReceivedReceipt_ReturnsError()
    {
        var receipt = BuildReceipt(1, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)));
        receipt.Status = "received";
        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        var (result, error) = await _sut.CancelAsync(receipt.Id);
        Assert.Null(result);
        Assert.Contains("Cannot cancel", error);
    }

    [Fact]
    public async Task CancelAsync_DraftReceipt_SetsCancelled()
    {
        var receipt = BuildReceipt(1, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)));
        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);

        await _sut.CancelAsync(receipt.Id);

        Assert.Equal("cancelled", receipt.Status);
        _repo.Received(1).Update(receipt);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private StockReceipt BuildReceipt(int itemCount, DateOnly? expiryDate = null)
    {
        var receipt = new StockReceipt
        {
            TenantId = _tenantId,
            DestinationStoreId = _storeId,
            Status = "draft",
            CreatedBy = _userId,
        };

        for (var i = 0; i < itemCount; i++)
        {
            receipt.Items.Add(new StockReceiptItem
            {
                ReceiptId = receipt.Id,
                ProductId = _productId,
                QuantityOrdered = 50,
                ExpiryDate = expiryDate,
            });
        }

        return receipt;
    }
}
