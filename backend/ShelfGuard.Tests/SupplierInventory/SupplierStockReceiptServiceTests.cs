using NSubstitute;
using ShelfGuard.Application.Features.SupplierInventory;
using ShelfGuard.Application.Features.SupplierInventory.Dtos;
using ShelfGuard.Domain.Entities;
using Xunit;

namespace ShelfGuard.Tests.SupplierInventory;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan 1-partitioned-book.md, D3). SupplierStockReceiptService
/// mirrors ReceiptService's draft → add lines → finalize shape, but a line is keyed on
/// (SupplierItem, expiry, batch) — N lines may repeat a SupplierItem. Finalize gates on every
/// line having an ExpiryDate and a positive Quantity, then emits one batch + one receipt movement
/// per line.
/// </summary>
public sealed class SupplierStockReceiptServiceTests
{
    private readonly ISupplierStockReceiptRepository _repo = Substitute.For<ISupplierStockReceiptRepository>();
    private readonly ISupplierStockRepository _stockRepo = Substitute.For<ISupplierStockRepository>();
    private readonly SupplierStockReceiptService _sut;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _warehouseId = Guid.NewGuid();
    private readonly Guid _supplierItemId = Guid.NewGuid();

    private readonly SupplierStockReceipt _receipt;

    public SupplierStockReceiptServiceTests()
    {
        _sut = new SupplierStockReceiptService(_repo, _stockRepo);
        _receipt = new SupplierStockReceipt
        {
            TenantId = _tenantId,
            WarehouseId = _warehouseId,
            Status = "draft",
        };
        _repo.GetByIdAsync(_tenantId, _receipt.Id, Arg.Any<CancellationToken>()).Returns(_receipt);
        _stockRepo.WarehouseExistsAsync(_tenantId, _warehouseId, Arg.Any<CancellationToken>()).Returns(true);
        _stockRepo.SupplierItemExistsAsync(_tenantId, _supplierItemId, Arg.Any<CancellationToken>()).Returns(true);
        // AddLineAsync now calls _repo.AddItem (explicit DbSet.Add) instead of receipt.Items.Add +
        // _repo.Update — see TASK-697. Simulate the real repo+reload: a persisted line shows up in
        // the receipt's Items on the next GetByIdAsync.
        _repo.When(r => r.AddItem(Arg.Any<SupplierStockReceiptItem>()))
             .Do(ci => _receipt.Items.Add(ci.Arg<SupplierStockReceiptItem>()));
    }

    private Task<(SupplierStockReceiptDto? Receipt, string? Error)> AddLine(
        DateOnly? expiry, decimal qty, string? batch) =>
        _sut.AddLineAsync(_tenantId, _receipt.Id,
            new AddSupplierReceiptLineRequest(_supplierItemId, expiry, qty, batch, null, null));

    // ── draft → 2 lines same product, different expiry → finalize → 2 batches ──

    [Fact]
    public async Task Finalize_TwoLinesSameProductDifferentExpiry_CreatesTwoFefoBatches()
    {
        var e1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var e2 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90));
        await AddLine(e1, 100, "LOT-A");
        await AddLine(e2, 50, "LOT-B");
        Assert.Equal(2, _receipt.Items.Count);

        var createdBatches = new List<SupplierStock>();
        _repo.When(r => r.AddStockAsync(Arg.Any<SupplierStock>(), Arg.Any<CancellationToken>()))
             .Do(ci => createdBatches.Add(ci.Arg<SupplierStock>()));
        var movements = new List<SupplierStockMovement>();
        _repo.When(r => r.AddMovementAsync(Arg.Any<SupplierStockMovement>(), Arg.Any<CancellationToken>()))
             .Do(ci => movements.Add(ci.Arg<SupplierStockMovement>()));

        var (dto, error) = await _sut.ReceiveAsync(_tenantId, _receipt.Id, _userId);

        Assert.Null(error);
        Assert.NotNull(dto);
        Assert.Equal("received", _receipt.Status);
        Assert.Equal(_userId, _receipt.ReceivedBy);
        Assert.NotNull(_receipt.ReceivedAt);

        Assert.Equal(2, createdBatches.Count);
        Assert.All(createdBatches, b =>
        {
            Assert.Equal(_tenantId, b.TenantId);
            Assert.Equal(_warehouseId, b.WarehouseId);
            Assert.Equal(_supplierItemId, b.SupplierItemId);
            Assert.Equal("supplier_receipt", b.SourceType);
            Assert.Equal(_receipt.Id, b.SourceId);
        });
        Assert.Contains(createdBatches, b => b.ExpiryDate == e1 && b.Quantity == 100 && b.BatchNumber == "LOT-A");
        Assert.Contains(createdBatches, b => b.ExpiryDate == e2 && b.Quantity == 50 && b.BatchNumber == "LOT-B");

        Assert.Equal(2, movements.Count);
        Assert.All(movements, m =>
        {
            Assert.Equal("receipt", m.MovementType);
            Assert.Equal(0, m.QuantityBefore);
            Assert.Equal(_warehouseId, m.ToWarehouseId);
        });
    }

    [Fact]
    public async Task Finalize_LineMissingExpiry_RejectedAndReceiptStaysDraft()
    {
        await AddLine(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), 100, "LOT-A");
        await AddLine(null, 50, "LOT-B"); // no expiry

        var (dto, error) = await _sut.ReceiveAsync(_tenantId, _receipt.Id, _userId);

        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.Equal("draft", _receipt.Status);
        await _repo.DidNotReceive().AddStockAsync(Arg.Any<SupplierStock>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddMovementAsync(Arg.Any<SupplierStockMovement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Finalize_EmptyReceipt_Rejected()
    {
        var (dto, error) = await _sut.ReceiveAsync(_tenantId, _receipt.Id, _userId);

        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.Equal("draft", _receipt.Status);
    }

    [Fact]
    public async Task Finalize_AlreadyReceived_Rejected()
    {
        _receipt.Status = "received";

        var (dto, error) = await _sut.ReceiveAsync(_tenantId, _receipt.Id, _userId);

        Assert.Null(dto);
        Assert.NotNull(error);
    }

    // ── line management ──────────────────────────────────────────────────────

    [Fact]
    public async Task AddLine_UnknownSupplierItem_Rejected()
    {
        _stockRepo.SupplierItemExistsAsync(_tenantId, _supplierItemId, Arg.Any<CancellationToken>()).Returns(false);

        var (dto, error) = await AddLine(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), 5, null);

        Assert.Null(dto);
        Assert.NotNull(error);
        Assert.Empty(_receipt.Items);
    }

    [Fact]
    public async Task AddLine_NonDraftReceipt_Rejected()
    {
        _receipt.Status = "received";

        var (dto, error) = await _sut.AddLineAsync(_tenantId, _receipt.Id,
            new AddSupplierReceiptLineRequest(_supplierItemId, null, 5, null, null, null));

        Assert.Null(dto);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CreateDraft_UnknownWarehouse_Rejected()
    {
        _stockRepo.WarehouseExistsAsync(_tenantId, _warehouseId, Arg.Any<CancellationToken>()).Returns(false);

        var (dto, error) = await _sut.CreateDraftAsync(_tenantId, _warehouseId, "REF", null, _userId);

        Assert.Null(dto);
        Assert.Equal("Склад не знайдено.", error);
    }
}
