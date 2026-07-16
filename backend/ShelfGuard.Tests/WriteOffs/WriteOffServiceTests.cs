using NSubstitute;
using ShelfGuard.Application.Features.WriteOffs;
using ShelfGuard.Application.Features.WriteOffs.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.WriteOffs;

public sealed class WriteOffServiceTests
{
    private readonly IWriteOffRepository _repo = Substitute.For<IWriteOffRepository>();
    private readonly WriteOffService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public WriteOffServiceTests() => _sut = new WriteOffService(_repo);

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_EmptyItems_ReturnsError()
    {
        var req = new CreateWriteOffRequest(_storeId, "expired", null, []);
        var (wo, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(wo);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_InvalidReason_ReturnsError()
    {
        var req = new CreateWriteOffRequest(_storeId, "bad_reason", null, [
            new(null, _productId, 5, null)
        ]);
        var (wo, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(wo);
        Assert.Contains("reason", error);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("damaged")]
    [InlineData("theft")]
    [InlineData("production_loss")]
    [InlineData("other")]
    public async Task CreateAsync_AllValidReasons_Succeed(string reason)
    {
        var req = new CreateWriteOffRequest(_storeId, reason, null, [
            new(null, _productId, 5, 10m)
        ]);
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(ci => BuildWriteOff(reason: reason));

        var (wo, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(error);
    }

    [Fact]
    public async Task CreateAsync_NullReason_IsValid()
    {
        var req = new CreateWriteOffRequest(_storeId, null, null, [
            new(null, _productId, 5, null)
        ]);
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(ci => BuildWriteOff());

        var (wo, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(error);
    }

    [Fact]
    public async Task CreateAsync_ItemWithZeroQty_ReturnsError()
    {
        var req = new CreateWriteOffRequest(_storeId, null, null, [
            new(null, _productId, 0, null)
        ]);
        var (wo, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(wo);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_CalculatesTotalLoss()
    {
        var stockId = Guid.NewGuid();
        var req = new CreateWriteOffRequest(_storeId, "expired", null, [
            new(stockId, _productId, 10, 25m),  // loss = 250
            new(null,    _productId,  5, 20m),  // loss = 100
        ]);
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(ci => BuildWriteOff(totalLoss: 350m));

        var (wo, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(error);
        await _repo.Received(1).AddAsync(
            Arg.Is<WriteOff>(w => w.TotalLossAmount == 350m && w.Status == "pending_approval"),
            Arg.Any<CancellationToken>());
    }

    // ── Approve ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_NotFound_ReturnsError()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns((WriteOff?)null);

        var (wo, error) = await _sut.ApproveAsync(Guid.NewGuid(), _userId);
        Assert.Null(wo);
        Assert.Equal("Write-off not found.", error);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyApproved_ReturnsError()
    {
        var writeOff = BuildWriteOff();
        writeOff.Status = "approved";
        _repo.GetByIdAsync(writeOff.Id, Arg.Any<CancellationToken>()).Returns(writeOff);

        var (wo, error) = await _sut.ApproveAsync(writeOff.Id, _userId);
        Assert.Null(wo);
        Assert.Contains("already approved", error);
    }

    [Fact]
    public async Task ApproveAsync_Rejected_ReturnsError()
    {
        var writeOff = BuildWriteOff();
        writeOff.Status = "rejected";
        _repo.GetByIdAsync(writeOff.Id, Arg.Any<CancellationToken>()).Returns(writeOff);

        var (wo, error) = await _sut.ApproveAsync(writeOff.Id, _userId);
        Assert.Null(wo);
        Assert.Contains("rejected", error);
    }

    [Fact]
    public async Task ApproveAsync_WithStockRef_DeductsStockAndLogsMovement()
    {
        var stock = new ProductStock
        {
            TenantId = _tenantId,
            ProductId = _productId,
            StoreId = _storeId,
            Quantity = 100,
            QuantityInitial = 100,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Status = "expired",
            LastCheckedAt = DateTime.UtcNow,
        };

        var writeOff = BuildWriteOff(stock: stock);
        _repo.GetByIdAsync(writeOff.Id, Arg.Any<CancellationToken>()).Returns(writeOff);
        _repo.GetStockByIdAsync(stock.Id, Arg.Any<CancellationToken>()).Returns(stock);
        _repo.GetByIdAsync(writeOff.Id, Arg.Any<CancellationToken>()).Returns(writeOff);

        var (result, error) = await _sut.ApproveAsync(writeOff.Id, _userId);

        Assert.Null(error);
        Assert.Equal(90, stock.Quantity);   // 100 - 10
        Assert.Equal("approved", writeOff.Status);
        Assert.Equal(_userId, writeOff.ApprovedBy);
        Assert.NotNull(writeOff.ApprovedAt);

        _repo.Received(1).UpdateStock(stock);
        await _repo.Received(1).AddMovementAsync(
            Arg.Is<StockMovement>(m => m.MovementType == "write_off" && m.Quantity == 10),
            Arg.Any<CancellationToken>());
    }

    // TASK-354: this used to assert the buggy behavior (no ProductStockId → silently
    // skipped, no deduction, no movement) — which is exactly what the mobile "quick
    // write-off" create flow sends for every item (it never picks a batch). Approving
    // one of those write-offs therefore never touched product_stock and never wrote a
    // stock_movements row. Fixed: no-batch items now FEFO-consume across the product's
    // batches at the write-off's store, nearest-expiry first.
    [Fact]
    public async Task ApproveAsync_ItemWithoutStockRef_FefoConsumesNearestExpiryBatchAndLogsMovement()
    {
        var nearExpiry = new ProductStock
        {
            TenantId = _tenantId, ProductId = _productId, StoreId = _storeId,
            Quantity = 4, QuantityInitial = 4,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Status = "critical", LastCheckedAt = DateTime.UtcNow,
        };
        var farExpiry = new ProductStock
        {
            TenantId = _tenantId, ProductId = _productId, StoreId = _storeId,
            Quantity = 20, QuantityInitial = 20,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = "safe", LastCheckedAt = DateTime.UtcNow,
        };

        var writeOff = BuildWriteOff(stock: null, withStockRef: false); // item.Quantity = 10
        _repo.GetByIdAsync(writeOff.Id, Arg.Any<CancellationToken>()).Returns(writeOff);
        _repo.GetFefoOrderedAsync(_productId, _storeId, Arg.Any<CancellationToken>())
             .Returns([nearExpiry, farExpiry]); // FEFO order: nearest expiry first

        var (result, error) = await _sut.ApproveAsync(writeOff.Id, _userId);

        Assert.Null(error);
        Assert.Equal("approved", writeOff.Status);
        // 10 requested: 4 from the near-expiry batch (fully drained), 6 from the far one.
        Assert.Equal(0, nearExpiry.Quantity);
        Assert.Equal(14, farExpiry.Quantity);
        _repo.Received(1).UpdateStock(nearExpiry);
        _repo.Received(1).UpdateStock(farExpiry);
        await _repo.Received(2).AddMovementAsync(
            Arg.Is<StockMovement>(m => m.MovementType == "write_off"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAsync_ItemWithoutStockRef_InsufficientStock_ReturnsErrorAndDoesNotSave()
    {
        var onlyBatch = new ProductStock
        {
            TenantId = _tenantId, ProductId = _productId, StoreId = _storeId,
            Quantity = 3, QuantityInitial = 3, // item requests 10 — not enough
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            Status = "safe", LastCheckedAt = DateTime.UtcNow,
        };

        var writeOff = BuildWriteOff(stock: null, withStockRef: false);
        _repo.GetByIdAsync(writeOff.Id, Arg.Any<CancellationToken>()).Returns(writeOff);
        _repo.GetFefoOrderedAsync(_productId, _storeId, Arg.Any<CancellationToken>())
             .Returns([onlyBatch]);

        var (result, error) = await _sut.ApproveAsync(writeOff.Id, _userId);

        Assert.Null(result);
        Assert.Contains("Insufficient stock", error);
        Assert.NotEqual("approved", writeOff.Status);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAsync_ExplicitStockRef_InsufficientQuantity_ReturnsErrorWithoutMutating()
    {
        var stock = new ProductStock
        {
            TenantId = _tenantId, ProductId = _productId, StoreId = _storeId,
            Quantity = 2, QuantityInitial = 2, // item requests 10 — not enough
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Status = "expired", LastCheckedAt = DateTime.UtcNow,
        };

        var writeOff = BuildWriteOff(stock: stock);
        _repo.GetByIdAsync(writeOff.Id, Arg.Any<CancellationToken>()).Returns(writeOff);
        _repo.GetStockByIdAsync(stock.Id, Arg.Any<CancellationToken>()).Returns(stock);

        var (result, error) = await _sut.ApproveAsync(writeOff.Id, _userId);

        Assert.Null(result);
        Assert.Contains("Insufficient quantity", error);
        Assert.Equal(2, stock.Quantity); // untouched
        Assert.NotEqual("approved", writeOff.Status);
        _repo.DidNotReceive().UpdateStock(Arg.Any<ProductStock>());
        await _repo.DidNotReceive().AddMovementAsync(Arg.Any<StockMovement>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Reject ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectAsync_AlreadyApproved_ReturnsError()
    {
        var writeOff = BuildWriteOff();
        writeOff.Status = "approved";
        _repo.GetByIdAsync(writeOff.Id, Arg.Any<CancellationToken>()).Returns(writeOff);

        var (wo, error) = await _sut.RejectAsync(writeOff.Id);
        Assert.Null(wo);
        Assert.Contains("Cannot reject", error);
    }

    [Fact]
    public async Task RejectAsync_PendingApproval_SetsRejected()
    {
        var writeOff = BuildWriteOff();
        _repo.GetByIdAsync(writeOff.Id, Arg.Any<CancellationToken>()).Returns(writeOff);

        await _sut.RejectAsync(writeOff.Id);

        Assert.Equal("rejected", writeOff.Status);
        _repo.Received(1).Update(writeOff);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private WriteOff BuildWriteOff(
        string? reason = "expired",
        decimal? totalLoss = null,
        ProductStock? stock = null,
        bool withStockRef = true)
    {
        var writeOff = new WriteOff
        {
            TenantId = _tenantId,
            StoreId = _storeId,
            Reason = reason,
            Status = "pending_approval",
            TotalLossAmount = totalLoss,
            CreatedBy = _userId,
        };

        writeOff.Items.Add(new WriteOffItem
        {
            WriteOffId = writeOff.Id,
            ProductStockId = withStockRef ? (stock?.Id ?? Guid.NewGuid()) : null,
            ProductId = _productId,
            Quantity = 10,
            UnitPrice = 25m,
            LossAmount = 250m,
        });

        return writeOff;
    }
}
