using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ShelfGuard.Application.Features.SupplierInventory;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using Xunit;

namespace ShelfGuard.Tests.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan 1-partitioned-book.md, D2). SupplierStockService
/// is the deliberate parallel of the retail StockService: add a batch (+ receipt movement),
/// FEFO-consume nearest-expiry-first returning a non-throwing shortfall, and xmin-guarded adjust.
/// </summary>
public sealed class SupplierStockServiceTests
{
    private readonly ISupplierStockRepository _repo = Substitute.For<ISupplierStockRepository>();
    private readonly SupplierStockService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _warehouseId = Guid.NewGuid();
    private readonly Guid _supplierItemId = Guid.NewGuid();

    public SupplierStockServiceTests()
    {
        _sut = new SupplierStockService(_repo);
        _repo.WarehouseExistsAsync(_tenantId, _warehouseId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.SupplierItemExistsAsync(_tenantId, _supplierItemId, Arg.Any<CancellationToken>()).Returns(true);
    }

    private SupplierStock Batch(decimal qty, int daysToExpiry) => new()
    {
        TenantId = _tenantId,
        SupplierItemId = _supplierItemId,
        WarehouseId = _warehouseId,
        Quantity = qty,
        QuantityInitial = qty,
        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysToExpiry)),
        Status = "safe",
        LastCheckedAt = DateTime.UtcNow,
    };

    // ── AddBatch ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddBatchAsync_ZeroQuantity_ReturnsError()
    {
        var (stock, error) = await _sut.AddBatchAsync(
            _tenantId, _warehouseId, _supplierItemId,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), 0, null, _userId);

        Assert.Null(stock);
        Assert.NotNull(error);
        await _repo.DidNotReceive().AddAsync(Arg.Any<SupplierStock>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddBatchAsync_PastExpiry_ReturnsError()
    {
        var (stock, error) = await _sut.AddBatchAsync(
            _tenantId, _warehouseId, _supplierItemId,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), 10, null, _userId);

        Assert.Null(stock);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task AddBatchAsync_UnknownWarehouse_ReturnsError()
    {
        _repo.WarehouseExistsAsync(_tenantId, _warehouseId, Arg.Any<CancellationToken>()).Returns(false);

        var (stock, error) = await _sut.AddBatchAsync(
            _tenantId, _warehouseId, _supplierItemId,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), 10, null, _userId);

        Assert.Null(stock);
        Assert.Equal("Склад не знайдено.", error);
    }

    [Fact]
    public async Task AddBatchAsync_Valid_AddsBatchAndReceiptMovement()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(45));
        _repo.GetByIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(ci => new SupplierStock
             {
                 TenantId = _tenantId,
                 SupplierItemId = _supplierItemId,
                 WarehouseId = _warehouseId,
                 Quantity = 40,
                 QuantityInitial = 40,
                 ExpiryDate = expiry,
                 BatchNumber = "B-1",
                 LastCheckedAt = DateTime.UtcNow,
             });

        var (stock, error) = await _sut.AddBatchAsync(
            _tenantId, _warehouseId, _supplierItemId, expiry, 40, "B-1", _userId);

        Assert.Null(error);
        Assert.NotNull(stock);
        await _repo.Received(1).AddAsync(
            Arg.Is<SupplierStock>(s => s.TenantId == _tenantId && s.Quantity == 40
                && s.QuantityInitial == 40 && s.SourceType == "manual"),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).AddMovementAsync(
            Arg.Is<SupplierStockMovement>(m => m.MovementType == "receipt" && m.Quantity == 40
                && m.QuantityBefore == 0 && m.QuantityAfter == 40 && m.ToWarehouseId == _warehouseId),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── FEFO consume ─────────────────────────────────────────────────────────

    [Fact]
    public async Task FefoConsumeAsync_WalksNearestExpiryFirst_AndFullyCovers()
    {
        var near = Batch(20, 3);
        var far = Batch(50, 30);
        _repo.GetFefoOrderedAsync(_tenantId, _supplierItemId, _warehouseId, Arg.Any<CancellationToken>())
             .Returns([near, far]);

        var result = await _sut.FefoConsumeAsync(
            _tenantId, _supplierItemId, _warehouseId, 30, "marketplace_order", Guid.NewGuid(), _userId);

        Assert.Equal(30, result.QuantityConsumed);
        Assert.Equal(0, result.Shortfall);
        Assert.Equal(2, result.BatchesConsumed.Count);
        Assert.Equal(near.ExpiryDate, result.BatchesConsumed[0].ExpiryDate); // nearest first
        Assert.Equal(20, result.BatchesConsumed[0].Qty);
        Assert.Equal(10, result.BatchesConsumed[1].Qty);
        Assert.Equal(0, near.Quantity);
        Assert.Equal(40, far.Quantity);
        await _repo.Received(2).AddMovementAsync(
            Arg.Is<SupplierStockMovement>(m => m.MovementType == "ship" && m.FromWarehouseId == _warehouseId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FefoConsumeAsync_InsufficientStock_ReturnsShortfall_DoesNotThrow()
    {
        var only = Batch(5, 3);
        _repo.GetFefoOrderedAsync(_tenantId, _supplierItemId, _warehouseId, Arg.Any<CancellationToken>())
             .Returns([only]);

        var result = await _sut.FefoConsumeAsync(
            _tenantId, _supplierItemId, _warehouseId, 12, "marketplace_order", Guid.NewGuid(), _userId);

        Assert.Equal(5, result.QuantityConsumed);
        Assert.Equal(7, result.Shortfall);
        Assert.Single(result.BatchesConsumed);
        Assert.Equal(0, only.Quantity);
    }

    [Fact]
    public async Task FefoConsumeAsync_NoStock_ReturnsFullShortfall_NoSave()
    {
        _repo.GetFefoOrderedAsync(_tenantId, _supplierItemId, _warehouseId, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await _sut.FefoConsumeAsync(
            _tenantId, _supplierItemId, _warehouseId, 8, "marketplace_order", Guid.NewGuid(), _userId);

        Assert.Equal(0, result.QuantityConsumed);
        Assert.Equal(8, result.Shortfall);
        Assert.Empty(result.BatchesConsumed);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Adjust (xmin) ────────────────────────────────────────────────────────

    [Fact]
    public async Task AdjustAsync_Valid_WritesAdjustMovement()
    {
        var batch = Batch(30, 20);
        _repo.GetByIdAsync(_tenantId, batch.Id, Arg.Any<CancellationToken>()).Returns(batch, batch);

        var (stock, error) = await _sut.AdjustAsync(_tenantId, batch.Id, 25, "стоктейк", _userId);

        Assert.Null(error);
        Assert.NotNull(stock);
        Assert.Equal(25, batch.Quantity);
        await _repo.Received(1).AddMovementAsync(
            Arg.Is<SupplierStockMovement>(m => m.MovementType == "adjust" && m.Quantity == 5
                && m.QuantityBefore == 30 && m.QuantityAfter == 25 && m.FromWarehouseId == _warehouseId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdjustAsync_ConcurrentWrite_SurfacedAsCleanRetryError()
    {
        var batch = Batch(30, 20);
        _repo.GetByIdAsync(_tenantId, batch.Id, Arg.Any<CancellationToken>()).Returns(batch);
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>())
             .Throws(new ConcurrencyConflictException("simulated concurrent write conflict"));

        var (stock, error) = await _sut.AdjustAsync(_tenantId, batch.Id, 25, "стоктейк", _userId);

        Assert.Null(stock);
        Assert.NotNull(error);
        Assert.Contains("інша операція", error);
    }

    [Fact]
    public async Task AdjustAsync_NegativeQuantity_ReturnsError()
    {
        var (stock, error) = await _sut.AdjustAsync(_tenantId, Guid.NewGuid(), -1, null, _userId);

        Assert.Null(stock);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task AdjustAsync_BatchNotFound_ReturnsError()
    {
        _repo.GetByIdAsync(_tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((SupplierStock?)null);

        var (stock, error) = await _sut.AdjustAsync(_tenantId, Guid.NewGuid(), 5, null, _userId);

        Assert.Null(stock);
        Assert.Equal("Партію не знайдено.", error);
    }
}
