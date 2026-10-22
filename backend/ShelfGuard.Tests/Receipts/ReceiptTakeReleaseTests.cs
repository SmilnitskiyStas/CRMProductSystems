using NSubstitute;
using ShelfGuard.Application.Features.Receipts;
using ShelfGuard.Application.Features.Receipts.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Receipts;

/// <summary>TASK-755: take / release of draft orders and the edit lock.</summary>
public sealed class ReceiptTakeReleaseTests
{
    private readonly IReceiptRepository _repo = Substitute.For<IReceiptRepository>();
    private readonly ReceiptService _sut;
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    public ReceiptTakeReleaseTests() => _sut = new ReceiptService(
        _repo, Substitute.For<INotificationRepository>(), Substitute.For<IActivityLogRepository>());

    private StockReceipt Draft(Guid? takenBy = null, string status = "draft")
    {
        var r = new StockReceipt
        {
            TenantId = Guid.NewGuid(), DestinationStoreId = Guid.NewGuid(), Status = status,
            Source = ReceiptSource.AutoReplenishment, TakenByUserId = takenBy,
            TakenAt = takenBy is null ? null : DateTime.UtcNow,
        };
        r.Items.Add(new StockReceiptItem { ReceiptId = r.Id, ProductId = Guid.NewGuid(), QuantityOrdered = 10m });
        _repo.GetByIdAsync(r.Id, Arg.Any<CancellationToken>()).Returns(r);
        _repo.GetUserNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [_me] = "Іван", [_other] = "Олена" });
        return r;
    }

    [Fact]
    public async Task Take_Untaken_SetsLockViaCompareAndSet()
    {
        var r = Draft();
        _repo.TryTakeAsync(r.Id, _me, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ci => { r.TakenByUserId = _me; r.TakenAt = DateTime.UtcNow; return true; });

        var (dto, error) = await _sut.TakeAsync(r.Id, _me);

        Assert.Null(error);
        Assert.Equal(_me, dto!.TakenByUserId);
        Assert.Equal("Іван", dto.TakenByName);
        Assert.NotNull(dto.TakenAt);
        Assert.Equal("auto_replenishment", dto.Source);
    }

    [Fact]
    public async Task Take_SameUserAgain_IsIdempotent_NoWrite()
    {
        var r = Draft(takenBy: _me);

        var (dto, error) = await _sut.TakeAsync(r.Id, _me);

        Assert.Null(error);
        Assert.Equal(_me, dto!.TakenByUserId);
        await _repo.DidNotReceiveWithAnyArgs().TryTakeAsync(default, default, default);
    }

    [Fact]
    public async Task Take_AlreadyTakenByOther_Conflict()
    {
        var r = Draft(takenBy: _other);

        var (dto, error) = await _sut.TakeAsync(r.Id, _me);

        Assert.Null(dto);
        Assert.Equal(ReceiptErrors.TakenByOther, error);
        await _repo.DidNotReceiveWithAnyArgs().TryTakeAsync(default, default, default);
    }

    [Fact]
    public async Task Take_LostTheRace_Conflict()
    {
        var r = Draft();
        _repo.TryTakeAsync(r.Id, _me, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        var (_, error) = await _sut.TakeAsync(r.Id, _me);

        Assert.Equal(ReceiptErrors.TakenByOther, error);
    }

    [Theory]
    [InlineData("received")]
    [InlineData("cancelled")]
    public async Task Take_NonDraft_Rejected(string status)
    {
        var r = Draft(status: status);

        var (dto, error) = await _sut.TakeAsync(r.Id, _me);

        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.NotEqual(ReceiptErrors.TakenByOther, error);
    }

    [Fact]
    public async Task Take_NotFound()
    {
        var (_, error) = await _sut.TakeAsync(Guid.NewGuid(), _me);
        Assert.Equal(ReceiptErrors.NotFound, error);
    }

    [Fact]
    public async Task Release_ByTaker_ClearsLock()
    {
        var r = Draft(takenBy: _me);

        var (dto, error) = await _sut.ReleaseAsync(r.Id, _me, canOverride: false);

        Assert.Null(error);
        Assert.Null(dto!.TakenByUserId);
        Assert.Null(r.TakenAt);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Release_ByOtherManager_Forbidden_ButAdminOverrideAllowed()
    {
        var r = Draft(takenBy: _other);

        var (_, denied) = await _sut.ReleaseAsync(r.Id, _me, canOverride: false);
        Assert.Equal(ReceiptErrors.ReleaseForbidden, denied);
        Assert.Equal(_other, r.TakenByUserId);

        var (dto, ok) = await _sut.ReleaseAsync(r.Id, _me, canOverride: true);
        Assert.Null(ok);
        Assert.Null(dto!.TakenByUserId);
    }

    [Fact]
    public async Task Release_Untaken_IsIdempotent()
    {
        var r = Draft();
        var (dto, error) = await _sut.ReleaseAsync(r.Id, _me, false);
        Assert.Null(error);
        Assert.Null(dto!.TakenByUserId);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── editing quantities on a draft ───────────────────────────────────────

    [Fact]
    public async Task UpdateItems_TakenByOther_Conflict_NothingChanged()
    {
        var r = Draft(takenBy: _other);
        var item = r.Items.Single();

        var (_, error) = await _sut.UpdateItemsAsync(
            r.Id, new UpdateItemsRequest([new(item.Id, null, null, null, null, null, 25m)]), default, _me);

        Assert.Equal(ReceiptErrors.TakenByOther, error);
        Assert.Equal(10m, item.QuantityOrdered);
    }

    [Fact]
    public async Task UpdateItems_TakenByMe_OrUntaken_CorrectsOrderedQuantity()
    {
        var mine = Draft(takenBy: _me);
        var (_, e1) = await _sut.UpdateItemsAsync(
            mine.Id, new UpdateItemsRequest([new(mine.Items.Single().Id, null, null, null, null, null, 25m)]), default, _me);
        Assert.Null(e1);
        Assert.Equal(25m, mine.Items.Single().QuantityOrdered);

        var untaken = Draft();
        var (_, e2) = await _sut.UpdateItemsAsync(
            untaken.Id, new UpdateItemsRequest([new(untaken.Items.Single().Id, null, null, null, null, null, 7m)]), default, _me);
        Assert.Null(e2);
        Assert.Equal(7m, untaken.Items.Single().QuantityOrdered);
    }

    [Fact]
    public async Task UpdateItems_NonPositiveOrderedQuantity_Rejected()
    {
        var r = Draft(takenBy: _me);
        var (_, error) = await _sut.UpdateItemsAsync(
            r.Id, new UpdateItemsRequest([new(r.Items.Single().Id, null, null, null, null, null, 0m)]), default, _me);
        Assert.NotNull(error);
        Assert.Equal(10m, r.Items.Single().QuantityOrdered);
    }
}
