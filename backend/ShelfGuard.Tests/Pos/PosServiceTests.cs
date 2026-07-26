using Microsoft.Extensions.Logging.Abstractions;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Exceptions;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Pos;

// ── Fakes ──────────────────────────────────────────────────────────────────

file sealed class FakePosRepo : IPosRepository
{
    public List<PosShift> Shifts { get; } = [];
    public List<PosTransaction> Transactions { get; } = [];
    public List<StockEvent> Events { get; } = [];
    public int SaveCount { get; private set; }

    /// <summary>TASK-356: simulates a concurrent-write conflict (another sale raced this
    /// one on the same ProductStock row) on the Nth call to SaveChangesAsync.</summary>
    public int? ThrowConcurrencyOnSaveCall { get; set; }

    public Task<PosShift?> GetOpenShiftAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Shifts.FirstOrDefault(s => s.TenantId == tenantId && s.ClosedAt is null));

    public Task<PosShift?> GetShiftByIdAsync(Guid shiftId, CancellationToken ct = default) =>
        Task.FromResult(Shifts.FirstOrDefault(s => s.Id == shiftId));

    public Task AddShiftAsync(PosShift shift, CancellationToken ct = default)
    {
        Shifts.Add(shift);
        return Task.CompletedTask;
    }

    public void UpdateShift(PosShift shift) { }

    public Task<PosTransaction?> GetTransactionByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.Id == id));

    public Task<List<PosTransaction>> GetTransactionsByShiftAsync(Guid shiftId, CancellationToken ct = default) =>
        Task.FromResult(Transactions.Where(t => t.ShiftId == shiftId).ToList());

    public Task<decimal> GetCashSalesTotalForShiftAsync(Guid shiftId, CancellationToken ct = default) =>
        Task.FromResult(Transactions
            .Where(t => t.ShiftId == shiftId && t.PaymentType == "cash")
            .Sum(t => t.TotalAmount));

    public Task AddTransactionAsync(PosTransaction tx, CancellationToken ct = default)
    {
        Transactions.Add(tx);
        return Task.CompletedTask;
    }

    public void UpdateTransaction(PosTransaction tx) { }

    public Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default)
    {
        Events.Add(ev);
        return Task.CompletedTask;
    }

    public Task<List<PosTransaction>> GetPendingFiscalizationAsync(
        int maxRetries, DateTime createdBefore, CancellationToken ct = default) =>
        Task.FromResult(Transactions
            .Where(t =>
                t.Status == "pending_fiscalization" &&
                t.RetryCount < maxRetries &&
                t.CreatedAt < createdBefore)
            .ToList());

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        if (ThrowConcurrencyOnSaveCall == SaveCount)
            throw new ConcurrencyConflictException("simulated concurrent write conflict");
        return Task.CompletedTask;
    }
}

file sealed class FakeStockRepo : IStockRepository
{
    public List<ProductStock> Batches { get; } = [];

    public Task<List<ProductStock>> GetFefoOrderedAsync(Guid productId, Guid storeId, CancellationToken ct = default) =>
        Task.FromResult(Batches
            .Where(b => b.ProductId == productId && b.StoreId == storeId && b.Quantity > 0)
            .OrderBy(b => b.ExpiryDate)
            .ToList());

    public Task<List<ProductStock>> GetAllAsync(Guid? storeId, string? status, Guid? zoneId, Guid? productId, CancellationToken ct = default) =>
        Task.FromResult(Batches
            .Where(b => (!storeId.HasValue || b.StoreId == storeId)
                     && (!productId.HasValue || b.ProductId == productId))
            .ToList());

    public void Update(ProductStock stock) { }

    // Stubs for unused methods
    public Task<ProductStock?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ProductStock?>(null);
    public Task<List<ProductStock>> GetExpiringAsync(Guid? storeId, int days, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetExpiredAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetNeedsCheckAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetActionRequiredAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetDeficitStocksAsync(Guid productId, Guid excludeStoreId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<Dictionary<Guid, List<ProductStock>>> GetDeficitStocksBulkAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct = default) =>
        Task.FromResult(productIds.ToDictionary(id => id, _ => new List<ProductStock>()));
    public Task<(List<ProductStock> Items, int Total)> GetPagedAsync(Guid? storeId, string? status, Guid? zoneId, Guid? productId, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult((new List<ProductStock>(), 0));
    public Task<List<Location>> GetProductionStoresAsync(CancellationToken ct = default) => Task.FromResult(new List<Location>());
    public Task<Dictionary<string, int>> GetStatusCountsAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, int>());
    public Task<List<(Guid? ZoneId, string ZoneName, string ZoneType, string Status)>> GetStockByZoneRawAsync(Guid? storeId, CancellationToken ct = default) =>
        Task.FromResult(new List<(Guid?, string, string, string)>());
    public Task AddAsync(ProductStock stock, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FakeCatalogRepo : IItemRepository
{
    public List<Item> Products { get; } = [];

    public Task<Item?> GetByBarcodeAsync(string barcode, CancellationToken ct = default) =>
        Task.FromResult(Products.FirstOrDefault(p => p.Barcodes.Contains(barcode)));

    public Task<List<Item>> GetAllAsync(Guid? categoryId, Guid? segmentId, string? managementType, CancellationToken ct = default) =>
        Task.FromResult(Products);

    public Task<(List<Item> Items, int Total)> GetPagedAsync(Guid? categoryId, Guid? segmentId, string? managementType, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult((Products, Products.Count));

    public Task<Item?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

    public Task<List<ProductSupplierSetting>> GetSupplierSettingsAsync(Guid productId, CancellationToken ct = default) => Task.FromResult(new List<ProductSupplierSetting>());
    public Task<bool> SupplierSettingExistsAsync(Guid productId, Guid supplierId, CancellationToken ct = default) => Task.FromResult(false);
    public Task AddAsync(Item product, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddSupplierSettingAsync(ProductSupplierSetting setting, CancellationToken ct = default) => Task.CompletedTask;
    public void Update(Item product) { }
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FakeDiscountRepo : IDiscountRepository
{
    public List<Discount> Discounts { get; } = [];

    public Task<IReadOnlyList<Discount>> GetAllAsync(Guid tenantId, Guid? storeId = null, string? status = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Discount>>(Discounts
            .Where(d => d.TenantId == tenantId
                && (!storeId.HasValue || d.StoreId == storeId)
                && (status is null || d.Status == status))
            .ToList());

    public Task<Discount?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Discounts.FirstOrDefault(d => d.Id == id));

    public Task AddAsync(Discount discount, CancellationToken ct = default) => Task.CompletedTask;
    public void Update(Discount discount) { }
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>TASK-405 (Loyalty Фаза 0): fake ILoyaltyRepository backing PosService's new accrual/redemption path.</summary>
file sealed class FakeLoyaltyRepo : ILoyaltyRepository
{
    public List<LoyaltyMembership> Memberships { get; } = [];
    public List<LoyaltyLedgerEntry> LedgerEntries { get; } = [];
    public List<LoyaltyProgramSettings> Settings { get; } = [];

    public Task<LoyaltyMembership?> GetMembershipByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.Id == id && m.TenantId == tenantId));

    public Task<LoyaltyMembership?> GetMembershipByTenantConsumerAsync(Guid tenantId, Guid consumerAccountId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.TenantId == tenantId && m.ConsumerAccountId == consumerAccountId));

    public Task<LoyaltyMembership?> GetMembershipByLinkedUserAsync(Guid tenantId, Guid linkedUserId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.FirstOrDefault(m => m.TenantId == tenantId && m.LinkedUserId == linkedUserId));

    public Task<List<LoyaltyMembership>> GetMembershipsForConsumerAsync(Guid consumerAccountId, CancellationToken ct = default) =>
        Task.FromResult(Memberships.Where(m => m.ConsumerAccountId == consumerAccountId).ToList());

    public Task AddMembershipAsync(LoyaltyMembership membership, CancellationToken ct = default)
    {
        Memberships.Add(membership);
        return Task.CompletedTask;
    }

    public void UpdateMembership(LoyaltyMembership membership) { }

    public Task<bool> TryClaimTimestepAsync(Guid membershipId, Guid tenantId, long timestep, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<(List<LoyaltyLedgerEntry> Items, int Total)> GetLedgerPagedAsync(
        Guid tenantId, Guid membershipId, int page, int pageSize, CancellationToken ct = default)
    {
        var items = LedgerEntries.Where(e => e.TenantId == tenantId && e.MembershipId == membershipId).ToList();
        return Task.FromResult((items, items.Count));
    }

    public Task<List<LoyaltyLedgerEntry>> GetLedgerEntriesForTransactionsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default) =>
        Task.FromResult(LedgerEntries
            .Where(e => e.TenantId == tenantId && e.PosTransactionId.HasValue && transactionIds.Contains(e.PosTransactionId.Value))
            .ToList());

    public Task AddLedgerEntryAsync(LoyaltyLedgerEntry entry, CancellationToken ct = default)
    {
        LedgerEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<LoyaltyProgramSettings?> GetSettingsAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Settings.FirstOrDefault(s => s.TenantId == tenantId));

    public Task AddSettingsAsync(LoyaltyProgramSettings settings, CancellationToken ct = default)
    {
        Settings.Add(settings);
        return Task.CompletedTask;
    }

    public void UpdateSettings(LoyaltyProgramSettings settings) { }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>TASK-405: fake ICustomerRepository backing PosService's Customer.TotalOrders/TotalSpent update.</summary>
file sealed class FakeCustomerRepo : ICustomerRepository
{
    public List<Customer> Customers { get; } = [];

    public Task<List<Customer>> GetAllAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult(Customers.Where(c => c.TenantId == tenantId).ToList());

    public Task<(List<Customer> Items, int Total)> GetPagedAsync(
        Guid tenantId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var items = Customers.Where(c => c.TenantId == tenantId).ToList();
        return Task.FromResult((items, items.Count));
    }

    public Task<Customer?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Id == id && c.TenantId == tenantId));

    public Task<Customer?> GetByIdWithTransactionsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.Id == id && c.TenantId == tenantId));

    public Task<bool> ExistsByPhoneAsync(string phone, Guid tenantId, Guid? excludeId, CancellationToken ct) =>
        Task.FromResult(Customers.Any(c => c.TenantId == tenantId && c.Phone == phone && c.Id != excludeId));

    public Task<Customer?> FindByPhoneAsync(string phone, Guid tenantId, CancellationToken ct) =>
        Task.FromResult(Customers.FirstOrDefault(c => c.TenantId == tenantId && c.Phone == phone));

    public Task<Customer> CreateAsync(Customer customer, CancellationToken ct)
    {
        Customers.Add(customer);
        return Task.FromResult(customer);
    }

    public Task UpdateAsync(Customer customer, CancellationToken ct) => Task.CompletedTask;

    public Task DeleteAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        Customers.RemoveAll(c => c.Id == id && c.TenantId == tenantId);
        return Task.CompletedTask;
    }
}

file sealed class FakeFiscalFactory : IFiscalServiceFactory
{
    private readonly IFiscalService _service;
    public PrroConnectionConfig? EnvFallback => null;
    public FakeFiscalFactory(IFiscalService service) => _service = service;
    public Task<IFiscalService> GetForTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(_service);
    public IFiscalService Create(Guid tenantId, PrroConnectionConfig config) => _service;
}

/// <summary>Simulates a Checkbox provider that fiscalizes instantly and successfully —
/// used to pin the TASK-356 inline (awaited) online-fiscalization behavior.</summary>
file sealed class FakeSuccessfulFiscalService : IFiscalService
{
    public Task<FiscalHealthResult> PingAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalHealthResult(true, "fake", "FN-TEST", true, false, null));

    public Task<FiscalCashierResult> CheckCashierAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalCashierResult(true, "Test Cashier", null));

    public Task<FiscalShiftResult> OpenShiftAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult("shift-1", FiscalShiftStatus.Opened, 1, DateTimeOffset.UtcNow, null));

    public Task<FiscalShiftResult> CloseShiftAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult("shift-1", FiscalShiftStatus.Closed, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

    public Task<FiscalShiftResult> GetShiftStatusAsync(string providerShiftId, CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult(providerShiftId, FiscalShiftStatus.Opened, 1, DateTimeOffset.UtcNow, null));

    public Task<FiscalReceiptResult> CreateReceiptAsync(FiscalReceiptRequest request, CancellationToken ct = default) =>
        Task.FromResult(new FiscalReceiptResult(
            request.LocalReceiptId, FiscalReceiptStatus.Done, "FN-12345", DateTimeOffset.UtcNow, null, null));

    public Task<FiscalReceiptResult> GetReceiptStatusAsync(string providerReceiptId, CancellationToken ct = default) =>
        Task.FromResult(new FiscalReceiptResult(
            providerReceiptId, FiscalReceiptStatus.Done, "FN-12345", DateTimeOffset.UtcNow, null, null));
}

// ── Tests ──────────────────────────────────────────────────────────────────

public sealed class PosServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CashierId = Guid.NewGuid();
    private static readonly Guid StoreId = Guid.NewGuid();

    private static PosService BuildService(
        IPosRepository? pos = null,
        IStockRepository? stock = null,
        IItemRepository? catalog = null,
        IDiscountRepository? discounts = null,
        IFiscalService? fiscal = null,
        ILoyaltyRepository? loyalty = null,
        ICustomerRepository? customers = null)
    {
        return new PosService(
            pos ?? new FakePosRepo(),
            stock ?? new FakeStockRepo(),
            catalog ?? new FakeCatalogRepo(),
            discounts ?? new FakeDiscountRepo(),
            new FakeFiscalFactory(fiscal ?? new NoopFiscalService()),
            loyalty ?? new FakeLoyaltyRepo(),
            customers ?? new FakeCustomerRepo(),
            NullLogger<PosService>.Instance);
    }

    private static Item MakeProduct(string barcode = "123", decimal price = 10m) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Barcodes = [barcode],
            Name = "Тестовий товар",
            PriceRetail = price,
        };

    private static ProductStock MakeBatch(Guid productId, decimal qty, int daysUntilExpiry = 30) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProductId = productId,
            StoreId = StoreId,
            Quantity = qty,
            QuantityInitial = qty,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysUntilExpiry)),
            Status = "safe",
        };

    // ── Open Shift ─────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenShift_creates_shift_in_repo()
    {
        var pos = new FakePosRepo();
        var svc = BuildService(pos: pos);

        var (shift, error, _) = await svc.OpenShiftAsync(TenantId, CashierId,
            new OpenShiftRequest(StoreId, 500m));

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.Equal(StoreId, shift.StoreId);
        Assert.Single(pos.Shifts);
    }

    [Fact]
    public async Task OpenShift_returns_409_when_shift_already_open()
    {
        var pos = new FakePosRepo();
        pos.Shifts.Add(new PosShift { TenantId = TenantId, StoreId = StoreId });
        var svc = BuildService(pos: pos);

        var (shift, error, statusCode) = await svc.OpenShiftAsync(TenantId, CashierId,
            new OpenShiftRequest(StoreId));

        Assert.Null(shift);
        Assert.Equal(409, statusCode);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task OpenShift_fiscal_failure_still_returns_shift()
    {
        // Noop provider returns PendingFiscalization — shift still persisted
        var pos = new FakePosRepo();
        var svc = BuildService(pos: pos, fiscal: new NoopFiscalService());

        var (shift, error, _) = await svc.OpenShiftAsync(TenantId, CashierId,
            new OpenShiftRequest(StoreId));

        Assert.Null(error);
        Assert.NotNull(shift);
        // fiscal status with noop → local_only (no provider shift id)
        Assert.Equal("local_only", shift.FiscalStatus);
    }

    // ── Close Shift ────────────────────────────────────────────────────────

    [Fact]
    public async Task CloseShift_returns_404_when_no_open_shift()
    {
        var svc = BuildService();

        var (_, error, statusCode) = await svc.CloseShiftAsync(TenantId);

        Assert.Equal(404, statusCode);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task CloseShift_sets_ClosedAt()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(existing);
        var svc = BuildService(pos: pos);

        var (shift, error, _) = await svc.CloseShiftAsync(TenantId);

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.NotNull(existing.ClosedAt);
    }

    [Fact]
    public async Task CloseShift_without_cash_count_leaves_reconciliation_null()
    {
        // Backward compat: omitting the body (or ActualClosingCash) closes exactly as
        // before TASK-356 — no reconciliation performed.
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId, OpeningCash = 500m };
        pos.Shifts.Add(existing);
        var svc = BuildService(pos: pos);

        var (shift, error, _) = await svc.CloseShiftAsync(TenantId, request: null);

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.Null(shift.ClosingCash);
        Assert.Null(shift.ExpectedCashAmount);
        Assert.Null(shift.CashDiscrepancy);
    }

    // ── Close Shift — cash reconciliation (TASK-356) ─────────────────────────

    [Fact]
    public async Task CloseShift_cash_count_matches_expected_zero_discrepancy()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId, OpeningCash = 500m };
        pos.Shifts.Add(existing);
        // Cash sale 300 + card sale 700 — card must NOT count toward expected cash.
        pos.Transactions.Add(new PosTransaction { TenantId = TenantId, ShiftId = existing.Id, PaymentType = "cash", TotalAmount = 300m, ReceiptNumber = "R-1" });
        pos.Transactions.Add(new PosTransaction { TenantId = TenantId, ShiftId = existing.Id, PaymentType = "card", TotalAmount = 700m, ReceiptNumber = "R-2" });
        var svc = BuildService(pos: pos);

        // Expected = OpeningCash(500) + cash sales(300) = 800
        var (shift, error, _) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(800m));

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.Equal(800m, shift.ExpectedCashAmount);
        Assert.Equal(800m, shift.ClosingCash);
        Assert.Equal(0m, shift.CashDiscrepancy);
    }

    [Fact]
    public async Task CloseShift_cash_count_shortage_returns_negative_discrepancy()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId, OpeningCash = 500m };
        pos.Shifts.Add(existing);
        pos.Transactions.Add(new PosTransaction { TenantId = TenantId, ShiftId = existing.Id, PaymentType = "cash", TotalAmount = 300m, ReceiptNumber = "R-1" });
        var svc = BuildService(pos: pos);

        // Expected = 500 + 300 = 800; cashier counted only 750 — 50 missing.
        var (shift, error, _) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(750m));

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.Equal(800m, shift.ExpectedCashAmount);
        Assert.Equal(750m, shift.ClosingCash);
        Assert.Equal(-50m, shift.CashDiscrepancy);
    }

    [Fact]
    public async Task CloseShift_cash_count_surplus_returns_positive_discrepancy()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId, OpeningCash = 500m };
        pos.Shifts.Add(existing);
        pos.Transactions.Add(new PosTransaction { TenantId = TenantId, ShiftId = existing.Id, PaymentType = "cash", TotalAmount = 300m, ReceiptNumber = "R-1" });
        var svc = BuildService(pos: pos);

        // Expected = 500 + 300 = 800; cashier counted 820 — 20 extra.
        var (shift, error, _) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(820m));

        Assert.Null(error);
        Assert.NotNull(shift);
        Assert.Equal(800m, shift.ExpectedCashAmount);
        Assert.Equal(20m, shift.CashDiscrepancy);
    }

    [Fact]
    public async Task CloseShift_negative_cash_count_returns_400()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(existing);
        var svc = BuildService(pos: pos);

        var (shift, error, statusCode) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(-1m));

        Assert.Null(shift);
        Assert.Equal(400, statusCode);
        Assert.NotNull(error);
        // Shift must stay open — validation failed before anything was persisted.
        Assert.Null(existing.ClosedAt);
    }

    [Fact]
    public async Task CloseShift_already_closed_shift_returns_404_on_second_attempt()
    {
        var pos = new FakePosRepo();
        var existing = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(existing);
        var svc = BuildService(pos: pos);

        var (firstShift, firstError, _) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(0m));
        Assert.Null(firstError);
        Assert.NotNull(firstShift);

        var (secondShift, secondError, statusCode) = await svc.CloseShiftAsync(TenantId, new CloseShiftRequest(0m));

        Assert.Null(secondShift);
        Assert.Equal(404, statusCode);
        Assert.NotNull(secondError);
    }

    // ── Get Current Shift ──────────────────────────────────────────────────

    [Fact]
    public async Task GetCurrentShift_returns_null_when_no_open_shift()
    {
        var svc = BuildService();
        var result = await svc.GetCurrentShiftAsync(TenantId);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentShift_returns_open_shift()
    {
        var pos = new FakePosRepo();
        pos.Shifts.Add(new PosShift { TenantId = TenantId, StoreId = StoreId });
        var svc = BuildService(pos: pos);

        var result = await svc.GetCurrentShiftAsync(TenantId);

        Assert.NotNull(result);
        Assert.Equal(StoreId, result.StoreId);
    }

    // ── Create Sale ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSale_returns_409_when_shift_not_found()
    {
        var svc = BuildService();

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(
                ShiftId: Guid.NewGuid(),
                Items: [new SaleItemRequest("123", 1)],
                PaymentType: "Cash",
                PaymentAmount: 10));

        Assert.Equal(409, statusCode);
        Assert.Null(sale);
    }

    [Fact]
    public async Task CreateSale_returns_409_when_shift_belongs_to_different_tenant()
    {
        var pos = new FakePosRepo();
        var otherTenantShift = new PosShift
        {
            TenantId = Guid.NewGuid(), // different tenant
            StoreId = StoreId,
        };
        pos.Shifts.Add(otherTenantShift);
        var svc = BuildService(pos: pos);

        var (_, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(otherTenantShift.Id, [new SaleItemRequest("123", 1)], "Cash", 10));

        Assert.Equal(409, statusCode);
    }

    [Fact]
    public async Task CreateSale_returns_400_when_barcode_not_found()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);
        var catalog = new FakeCatalogRepo(); // empty
        var svc = BuildService(pos: pos, catalog: catalog);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("UNKNOWN-BAR", 1)], "Cash", 10));

        Assert.Equal(400, statusCode);
        Assert.Contains("UNKNOWN-BAR", error);
    }

    [Fact]
    public async Task CreateSale_returns_423_when_all_batches_expired()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("EXPIRED_BAR");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        // All batches expired (daysUntilExpiry = -1)
        stock.Batches.Add(new ProductStock
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProductId = product.Id,
            StoreId = StoreId,
            Quantity = 5,
            QuantityInitial = 5,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            Status = "expired",
        });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("EXPIRED_BAR", 1)], "Cash", 10));

        Assert.Equal(423, statusCode);
        Assert.NotNull(error);
        Assert.Contains("expired", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_returns_400_when_insufficient_stock()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("LOW_STOCK");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 2)); // only 2 available

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("LOW_STOCK", 5)], "Cash", 50));

        Assert.Equal(400, statusCode);
        Assert.Contains("Insufficient stock", error);
    }

    [Fact]
    public async Task CreateSale_applies_fefo_order()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("FEFO_TEST");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        // Batch A: expires in 30 days, qty 3
        var batchA = MakeBatch(product.Id, qty: 3, daysUntilExpiry: 30);
        // Batch B: expires in 5 days (nearer!), qty 10 — FEFO should consume this first
        var batchB = MakeBatch(product.Id, qty: 10, daysUntilExpiry: 5);
        stock.Batches.Add(batchA);
        stock.Batches.Add(batchB);

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("FEFO_TEST", 2)], "Cash", 20));

        Assert.Null(error);
        Assert.NotNull(sale);

        // Batch B (nearest expiry) should be consumed first → its qty drops by 2
        Assert.Equal(8, batchB.Quantity); // consumed 2 from batchB
        Assert.Equal(3, batchA.Quantity); // batchA untouched
    }

    [Fact]
    public async Task CreateSale_fefo_spans_multiple_batches()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("MULTI_BATCH");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        var batchNear = MakeBatch(product.Id, qty: 3, daysUntilExpiry: 5);  // consumed first
        var batchFar = MakeBatch(product.Id, qty: 5, daysUntilExpiry: 30);  // remainder from here
        stock.Batches.Add(batchNear);
        stock.Batches.Add(batchFar);

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("MULTI_BATCH", 5)], "Cash", 50));

        Assert.Null(error);
        // 3 consumed from near batch (now 0), 2 from far batch (now 3)
        Assert.Equal(0, batchNear.Quantity);
        Assert.Equal(3, batchFar.Quantity);
    }

    [Fact]
    public async Task CreateSale_calculates_totals_correctly()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("TOTALS_TEST", price: 25m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("TOTALS_TEST", 3)], "Cash", 100m));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(75m, sale.Subtotal);       // 3 × 25
        Assert.Equal(25m, sale.Change);          // 100 - 75
    }

    [Fact]
    public async Task CreateSale_creates_stock_events_type_pos_sale()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("EVENT_TEST");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("EVENT_TEST", 2)], "Cash", 20));

        Assert.Contains(pos.Events, e => e.EventType == "pos_sale");
        Assert.All(pos.Events, e => Assert.Equal("pos_sale", e.EventType));
    }

    [Fact]
    public async Task CreateSale_fiscal_failure_does_not_block_sale()
    {
        // Noop = no fiscal provider → sale still committed
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("FISCAL_FAIL");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 5));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, fiscal: new NoopFiscalService());

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("FISCAL_FAIL", 1)], "Cash", 10));

        Assert.Null(error);
        Assert.NotNull(sale);
        // Status starts as pending_fiscalization (fiscal runs async in background)
        Assert.Equal("pending_fiscalization", sale.FiscalStatus);
        Assert.Null(sale.FiscalNumber);
    }

    /// <summary>
    /// TASK-356: before this fix, online fiscalization ran on a detached, un-awaited
    /// Task.Run — the response was always built and returned before that background task
    /// could possibly complete, so a successful fiscal outcome could never be reflected in
    /// the SaleDto (untestable with a synchronous unit test, by construction). Now that the
    /// attempt is inline and awaited, a fast/successful provider response is visible
    /// directly in the returned SaleDto — this is the regression guard for that fix.
    /// </summary>
    [Fact]
    public async Task CreateSale_successful_online_fiscalization_is_reflected_in_response()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("FISCAL_OK");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 5));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, fiscal: new FakeSuccessfulFiscalService());

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("FISCAL_OK", 1)], "Cash", 10));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal("fiscalized", sale.FiscalStatus);
        Assert.Equal("FN-12345", sale.FiscalNumber);
    }

    /// <summary>
    /// TASK-356: ProductStock now carries an xmin optimistic-concurrency token so two
    /// sales racing on the same batch can't silently oversell (see the real-Postgres
    /// PosConcurrencySalesIntegrationTests for the end-to-end proof). This test pins the
    /// service-layer half of that fix in isolation: PosService must translate a
    /// ConcurrencyConflictException raised from the final SaveChangesAsync into a clean
    /// 409 instead of letting it propagate as an unhandled exception (which would surface
    /// as a raw 500 to the cashier with no actionable message).
    /// </summary>
    [Fact]
    public async Task CreateSale_concurrency_conflict_on_commit_returns_409()
    {
        var pos = new FakePosRepo { ThrowConcurrencyOnSaveCall = 1 };
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("CONFLICT_TEST");
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 1));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("CONFLICT_TEST", 1)], "Cash", 10));

        Assert.Null(sale);
        Assert.Equal(409, statusCode);
        Assert.NotNull(error);
        // Only the one (conflicting) SaveChangesAsync call happened — the method returned
        // immediately on the conflict instead of proceeding to attempt fiscalization.
        Assert.Equal(1, pos.SaveCount);
    }

    [Fact]
    public async Task CreateSale_returns_400_on_invalid_payment_type()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);
        var svc = BuildService(pos: pos);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("123", 1)], "Bitcoin", 10));

        Assert.Equal(400, statusCode);
        Assert.Contains("PaymentType", error);
    }

    [Fact]
    public async Task CreateSale_applies_auto_discount_for_critical_product()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("CRITICAL_PROD", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        // Batch is critical (expires in 4 days) — within CriticalDays=6 threshold
        var critBatch = new ProductStock
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            ProductId = product.Id,
            StoreId = StoreId,
            Quantity = 10,
            QuantityInitial = 10,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(4)),
            Status = "critical",
        };
        stock.Batches.Add(critBatch);

        // Auto-discount of 20%: discounted price = 80
        var discount = Discount.Create(
            tenantId: TenantId,
            productId: product.Id,
            storeId: StoreId,
            discountPercent: 20m,
            reason: "expiry",
            priceOriginal: 100m,
            autoApplied: true);
        discount.Approve(CashierId);

        var discounts = new FakeDiscountRepo();
        discounts.Discounts.Add(discount);

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, discounts: discounts);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("CRITICAL_PROD", 1)], "Cash", 100));

        Assert.Null(error);
        Assert.NotNull(sale);

        var item = Assert.Single(sale.Items);
        Assert.Equal(20m, item.DiscountAmount);     // 20% of 100
        Assert.Equal(80m, item.Total);              // 100 - 20
    }

    // ── Create Sale — Loyalty (TASK-405) ──────────────────────────────────

    [Fact]
    public async Task CreateSale_without_loyalty_fields_behaves_exactly_as_before()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("NO_LOYALTY", price: 20m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 5));

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("NO_LOYALTY", 1)], "Cash", 20m));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(20m, sale.Subtotal);
        Assert.Null(sale.LoyaltyAccrued);
        Assert.Null(sale.LoyaltyRedeemed);
        Assert.Null(sale.LoyaltyBalance);
    }

    [Fact]
    public async Task CreateSale_with_customerId_updates_customer_aggregates()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("CUST_AGG", price: 50m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var customer = new Customer { TenantId = TenantId, Name = "Ірина", TotalOrders = 2, TotalSpent = 100m };
        var customers = new FakeCustomerRepo();
        customers.Customers.Add(customer);

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, customers: customers);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("CUST_AGG", 1)], "Cash", 50m, CustomerId: customer.Id));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(3, customer.TotalOrders);
        Assert.Equal(150m, customer.TotalSpent);
    }

    [Fact]
    public async Task CreateSale_with_unknown_customerId_returns_400()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);
        var svc = BuildService(pos: pos);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("123", 1)], "Cash", 10, CustomerId: Guid.NewGuid()));

        Assert.Equal(400, statusCode);
        Assert.Contains("Customer", error);
    }

    [Fact]
    public async Task CreateSale_with_loyalty_membership_accrues_bonus()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("ACCRUAL", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 0m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = true, AccrualRatePercent = 10m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("ACCRUAL", 1)], "Cash", 100m, LoyaltyMembershipId: membership.Id));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(10m, sale.LoyaltyAccrued);
        Assert.Equal(10m, membership.Balance);
        Assert.Equal(10m, sale.LoyaltyBalance);
        Assert.Single(loyalty.LedgerEntries);
        Assert.Equal(LoyaltyEntryType.Accrual, loyalty.LedgerEntries[0].EntryType);
    }

    [Fact]
    public async Task CreateSale_with_redemption_reduces_total_and_records_ledger()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("REDEEM", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 50m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings
        {
            TenantId = TenantId, IsEnabled = true, AccrualRatePercent = 0m, RedemptionCapPercent = 50m,
        });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("REDEEM", 1)], "Cash", 80m,
                LoyaltyMembershipId: membership.Id, RedeemAmount: 20m));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Equal(80m, sale.Subtotal);       // 100 - 20 redeemed
        Assert.Equal(20m, sale.LoyaltyRedeemed);
        Assert.Equal(30m, membership.Balance);  // 50 - 20
        Assert.Single(loyalty.LedgerEntries);
        Assert.Equal(LoyaltyEntryType.Redemption, loyalty.LedgerEntries[0].EntryType);
        Assert.Equal(-20m, loyalty.LedgerEntries[0].Amount);
    }

    [Fact]
    public async Task CreateSale_redemption_over_cap_returns_400()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("OVERCAP", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 100m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = true, RedemptionCapPercent = 50m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        // Cap is 50% of 100 = 50; trying to redeem 60 must fail.
        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("OVERCAP", 1)], "Cash", 40m,
                LoyaltyMembershipId: membership.Id, RedeemAmount: 60m));

        Assert.Equal(400, statusCode);
        Assert.Contains("cap", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100m, membership.Balance); // unchanged
        Assert.Empty(loyalty.LedgerEntries);
    }

    [Fact]
    public async Task CreateSale_redemption_over_balance_returns_400()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("OVERBALANCE", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 5m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = true, RedemptionCapPercent = 100m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("OVERBALANCE", 1)], "Cash", 90m,
                LoyaltyMembershipId: membership.Id, RedeemAmount: 10m));

        Assert.Equal(400, statusCode);
        Assert.Contains("Insufficient loyalty balance", error);
    }

    [Fact]
    public async Task CreateSale_blocked_membership_returns_400()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var membership = new LoyaltyMembership { TenantId = TenantId, Status = LoyaltyMembershipStatus.Blocked };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);

        var svc = BuildService(pos: pos, loyalty: loyalty);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("123", 1)], "Cash", 10, LoyaltyMembershipId: membership.Id));

        Assert.Equal(400, statusCode);
        Assert.Contains("blocked", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSale_redeemAmount_without_membershipId_returns_400()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);
        var svc = BuildService(pos: pos);

        var (sale, error, statusCode) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("123", 1)], "Cash", 10, RedeemAmount: 5m));

        Assert.Equal(400, statusCode);
        Assert.Contains("LoyaltyMembershipId", error);
    }

    [Fact]
    public async Task CreateSale_loyalty_disabled_by_settings_skips_accrual_but_still_sells()
    {
        var pos = new FakePosRepo();
        var shift = new PosShift { TenantId = TenantId, StoreId = StoreId };
        pos.Shifts.Add(shift);

        var product = MakeProduct("DISABLED_PROGRAM", price: 100m);
        var catalog = new FakeCatalogRepo();
        catalog.Products.Add(product);

        var stock = new FakeStockRepo();
        stock.Batches.Add(MakeBatch(product.Id, qty: 10));

        var membership = new LoyaltyMembership { TenantId = TenantId, Balance = 0m, Status = LoyaltyMembershipStatus.Active };
        var loyalty = new FakeLoyaltyRepo();
        loyalty.Memberships.Add(membership);
        loyalty.Settings.Add(new LoyaltyProgramSettings { TenantId = TenantId, IsEnabled = false, AccrualRatePercent = 10m });

        var svc = BuildService(pos: pos, stock: stock, catalog: catalog, loyalty: loyalty);

        var (sale, error, _) = await svc.CreateSaleAsync(TenantId, CashierId,
            new CreateSaleRequest(shift.Id, [new SaleItemRequest("DISABLED_PROGRAM", 1)], "Cash", 100m,
                LoyaltyMembershipId: membership.Id));

        Assert.Null(error);
        Assert.NotNull(sale);
        Assert.Null(sale.LoyaltyAccrued);
        Assert.Equal(0m, membership.Balance);
        Assert.Empty(loyalty.LedgerEntries);
    }

    // ── List Sales ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSalesForShift_returns_empty_list_when_no_transactions()
    {
        var svc = BuildService();
        var result = await svc.GetSalesForShiftAsync(TenantId, Guid.NewGuid());
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalAmount);
    }

    // ── List Sales — Customer + Loyalty mapping (TASK-410) ─────────────────

    [Fact]
    public async Task GetSalesForShift_maps_customer_and_leaves_loyalty_null_with_no_ledger_activity()
    {
        var pos = new FakePosRepo();
        var shiftId = Guid.NewGuid();
        var customer = new Customer { TenantId = TenantId, Name = "Ірина" };
        var tx = new PosTransaction
        {
            TenantId = TenantId,
            ShiftId = shiftId,
            PaymentType = "cash",
            TotalAmount = 100m,
            ReceiptNumber = "R-1",
            CustomerId = customer.Id,
            Customer = customer, // FakePosRepo doesn't run EF Include — set directly.
        };
        pos.Transactions.Add(tx);

        var svc = BuildService(pos: pos);
        var result = await svc.GetSalesForShiftAsync(TenantId, shiftId);

        var sale = Assert.Single(result.Items);
        Assert.Equal(customer.Id, sale.CustomerId);
        Assert.Equal("Ірина", sale.CustomerName);
        Assert.Null(sale.LoyaltyAccrued);
        Assert.Null(sale.LoyaltyRedeemed);
        Assert.Null(sale.LoyaltyBalance);
    }

    [Fact]
    public async Task GetSalesForShift_maps_accrual_only_ledger_entry()
    {
        var pos = new FakePosRepo();
        var shiftId = Guid.NewGuid();
        var tx = new PosTransaction
        {
            TenantId = TenantId, ShiftId = shiftId, PaymentType = "cash",
            TotalAmount = 100m, ReceiptNumber = "R-1",
        };
        pos.Transactions.Add(tx);

        var loyalty = new FakeLoyaltyRepo();
        loyalty.LedgerEntries.Add(new LoyaltyLedgerEntry
        {
            TenantId = TenantId,
            MembershipId = Guid.NewGuid(),
            EntryType = LoyaltyEntryType.Accrual,
            Amount = 10m,
            BalanceAfter = 40m,
            PosTransactionId = tx.Id,
        });

        var svc = BuildService(pos: pos, loyalty: loyalty);
        var result = await svc.GetSalesForShiftAsync(TenantId, shiftId);

        var sale = Assert.Single(result.Items);
        Assert.Equal(10m, sale.LoyaltyAccrued);
        Assert.Null(sale.LoyaltyRedeemed);
        Assert.Equal(40m, sale.LoyaltyBalance);
    }

    [Fact]
    public async Task GetSalesForShift_balance_reflects_last_ledger_entry_when_both_redemption_and_accrual_exist()
    {
        var pos = new FakePosRepo();
        var shiftId = Guid.NewGuid();
        var tx = new PosTransaction
        {
            TenantId = TenantId, ShiftId = shiftId, PaymentType = "cash",
            TotalAmount = 80m, ReceiptNumber = "R-1",
        };
        pos.Transactions.Add(tx);

        // Mirrors CreateSaleAsync's write order: redemption entry persisted first, accrual
        // second — explicit CreatedAt values (rather than relying on two back-to-back
        // DateTimeOffset.UtcNow calls) so the "last entry wins" ordering is deterministic.
        var membershipId = Guid.NewGuid();
        var loyalty = new FakeLoyaltyRepo();
        var now = DateTimeOffset.UtcNow;
        loyalty.LedgerEntries.Add(new LoyaltyLedgerEntry
        {
            TenantId = TenantId,
            MembershipId = membershipId,
            EntryType = LoyaltyEntryType.Redemption,
            Amount = -20m,
            BalanceAfter = 30m,
            PosTransactionId = tx.Id,
            CreatedAt = now,
        });
        loyalty.LedgerEntries.Add(new LoyaltyLedgerEntry
        {
            TenantId = TenantId,
            MembershipId = membershipId,
            EntryType = LoyaltyEntryType.Accrual,
            Amount = 8m,
            BalanceAfter = 38m,
            PosTransactionId = tx.Id,
            CreatedAt = now.AddMilliseconds(1),
        });

        var svc = BuildService(pos: pos, loyalty: loyalty);
        var result = await svc.GetSalesForShiftAsync(TenantId, shiftId);

        var sale = Assert.Single(result.Items);
        Assert.Equal(8m, sale.LoyaltyAccrued);
        Assert.Equal(20m, sale.LoyaltyRedeemed); // positive, sign flipped from the stored -20m
        Assert.Equal(38m, sale.LoyaltyBalance);  // last entry (accrual) wins, not redemption's 30m
    }
}
