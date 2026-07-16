using NSubstitute;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Application.Features.Stock.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Stock;

public sealed class StockServiceTests
{
    private readonly IStockRepository _repo = Substitute.For<IStockRepository>();
    private readonly StockService _sut;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _storeId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public StockServiceTests() => _sut = new StockService(_repo);

    // ── Create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ZeroQuantity_ReturnsError()
    {
        var req = new CreateStockRequest(
            _productId, _storeId, null, null, null, 0,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null, null);

        var (stock, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(stock);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_PastExpiryDate_ReturnsError()
    {
        var req = new CreateStockRequest(
            _productId, _storeId, null, null, null, 10,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), null, null);

        var (stock, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(stock);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_AddsStockAndMovement()
    {
        var expiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var req = new CreateStockRequest(
            _productId, _storeId, null, null, "BATCH-001", 50,
            expiry, "receipt", null);

        // GetByIdAsync will be called after save — return a minimal stub
        _repo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
             .Returns(ci => new ProductStock
             {
                 TenantId = _tenantId,
                 ProductId = _productId,
                 StoreId = _storeId,
                 Quantity = 50,
                 QuantityInitial = 50,
                 ExpiryDate = expiry,
                 BatchNumber = "BATCH-001",
                 LastCheckedAt = DateTime.UtcNow,
             });

        var (stock, error) = await _sut.CreateAsync(_tenantId, _userId, req);

        Assert.Null(error);
        Assert.NotNull(stock);
        await _repo.Received(1).AddAsync(
            Arg.Is<ProductStock>(s => s.TenantId == _tenantId && s.Quantity == 50),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).AddMovementAsync(
            Arg.Is<StockMovement>(m => m.MovementType == "receipt" && m.Quantity == 50),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── FEFO Consume ───────────────────────────────────────────────────────

    [Fact]
    public async Task FefoConsumeAsync_ZeroQuantity_ReturnsError()
    {
        var req = new FefoConsumeRequest(_productId, _storeId, 0, null);
        var result = await _sut.FefoConsumeAsync(_tenantId, _userId, req);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(0, result.QuantityConsumed);
    }

    [Fact]
    public async Task FefoConsumeAsync_NoStock_ReturnsError()
    {
        _repo.GetFefoOrderedAsync(_productId, _storeId, Arg.Any<CancellationToken>())
             .Returns([]);

        var req = new FefoConsumeRequest(_productId, _storeId, 10, null);
        var result = await _sut.FefoConsumeAsync(_tenantId, _userId, req);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(0, result.QuantityConsumed);
    }

    [Fact]
    public async Task FefoConsumeAsync_SufficientSingleBatch_ConsumesAll()
    {
        var batch = CreateBatch(quantity: 100, daysToExpiry: 10);
        _repo.GetFefoOrderedAsync(_productId, _storeId, Arg.Any<CancellationToken>())
             .Returns([batch]);

        var req = new FefoConsumeRequest(_productId, _storeId, 30, null);
        var result = await _sut.FefoConsumeAsync(_tenantId, _userId, req);

        Assert.True(result.Success);
        Assert.Equal(30, result.QuantityConsumed);
        Assert.Equal(0, result.QuantityShortfall);
        Assert.Single(result.BatchesConsumed);
        Assert.Equal(70, batch.Quantity);
        await _repo.Received(1).AddMovementAsync(
            Arg.Is<StockMovement>(m => m.MovementType == "write_off" && m.Quantity == 30),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FefoConsumeAsync_MultipleBatches_ConsumesFefoOrder()
    {
        var olderExpiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var newerExpiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

        var batch1 = CreateBatch(quantity: 20, daysToExpiry: 3);   // expires sooner
        var batch2 = CreateBatch(quantity: 50, daysToExpiry: 10);  // expires later

        // FEFO: batch1 first
        _repo.GetFefoOrderedAsync(_productId, _storeId, Arg.Any<CancellationToken>())
             .Returns([batch1, batch2]);

        var req = new FefoConsumeRequest(_productId, _storeId, 30, null);
        var result = await _sut.FefoConsumeAsync(_tenantId, _userId, req);

        Assert.True(result.Success);
        Assert.Equal(30, result.QuantityConsumed);
        Assert.Equal(0, result.QuantityShortfall);
        Assert.Equal(2, result.BatchesConsumed.Count);
        Assert.Equal(0, batch1.Quantity);       // fully consumed
        Assert.Equal(40, batch2.Quantity);      // 50 - 10 remaining
    }

    [Fact]
    public async Task FefoConsumeAsync_InsufficientStock_PartialConsumeReturnsShortfall()
    {
        var batch = CreateBatch(quantity: 5, daysToExpiry: 3);
        _repo.GetFefoOrderedAsync(_productId, _storeId, Arg.Any<CancellationToken>())
             .Returns([batch]);

        var req = new FefoConsumeRequest(_productId, _storeId, 10, null);
        var result = await _sut.FefoConsumeAsync(_tenantId, _userId, req);

        Assert.False(result.Success);
        Assert.Equal(5, result.QuantityConsumed);
        Assert.Equal(5, result.QuantityShortfall);
        Assert.NotNull(result.Error);
        Assert.Equal(0, batch.Quantity);
    }

    [Fact]
    public async Task FefoConsumeAsync_BatchMarkedSoldOutAfterFullConsumption()
    {
        var batch = CreateBatch(quantity: 10, daysToExpiry: 5);
        _repo.GetFefoOrderedAsync(_productId, _storeId, Arg.Any<CancellationToken>())
             .Returns([batch]);

        var req = new FefoConsumeRequest(_productId, _storeId, 10, null);
        await _sut.FefoConsumeAsync(_tenantId, _userId, req);

        Assert.Equal("sold_out", batch.Status);
    }

    [Fact]
    public async Task FefoConsumeAsync_TiedExpiryDates_ConsumesAcrossBothBatches()
    {
        // Two batches share the exact same expiry_date — FEFO doesn't mandate a specific
        // tie-break order, but consumption must still walk through both and fully satisfy
        // the request (neither batch should be skipped just because dates are equal).
        var sameExpiry = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var batchA = CreateBatch(quantity: 10, daysToExpiry: 5);
        var batchB = CreateBatch(quantity: 10, daysToExpiry: 5);
        Assert.Equal(sameExpiry, batchA.ExpiryDate);
        Assert.Equal(sameExpiry, batchB.ExpiryDate);

        _repo.GetFefoOrderedAsync(_productId, _storeId, Arg.Any<CancellationToken>())
             .Returns([batchA, batchB]);

        var req = new FefoConsumeRequest(_productId, _storeId, 15, null);
        var result = await _sut.FefoConsumeAsync(_tenantId, _userId, req);

        Assert.True(result.Success);
        Assert.Equal(15, result.QuantityConsumed);
        Assert.Equal(0, result.QuantityShortfall);
        Assert.Equal(2, result.BatchesConsumed.Count);
        // Total remaining across both tied batches must be exactly 5 — no quantity lost or
        // double-counted regardless of which of the two equally-eligible batches went first.
        Assert.Equal(5, batchA.Quantity + batchB.Quantity);
    }

    // ── Suggestions (deficit-transfer lookup) ────────────────────────────────

    [Fact]
    public async Task GetSuggestionsAsync_UsesBulkDeficitLookup_NotPerBatchQuery()
    {
        // Regression guard: GetSuggestionsAsync must resolve deficit stocks for all
        // action-required batches via a single GetDeficitStocksBulkAsync call, not one
        // GetDeficitStocksAsync round-trip per batch (N+1).
        var batch1 = CreateBatch(quantity: 5, daysToExpiry: 3);
        var batch2 = CreateBatch(quantity: 5, daysToExpiry: 4);
        _repo.GetActionRequiredAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
             .Returns([batch1, batch2]);
        _repo.GetProductionStoresAsync(Arg.Any<CancellationToken>()).Returns([]);
        _repo.GetDeficitStocksBulkAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
             .Returns(new Dictionary<Guid, List<ProductStock>>());

        await _sut.GetSuggestionsAsync(null);

        await _repo.Received(1).GetDeficitStocksBulkAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().GetDeficitStocksAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSuggestionsAsync_DeficitInOwnStore_IsExcludedFromTransferSuggestion()
    {
        // A "deficit" row belonging to the SAME store as the batch itself must never be
        // offered as a transfer target (that would suggest moving stock to itself).
        var batch = CreateBatch(quantity: 5, daysToExpiry: 3);
        var otherStoreId = Guid.NewGuid();
        var otherStore = new Location { Id = otherStoreId, Name = "Filія 2" };

        var deficitSameStore = CreateBatch(quantity: 1, daysToExpiry: 20);
        var deficitOtherStore = new ProductStock
        {
            TenantId = _tenantId,
            ProductId = _productId,
            StoreId = otherStoreId,
            Quantity = 1,
            QuantityInitial = 1,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25)),
            Status = "safe",
            LastCheckedAt = DateTime.UtcNow,
            Store = otherStore,
        };

        _repo.GetActionRequiredAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns([batch]);
        _repo.GetProductionStoresAsync(Arg.Any<CancellationToken>()).Returns([]);
        _repo.GetDeficitStocksBulkAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
             .Returns(new Dictionary<Guid, List<ProductStock>>
             {
                 [_productId] = [deficitSameStore, deficitOtherStore], // same-store entry ordered first
             });

        var suggestions = await _sut.GetSuggestionsAsync(null);

        var transferAction = Assert.Single(suggestions).Actions.SingleOrDefault(a => a.Type == "transfer");
        Assert.NotNull(transferAction);
        Assert.Equal(otherStoreId, transferAction!.TargetStoreId);
    }

    // ── Verify ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_NotFound_ReturnsError()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((ProductStock?)null);

        var (stock, error) = await _sut.VerifyAsync(id, _userId);

        Assert.Null(stock);
        Assert.Equal("Stock batch not found.", error);
    }

    [Fact]
    public async Task VerifyAsync_UpdatesLastCheckedAt()
    {
        var batch = CreateBatch(quantity: 10, daysToExpiry: 100);
        batch.LastCheckedAt = DateTime.UtcNow.AddDays(-100);
        _repo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        await _sut.VerifyAsync(batch.Id, _userId);

        Assert.True((DateTime.UtcNow - batch.LastCheckedAt).TotalSeconds < 5);
        _repo.Received(1).Update(batch);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private ProductStock CreateBatch(decimal quantity, int daysToExpiry) => new()
    {
        TenantId = _tenantId,
        ProductId = _productId,
        StoreId = _storeId,
        Quantity = quantity,
        QuantityInitial = quantity,
        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysToExpiry)),
        Status = StockStatus.Compute(quantity, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysToExpiry)), DateTime.UtcNow),
        LastCheckedAt = DateTime.UtcNow,
    };
}
