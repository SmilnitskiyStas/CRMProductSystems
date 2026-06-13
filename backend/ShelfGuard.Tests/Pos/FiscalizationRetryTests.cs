using Microsoft.Extensions.Logging.Abstractions;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using Xunit;

namespace ShelfGuard.Tests.Pos;

// ── Fakes shared with this test file ─────────────────────────────────────────

/// <summary>
/// Configurable fake IFiscalService for retry tests: either returns Done or throws.
/// </summary>
file sealed class ConfigurableFiscalService : IFiscalService
{
    private readonly bool _succeeds;
    private readonly string? _fiscalNumber;

    public ConfigurableFiscalService(bool succeeds, string? fiscalNumber = "FISCAL-001")
    {
        _succeeds = succeeds;
        _fiscalNumber = fiscalNumber;
    }

    public Task<FiscalReceiptResult> CreateReceiptAsync(FiscalReceiptRequest request, CancellationToken ct = default)
    {
        if (!_succeeds)
            throw new FiscalProviderException("Checkbox", "provider unavailable");

        return Task.FromResult(new FiscalReceiptResult(
            ProviderReceiptId: "prov-001",
            Status: FiscalReceiptStatus.Done,
            FiscalNumber: _fiscalNumber,
            FiscalDate: DateTimeOffset.UtcNow,
            TotalAmount: 100m,
            TaxUrl: null));
    }

    public Task<FiscalHealthResult> PingAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalHealthResult(true, "test", null, null, null, null));

    public Task<FiscalCashierResult> CheckCashierAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalCashierResult(true, "Test Cashier", null));

    public Task<FiscalShiftResult> OpenShiftAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult(null, FiscalShiftStatus.PendingFiscalization, null, null, null));

    public Task<FiscalShiftResult> CloseShiftAsync(CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult(null, FiscalShiftStatus.PendingFiscalization, null, null, null));

    public Task<FiscalShiftResult> GetShiftStatusAsync(string providerShiftId, CancellationToken ct = default) =>
        Task.FromResult(new FiscalShiftResult(providerShiftId, FiscalShiftStatus.PendingFiscalization, null, null, null));

    public Task<FiscalReceiptResult> GetReceiptStatusAsync(string providerReceiptId, CancellationToken ct = default) =>
        Task.FromResult(new FiscalReceiptResult(
            ProviderReceiptId: providerReceiptId,
            Status: _succeeds ? FiscalReceiptStatus.Done : FiscalReceiptStatus.Error,
            FiscalNumber: _succeeds ? _fiscalNumber : null,
            FiscalDate: null, TotalAmount: null, TaxUrl: null));
}

file sealed class RetryFakePosRepo : IPosRepository
{
    public List<PosTransaction> Transactions { get; } = [];

    public Task<PosTransaction?> GetOpenShiftAsync_NotUsed(Guid tenantId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<PosShift?> GetOpenShiftAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<PosShift?>(null);

    public Task<PosShift?> GetShiftByIdAsync(Guid shiftId, CancellationToken ct = default) =>
        Task.FromResult<PosShift?>(null);

    public Task AddShiftAsync(PosShift shift, CancellationToken ct = default) => Task.CompletedTask;
    public void UpdateShift(PosShift shift) { }

    public Task<PosTransaction?> GetTransactionByIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Transactions.FirstOrDefault(t => t.Id == id));

    public Task<List<PosTransaction>> GetTransactionsByShiftAsync(Guid shiftId, CancellationToken ct = default) =>
        Task.FromResult(new List<PosTransaction>());

    public Task AddTransactionAsync(PosTransaction tx, CancellationToken ct = default)
    {
        Transactions.Add(tx);
        return Task.CompletedTask;
    }

    public void UpdateTransaction(PosTransaction tx) { }

    public Task AddStockEventAsync(StockEvent ev, CancellationToken ct = default) => Task.CompletedTask;

    public Task<List<PosTransaction>> GetPendingFiscalizationAsync(
        int maxRetries, DateTime createdBefore, CancellationToken ct = default) =>
        Task.FromResult(Transactions
            .Where(t =>
                t.Status == "pending_fiscalization" &&
                t.RetryCount < maxRetries &&
                t.CreatedAt < createdBefore)
            .ToList());

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class RetryFakeStockRepo : IStockRepository
{
    public Task<List<ProductStock>> GetFefoOrderedAsync(Guid p, Guid s, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetAllAsync(Guid? storeId, string? status, Guid? zoneId, Guid? productId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public void Update(ProductStock stock) { }
    public Task<ProductStock?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<ProductStock?>(null);
    public Task<List<ProductStock>> GetExpiringAsync(Guid? storeId, int days, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetExpiredAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetNeedsCheckAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetActionRequiredAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<ProductStock>> GetDeficitStocksAsync(Guid productId, Guid excludeStoreId, CancellationToken ct = default) => Task.FromResult(new List<ProductStock>());
    public Task<List<Store>> GetProductionStoresAsync(CancellationToken ct = default) => Task.FromResult(new List<Store>());
    public Task<Dictionary<string, int>> GetStatusCountsAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, int>());
    public Task AddAsync(ProductStock stock, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class RetryFakeCatalogRepo : ICatalogProductRepository
{
    public Task<CatalogProduct?> GetByBarcodeAsync(string barcode, CancellationToken ct = default) => Task.FromResult<CatalogProduct?>(null);
    public Task<List<CatalogProduct>> GetAllAsync(Guid? c, Guid? s, string? m, CancellationToken ct = default) => Task.FromResult(new List<CatalogProduct>());
    public Task<CatalogProduct?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<CatalogProduct?>(null);
    public Task<List<ProductSupplierSetting>> GetSupplierSettingsAsync(Guid productId, CancellationToken ct = default) => Task.FromResult(new List<ProductSupplierSetting>());
    public Task<bool> SupplierSettingExistsAsync(Guid productId, Guid supplierId, CancellationToken ct = default) => Task.FromResult(false);
    public Task AddAsync(CatalogProduct product, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddSupplierSettingAsync(ProductSupplierSetting setting, CancellationToken ct = default) => Task.CompletedTask;
    public void Update(CatalogProduct product) { }
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class RetryFakeDiscountRepo : IDiscountRepository
{
    public Task<IReadOnlyList<Discount>> GetAllAsync(Guid tenantId, Guid? storeId = null, string? status = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Discount>>(new List<Discount>());
    public Task<Discount?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Discount?>(null);
    public Task AddAsync(Discount discount, CancellationToken ct = default) => Task.CompletedTask;
    public void Update(Discount discount) { }
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class RetryFakeFiscalFactory : IFiscalServiceFactory
{
    private readonly IFiscalService _service;
    public PrroConnectionConfig? EnvFallback => null;
    public RetryFakeFiscalFactory(IFiscalService service) => _service = service;
    public Task<IFiscalService> GetForTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(_service);
    public IFiscalService Create(Guid tenantId, PrroConnectionConfig config) => _service;
}

// ── Tests ──────────────────────────────────────────────────────────────────

public sealed class FiscalizationRetryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static PosService BuildService(
        IPosRepository posRepo,
        IFiscalService? fiscal = null) =>
        new PosService(
            posRepo,
            new RetryFakeStockRepo(),
            new RetryFakeCatalogRepo(),
            new RetryFakeDiscountRepo(),
            new RetryFakeFiscalFactory(fiscal ?? new NoopFiscalService()),
            NullLogger<PosService>.Instance);

    private static PosTransaction MakePendingTx(int retryCount = 0, int ageSeconds = 60) =>
        new PosTransaction
        {
            TenantId = TenantId,
            StoreId = Guid.NewGuid(),
            ShiftId = Guid.NewGuid(),
            ReceiptNumber = $"R-{Guid.NewGuid():N}",
            Status = "pending_fiscalization",
            RetryCount = retryCount,
            TotalAmount = 100m,
            TaxAmount = 16.67m,
            PaymentType = "cash",
            CreatedAt = DateTime.UtcNow.AddSeconds(-ageSeconds),
        };

    // ── FiscalizeTransactionAsync ───────────────────────────────────────────

    [Fact]
    public async Task FiscalizeTransaction_returns_not_found_for_unknown_id()
    {
        var repo = new RetryFakePosRepo();
        var svc = BuildService(repo);

        var result = await svc.FiscalizeTransactionAsync(Guid.NewGuid());

        Assert.False(result.Ok);
        Assert.Equal("Transaction not found.", result.Error);
    }

    [Fact]
    public async Task FiscalizeTransaction_success_sets_fiscalized_status()
    {
        var repo = new RetryFakePosRepo();
        var tx = MakePendingTx();
        await repo.AddTransactionAsync(tx);

        var svc = BuildService(repo, fiscal: new ConfigurableFiscalService(succeeds: true, "FISCAL-TEST-001"));

        var result = await svc.FiscalizeTransactionAsync(tx.Id);

        Assert.True(result.Ok);
        Assert.Equal("FISCAL-TEST-001", result.FiscalNumber);
        Assert.Equal("fiscalized", tx.Status);
        Assert.Equal(1, tx.RetryCount);
    }

    [Fact]
    public async Task FiscalizeTransaction_failure_increments_retry_count()
    {
        var repo = new RetryFakePosRepo();
        var tx = MakePendingTx(retryCount: 0);
        await repo.AddTransactionAsync(tx);

        var svc = BuildService(repo, fiscal: new ConfigurableFiscalService(succeeds: false));

        var result = await svc.FiscalizeTransactionAsync(tx.Id);

        Assert.False(result.Ok);
        Assert.Equal(1, tx.RetryCount);                  // incremented
        Assert.Equal("pending_fiscalization", tx.Status); // stays pending (not yet at limit)
    }

    [Fact]
    public async Task FiscalizeTransaction_at_max_retries_sets_fiscalization_failed()
    {
        var repo = new RetryFakePosRepo();
        // RetryCount = 4 → after this attempt it becomes 5 → limit hit
        var tx = MakePendingTx(retryCount: 4);
        await repo.AddTransactionAsync(tx);

        var svc = BuildService(repo, fiscal: new ConfigurableFiscalService(succeeds: false));

        var result = await svc.FiscalizeTransactionAsync(tx.Id);

        Assert.False(result.Ok);
        Assert.Equal(5, tx.RetryCount);
        Assert.Equal("fiscalization_failed", tx.Status);
    }

    [Fact]
    public async Task FiscalizeTransaction_already_fiscalized_returns_ok_without_re_attempting()
    {
        var repo = new RetryFakePosRepo();
        var tx = MakePendingTx();
        tx.Status = "fiscalized";
        tx.FiscalNumber = "ALREADY-DONE";
        await repo.AddTransactionAsync(tx);

        var svc = BuildService(repo, fiscal: new ConfigurableFiscalService(succeeds: true));

        var result = await svc.FiscalizeTransactionAsync(tx.Id);

        // Returns ok immediately, does not increment retry count
        Assert.True(result.Ok);
        Assert.Equal("ALREADY-DONE", result.FiscalNumber);
        Assert.Equal(0, tx.RetryCount); // not incremented
    }

    // ── GetPendingFiscalizationAsync ────────────────────────────────────────

    [Fact]
    public async Task GetPendingFiscalization_excludes_recent_transactions()
    {
        var repo = new RetryFakePosRepo();
        // Too recent (only 10 s old) — should be excluded
        var recentTx = MakePendingTx(ageSeconds: 10);
        // Old enough (60 s) — should be included
        var oldTx = MakePendingTx(ageSeconds: 60);
        await repo.AddTransactionAsync(recentTx);
        await repo.AddTransactionAsync(oldTx);

        var svc = BuildService(repo);
        var result = await svc.GetPendingFiscalizationAsync(maxRetries: 5, olderThanSeconds: 30);

        Assert.Single(result);
        Assert.Equal(oldTx.Id, result[0].Id);
    }

    [Fact]
    public async Task GetPendingFiscalization_excludes_transactions_at_max_retries()
    {
        var repo = new RetryFakePosRepo();
        var maxed = MakePendingTx(retryCount: 5, ageSeconds: 60);  // at limit
        var ok = MakePendingTx(retryCount: 2, ageSeconds: 60);      // under limit
        await repo.AddTransactionAsync(maxed);
        await repo.AddTransactionAsync(ok);

        var svc = BuildService(repo);
        var result = await svc.GetPendingFiscalizationAsync(maxRetries: 5, olderThanSeconds: 30);

        Assert.Single(result);
        Assert.Equal(ok.Id, result[0].Id);
    }

    [Fact]
    public async Task GetPendingFiscalization_returns_empty_when_none_pending()
    {
        var repo = new RetryFakePosRepo();
        var tx = MakePendingTx(ageSeconds: 60);
        tx.Status = "fiscalized";
        await repo.AddTransactionAsync(tx);

        var svc = BuildService(repo);
        var result = await svc.GetPendingFiscalizationAsync(maxRetries: 5, olderThanSeconds: 30);

        Assert.Empty(result);
    }
}
