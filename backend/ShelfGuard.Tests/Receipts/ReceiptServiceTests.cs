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
    private readonly IActivityLogRepository _activityLogs = Substitute.For<IActivityLogRepository>();
    private readonly ReceiptService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public ReceiptServiceTests() => _sut = new ReceiptService(_repo, _activityLogs);

    [Theory]
    [InlineData("stale")]
    [InlineData("invalid")]
    [InlineData("missing_line")]
    [InlineData("duplicate_line")]
    public async Task Reviewed_source_rejects_stale_or_incomplete_edits_before_mutation(string kind)
    {
        var receipt = BuildReceipt(2, new(2027, 1, 1));
        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);
        var fingerprint = ShelfGuard.Application.Features.Erp.Posting.ReceiptPostingRules.Fingerprint(receipt);
        var items = receipt.Items.Select(x => new UpdateItemPayload(x.Id, 2.25m, 12.50m, x.ExpiryDate, "reviewed", null)).ToList();
        if (kind == "stale") receipt.Notes = "Changed by another operator";
        if (kind == "invalid") fingerprint = new string('z', 64);
        if (kind == "missing_line") items.RemoveAt(1);
        if (kind == "duplicate_line") items[1] = items[0];
        var (dto, error) = await _sut.UpdateItemsAsync(receipt.Id, new(items, fingerprint));
        Assert.Null(dto); Assert.Equal(ReceiptErrors.SourceChanged, error);
        Assert.All(receipt.Items, x => Assert.Null(x.QuantityReceived));
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reviewed_source_save_returns_the_new_fingerprint_for_posting_review()
    {
        var receipt = BuildReceipt(1, new(2027, 1, 1));
        _repo.GetByIdAsync(receipt.Id, Arg.Any<CancellationToken>()).Returns(receipt);
        var before = ShelfGuard.Application.Features.Erp.Posting.ReceiptPostingRules.Fingerprint(receipt);
        var (dto, error) = await _sut.UpdateItemsAsync(receipt.Id,
            new([new(receipt.Items.First().Id, 2.25m, 12.50m, new(2027,1,1), "reviewed", "Counted")], before));
        Assert.Null(error); Assert.NotNull(dto); Assert.NotEqual(before, dto.ReceivingFingerprint);
        Assert.Equal(2.25m, dto.Items[0].QuantityReceived);
        Assert.Equal(ShelfGuard.Application.Features.Erp.Posting.ReceiptPostingRules.Fingerprint(receipt), dto.ReceivingFingerprint);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("2.251")]
    [InlineData("100000000")]
    public async Task Draft_counts_are_not_silently_rounded_before_ERP_review(string raw)
    {
        var quantity=decimal.Parse(raw,System.Globalization.CultureInfo.InvariantCulture);
        var (_,error)=await _sut.CreateAsync(_tenantId,_userId,new(null,_storeId,false,null,null,[new(_productId,quantity,null,new(2027,1,1),null)]));
        Assert.NotNull(error);await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
    [Theory]
    [InlineData("2.251")]
    [InlineData("100000000")]
    public async Task Received_counts_are_not_silently_rounded_before_ERP_review(string raw)
    {
        var r=BuildReceipt(1,new(2027,1,1));_repo.GetByIdAsync(r.Id,Arg.Any<CancellationToken>()).Returns(r);
        var quantity=decimal.Parse(raw,System.Globalization.CultureInfo.InvariantCulture);
        var (_,error)=await _sut.UpdateItemsAsync(r.Id,new([new(r.Items.First().Id,quantity,null,null,null,null)]));
        Assert.NotNull(error);await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

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
        await _repo.Received(1).AddNotificationAsync(
            Arg.Is<NotificationQueue>(n => n.TenantId == _tenantId && n.StoreId == _storeId
                && n.EventType == "receipt.created" && n.Channel == "system" && n.Status == "pending"),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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

    // ── Postpone ───────────────────────────────────────────────────────────

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private StockReceipt OpenReceipt(string status = "ordered")
    {
        var r = BuildReceipt(1);
        r.Status = status;
        r.ExpectedAt = Today.AddDays(3);
        _repo.GetByIdAsync(r.Id, Arg.Any<CancellationToken>()).Returns(r);
        return r;
    }

    [Fact]
    public async Task PostponeAsync_PastDate_ReturnsError()
    {
        var r = OpenReceipt();
        var (dto, error) = await _sut.PostponeAsync(r.Id, _userId, new PostponeReceiptRequest(Today.AddDays(-1), null));
        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.Equal(0, r.PostponeCount);
    }

    [Fact]
    public async Task PostponeAsync_MoreThan365Days_ReturnsError()
    {
        var r = OpenReceipt();
        var (dto, error) = await _sut.PostponeAsync(r.Id, _userId, new PostponeReceiptRequest(Today.AddDays(366), null));
        Assert.Null(dto);
        Assert.Contains("too far", error);
        Assert.Equal(0, r.PostponeCount);
    }

    [Theory]
    [InlineData("received")]
    [InlineData("cancelled")]
    public async Task PostponeAsync_NonOpenStatus_ReturnsError(string status)
    {
        var r = OpenReceipt(status);
        var (dto, error) = await _sut.PostponeAsync(r.Id, _userId, new PostponeReceiptRequest(Today.AddDays(10), null));
        Assert.Null(dto);
        Assert.Contains("open order", error);
    }

    [Fact]
    public async Task PostponeAsync_ReasonTooLong_ReturnsError()
    {
        var r = OpenReceipt();
        var (dto, error) = await _sut.PostponeAsync(r.Id, _userId,
            new PostponeReceiptRequest(Today.AddDays(10), new string('x', 501)));
        Assert.Null(dto);
        Assert.Contains("too long", error);
    }

    [Fact]
    public async Task PostponeAsync_Valid_SetsFieldsAndDto()
    {
        var r = OpenReceipt();
        var original = r.ExpectedAt;
        var newDate = Today.AddDays(10);

        var (dto, error) = await _sut.PostponeAsync(r.Id, _userId, new PostponeReceiptRequest(newDate, "  late  "));

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal(1, r.PostponeCount);
        Assert.Equal(original, r.OriginalExpectedAt);
        Assert.Equal(newDate, r.ExpectedAt);
        Assert.Equal("late", r.DelayReason);
        Assert.Equal(1, dto!.PostponeCount);
        Assert.Equal(original, dto.OriginalExpectedAt);
        Assert.Equal(newDate, dto.ExpectedAt);
        Assert.Equal("late", dto.DelayReason);
        Assert.NotNull(dto.PostponedAt);
        await _activityLogs.Received(1).LogAsync(
            Arg.Is<ActivityLog>(a => a.TenantId == _tenantId && a.Action == "receipt.postponed"),
            Arg.Any<CancellationToken>());
    }

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
