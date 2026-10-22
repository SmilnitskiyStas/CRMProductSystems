using NSubstitute;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Receipts.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Receipts;

/// <summary>TASK-757: draft header / line editing, send ("ordered"), receive/cancel from ordered.</summary>
public sealed class ReceiptSendEditTests
{
    private readonly IReceiptRepository _repo = Substitute.For<IReceiptRepository>();
    private readonly ReceiptService _sut;
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    public ReceiptSendEditTests() => _sut = new ReceiptService(
        _repo, Substitute.For<INotificationRepository>(), Substitute.For<IActivityLogRepository>());

    private StockReceipt Make(string status = "draft", string source = ReceiptSource.AutoReplenishment,
        Guid? takenBy = null, Guid? supplier = null, int lines = 2)
    {
        var r = new StockReceipt
        {
            TenantId = Guid.NewGuid(), DestinationStoreId = Guid.NewGuid(), Status = status,
            Source = source, TakenByUserId = takenBy, SupplierId = supplier,
        };
        for (var i = 0; i < lines; i++)
            r.Items.Add(new StockReceiptItem
            {
                ReceiptId = r.Id, ProductId = Guid.NewGuid(), QuantityOrdered = 5m, ExpiryDate = new DateOnly(2027, 1, 1),
            });
        _repo.GetByIdAsync(r.Id, Arg.Any<CancellationToken>()).Returns(r);
        _repo.GetUserNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [_me] = "Іван", [_other] = "Олена" });
        return r;
    }

    // ── header ──
    [Fact]
    public async Task Header_SetsSupplierNotesExpected()
    {
        var r = Make();
        var sup = Guid.NewGuid();
        _repo.SupplierExistsAsync(sup, Arg.Any<CancellationToken>()).Returns(true);

        var (dto, err) = await _sut.UpdateHeaderAsync(
            r.Id, new UpdateReceiptHeaderRequest(sup, " нотатка ", new DateOnly(2026, 12, 1)), _me);

        Assert.Null(err);
        Assert.Equal(sup, dto!.SupplierId);
        Assert.Equal("нотатка", dto.Notes);
        Assert.Equal(new DateOnly(2026, 12, 1), dto.ExpectedAt);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Header_UnknownSupplier_Error()
    {
        var r = Make();
        var (_, err) = await _sut.UpdateHeaderAsync(r.Id, new UpdateReceiptHeaderRequest(Guid.NewGuid(), null, null), _me);
        Assert.Equal(ReceiptErrors.SupplierNotFound, err);
    }

    [Fact]
    public async Task Header_TakenByOther_Conflict()
    {
        var r = Make(takenBy: _other);
        var (_, err) = await _sut.UpdateHeaderAsync(r.Id, new UpdateReceiptHeaderRequest(null, "x", null), _me);
        Assert.Equal(ReceiptErrors.TakenByOther, err);
    }

    [Fact]
    public async Task Header_ManualDraft_NotSubjectToTakenGuard()
    {
        var r = Make(source: ReceiptSource.Manual, takenBy: _other);
        var (_, err) = await _sut.UpdateHeaderAsync(r.Id, new UpdateReceiptHeaderRequest(null, "x", null), _me);
        Assert.Null(err);
    }

    [Fact]
    public async Task Header_NonDraft_Error()
    {
        var r = Make(status: "ordered");
        var (_, err) = await _sut.UpdateHeaderAsync(r.Id, new UpdateReceiptHeaderRequest(null, "x", null), _me);
        Assert.Equal(ReceiptErrors.NotDraft, err);
    }

    // ── items ──
    [Fact]
    public async Task AddItem_NewProduct_UsesItemPriceByDefault()
    {
        var r = Make(takenBy: _me);
        var pid = Guid.NewGuid();
        _repo.GetItemPurchaseInfoAsync(pid, Arg.Any<CancellationToken>()).Returns((true, (decimal?)12.5m));

        var (_, err) = await _sut.AddItemAsync(r.Id, new AddReceiptItemRequest(pid, 3m, null), _me);

        Assert.Null(err);
        await _repo.Received(1).AddItemAsync(
            Arg.Is<StockReceiptItem>(i => i.ProductId == pid && i.QuantityOrdered == 3m && i.PricePurchase == 12.5m && i.ReceiptId == r.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddItem_ExistingProduct_IncreasesQuantity_NoDuplicate()
    {
        var r = Make();
        var line = r.Items.First();

        var (_, err) = await _sut.AddItemAsync(r.Id, new AddReceiptItemRequest(line.ProductId, 4m, null), _me);

        Assert.Null(err);
        Assert.Equal(9m, line.QuantityOrdered);
        await _repo.DidNotReceiveWithAnyArgs().AddItemAsync(default!, default);
    }

    [Fact]
    public async Task AddItem_UnknownProduct_Error()
    {
        var r = Make();
        _repo.GetItemPurchaseInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((false, (decimal?)null));
        var (_, err) = await _sut.AddItemAsync(r.Id, new AddReceiptItemRequest(Guid.NewGuid(), 1m, null), _me);
        Assert.Equal(ReceiptErrors.ProductNotFound, err);
    }

    [Fact]
    public async Task AddItem_ZeroQuantity_Error()
    {
        var r = Make();
        var (_, err) = await _sut.AddItemAsync(r.Id, new AddReceiptItemRequest(Guid.NewGuid(), 0m, null), _me);
        Assert.Equal(ReceiptErrors.BadQuantity, err);
    }

    [Fact]
    public async Task AddItem_TakenByOther_Conflict()
    {
        var r = Make(takenBy: _other);
        var (_, err) = await _sut.AddItemAsync(r.Id, new AddReceiptItemRequest(Guid.NewGuid(), 1m, null), _me);
        Assert.Equal(ReceiptErrors.TakenByOther, err);
    }

    [Fact]
    public async Task RemoveItem_Removes()
    {
        var r = Make();
        var line = r.Items.First();
        var (_, err) = await _sut.RemoveItemAsync(r.Id, line.Id, _me);
        Assert.Null(err);
        _repo.Received(1).RemoveItems(Arg.Is<IEnumerable<StockReceiptItem>>(x => x.Single() == line));
    }

    [Fact]
    public async Task RemoveItem_LastLine_Error()
    {
        var r = Make(lines: 1);
        var (_, err) = await _sut.RemoveItemAsync(r.Id, r.Items.First().Id, _me);
        Assert.Equal(ReceiptErrors.LastLine, err);
        _repo.DidNotReceiveWithAnyArgs().RemoveItems(default!);
    }

    [Fact]
    public async Task RemoveItem_NonDraft_Error()
    {
        var r = Make(status: "ordered");
        var (_, err) = await _sut.RemoveItemAsync(r.Id, r.Items.First().Id, _me);
        Assert.Equal(ReceiptErrors.NotDraft, err);
    }

    // ── send ──
    [Fact]
    public async Task Send_TakenByMe_SetsOrdered()
    {
        var r = Make(takenBy: _me, supplier: Guid.NewGuid());

        var (dto, err) = await _sut.SendAsync(r.Id, _me);

        Assert.Null(err);
        Assert.Equal("ordered", dto!.Status);
        Assert.NotNull(dto.OrderedAt);
        Assert.Equal("Іван", dto.OrderedByName);
        Assert.Equal(_me, r.OrderedByUserId);
    }

    [Fact]
    public async Task Send_UntakenAuto_AutoTakes()
    {
        var r = Make(supplier: Guid.NewGuid());
        _repo.TryTakeAsync(r.Id, _me, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ci => { r.TakenByUserId = _me; return true; });

        var (dto, err) = await _sut.SendAsync(r.Id, _me);

        Assert.Null(err);
        Assert.Equal("ordered", dto!.Status);
        await _repo.Received(1).TryTakeAsync(r.Id, _me, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_AutoTakeLostRace_Conflict()
    {
        var r = Make(supplier: Guid.NewGuid());
        _repo.TryTakeAsync(r.Id, _me, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);
        var (_, err) = await _sut.SendAsync(r.Id, _me);
        Assert.Equal(ReceiptErrors.TakenByOther, err);
        Assert.Equal("draft", r.Status);
    }

    [Fact]
    public async Task Send_TakenByOther_Conflict()
    {
        var r = Make(takenBy: _other, supplier: Guid.NewGuid());
        var (_, err) = await _sut.SendAsync(r.Id, _me);
        Assert.Equal(ReceiptErrors.TakenByOther, err);
    }

    [Fact]
    public async Task Send_NoSupplier_Error()
    {
        var r = Make(takenBy: _me);
        var (_, err) = await _sut.SendAsync(r.Id, _me);
        Assert.Equal(ReceiptErrors.SupplierRequired, err);
        Assert.Equal("draft", r.Status);
    }

    [Fact]
    public async Task Send_ManualDraft_NoTakeNeeded()
    {
        var r = Make(source: ReceiptSource.Manual, supplier: Guid.NewGuid());
        var (dto, err) = await _sut.SendAsync(r.Id, _me);
        Assert.Null(err);
        Assert.Equal("ordered", dto!.Status);
        await _repo.DidNotReceiveWithAnyArgs().TryTakeAsync(default, default, default);
    }

    [Fact]
    public async Task Send_NoLines_Error()
    {
        var r = Make(takenBy: _me, supplier: Guid.NewGuid(), lines: 0);
        var (_, err) = await _sut.SendAsync(r.Id, _me);
        Assert.Equal(ReceiptErrors.NoLines, err);
    }

    [Fact]
    public async Task Send_ZeroQuantityLine_Error()
    {
        var r = Make(takenBy: _me, supplier: Guid.NewGuid());
        r.Items.First().QuantityOrdered = 0m;
        var (_, err) = await _sut.SendAsync(r.Id, _me);
        Assert.Equal(ReceiptErrors.BadQuantity, err);
    }

    [Fact]
    public async Task Send_AlreadyOrdered_Error()
    {
        var r = Make(status: "ordered", takenBy: _me, supplier: Guid.NewGuid());
        var (_, err) = await _sut.SendAsync(r.Id, _me);
        Assert.Equal(ReceiptErrors.NotDraft, err);
    }

    // ── ordered is first-class ──
    [Fact]
    public async Task Receive_FromOrdered_Works()
    {
        var r = Make(status: "ordered", takenBy: _me, supplier: Guid.NewGuid());
        var (dto, err) = await _sut.ReceiveAsync(r.Id, _me);
        Assert.Null(err);
        Assert.Equal("received", dto!.Status);
    }

    [Fact]
    public async Task Cancel_FromOrdered_Works()
    {
        var r = Make(status: "ordered");
        var (dto, err) = await _sut.CancelAsync(r.Id);
        Assert.Null(err);
        Assert.Equal("cancelled", dto!.Status);
    }

    [Fact]
    public async Task UpdateItems_OnOrdered_NotBlockedByTakenGuard_ButQuantityOrderedRejected()
    {
        var r = Make(status: "ordered", takenBy: _other);
        var line = r.Items.First();

        var (_, err) = await _sut.UpdateItemsAsync(r.Id,
            new UpdateItemsRequest([new UpdateItemPayload(line.Id, 5m, null, null, null, null)]), default, _me);
        Assert.Null(err);

        var (_, err2) = await _sut.UpdateItemsAsync(r.Id,
            new UpdateItemsRequest([new UpdateItemPayload(line.Id, null, null, null, null, null, 9m)]), default, _me);
        Assert.NotNull(err2);
    }
}
