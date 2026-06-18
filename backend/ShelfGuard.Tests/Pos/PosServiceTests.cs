using Microsoft.Extensions.Logging.Abstractions;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Domain.Entities;
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
    public Task<Dictionary<Guid, ProductStock?>> GetDeficitStocksBulkAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct = default) =>
        Task.FromResult(productIds.ToDictionary(id => id, _ => (ProductStock?)null));
    public Task<(List<ProductStock> Items, int Total)> GetPagedAsync(Guid? storeId, string? status, Guid? zoneId, Guid? productId, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult((new List<ProductStock>(), 0));
    public Task<List<Location>> GetProductionStoresAsync(CancellationToken ct = default) => Task.FromResult(new List<Location>());
    public Task<Dictionary<string, int>> GetStatusCountsAsync(Guid? storeId, CancellationToken ct = default) => Task.FromResult(new Dictionary<string, int>());
    public Task AddAsync(ProductStock stock, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FakeCatalogRepo : IItemRepository
{
    public List<Item> Products { get; } = [];

    public Task<Item?> GetByBarcodeAsync(string barcode, CancellationToken ct = default) =>
        Task.FromResult(Products.FirstOrDefault(p => p.Barcode == barcode));

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

file sealed class FakeFiscalFactory : IFiscalServiceFactory
{
    private readonly IFiscalService _service;
    public PrroConnectionConfig? EnvFallback => null;
    public FakeFiscalFactory(IFiscalService service) => _service = service;
    public Task<IFiscalService> GetForTenantAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(_service);
    public IFiscalService Create(Guid tenantId, PrroConnectionConfig config) => _service;
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
        IFiscalService? fiscal = null)
    {
        return new PosService(
            pos ?? new FakePosRepo(),
            stock ?? new FakeStockRepo(),
            catalog ?? new FakeCatalogRepo(),
            discounts ?? new FakeDiscountRepo(),
            new FakeFiscalFactory(fiscal ?? new NoopFiscalService()),
            NullLogger<PosService>.Instance);
    }

    private static Item MakeProduct(string barcode = "123", decimal price = 10m) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Barcode = barcode,
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

    // ── List Sales ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSalesForShift_returns_empty_list_when_no_transactions()
    {
        var svc = BuildService();
        var result = await svc.GetSalesForShiftAsync(TenantId, Guid.NewGuid());
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalAmount);
    }
}
