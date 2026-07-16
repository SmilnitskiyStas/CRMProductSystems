using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Pos;

/// <summary>
/// Block 6 pre-launch audit (TASK-356): real-Postgres concurrency test for the exact
/// scenario the audit called for — two cashiers selling the last unit of the same batch
/// at the same moment via <see cref="Task.WhenAll"/>. Uses two independent
/// <see cref="PosService"/> instances, each backed by its own <see cref="AppDbContext"/>
/// (mirrors two separate HTTP requests, each with its own DI scope), against the local
/// dev Postgres. Same soft-skip pattern as
/// <see cref="ShelfGuard.Tests.Infrastructure.RlsCrossTenantIntegrationTests"/> (Block 2):
/// this repo has no WebApplicationFactory harness yet, and the rest of
/// <c>ShelfGuard.Tests.Pos.PosServiceTests</c> uses in-memory fakes that share one mutable
/// list — those cannot exercise a real DB-level race at all (no two separate DbContexts,
/// no real optimistic-concurrency check).
///
/// The race is made deterministic (not timing-luck) via <see cref="GatedStockRepository"/>:
/// cashier A's FEFO stock read releases a gate that cashier B's FEFO stock read waits on,
/// so both are guaranteed to read the same pre-decrement Quantity before either writes —
/// exactly the "read-read-write-write" interleaving that causes a lost update without an
/// optimistic-concurrency token.
///
/// Before TASK-356 (no concurrency token on ProductStock.Quantity), both sales used to
/// succeed with a last-write-wins UPDATE, silently overselling the last unit. After: the
/// loser's SaveChangesAsync throws on the xmin mismatch, PosRepository translates that into
/// ConcurrencyConflictException, and PosService.CreateSaleAsync turns it into a clean 409
/// instead of corrupting Quantity.
///
/// Also incidentally exercises <c>ItemRepository.GetByBarcodeAsync</c> against a real
/// database — this test's first run caught a separate, previously-undiscovered bug there
/// (jsonb `Contains()` translation, fixed alongside this concurrency fix; see
/// ItemRepository.cs). Skips (soft-pass) when no reachable Postgres is configured.
/// Override via env var SHELFGUARD_TEST_DB_CONNECTION.
/// </summary>
public sealed class PosConcurrencySalesIntegrationTests : IAsyncLifetime
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5435;Database=crm;Username=crm;Password=crm_dev_password";

    private readonly ITestOutputHelper _output;
    private string _connectionString = DefaultConnectionString;
    private bool _dbAvailable;

    private Guid _tenantId;
    private Guid _stockId;
    private Guid _shiftId;
    private string _barcode = string.Empty;

    public PosConcurrencySalesIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("SHELFGUARD_TEST_DB_CONNECTION") ?? DefaultConnectionString;

        try
        {
            await using var probe = new NpgsqlConnection(_connectionString);
            await probe.OpenAsync();
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine(
                $"Skipping POS concurrency integration test — no reachable Postgres at '{_connectionString}': {ex.Message}");
            return;
        }

        _barcode = $"CONC-{Guid.NewGuid():N}"[..16];

        await using var db = NewContext();

        var tenant = Tenant.Create($"Concurrency Test {Guid.NewGuid():N}", $"conc-test-{Guid.NewGuid():N}");
        var store = new Location { TenantId = tenant.Id, Name = "Concurrency Test Store" };
        var product = new Item
        {
            TenantId = tenant.Id,
            Name = "Concurrency Test Product",
            Barcodes = [_barcode],
            PriceRetail = 50m,
        };
        var stock = new ProductStock
        {
            TenantId = tenant.Id,
            ProductId = product.Id,
            StoreId = store.Id,
            Quantity = 1m, // exactly one unit left — the scenario under test
            QuantityInitial = 1m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = "safe",
        };
        var shift = new PosShift
        {
            TenantId = tenant.Id,
            StoreId = store.Id,
            OpenedAt = DateTime.UtcNow,
        };

        db.Tenants.Add(tenant);
        db.Locations.Add(store);
        db.Items.Add(product);
        db.ProductStocks.Add(stock);
        db.PosShifts.Add(shift);
        await db.SaveChangesAsync();

        _tenantId = tenant.Id;
        _stockId = stock.Id;
        _shiftId = shift.Id;
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_transaction_items WHERE \"ProductStockId\" = {_stockId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_events WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_transactions WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_shifts WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM product_stock WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [Fact]
    public async Task Two_concurrent_sales_of_the_last_unit_never_oversell()
    {
        if (!_dbAvailable)
        {
            _output.WriteLine("DB not available — skipped.");
            return;
        }

        await using var dbA = NewContext();
        await using var dbB = NewContext();

        // Two-way rendezvous around the FEFO stock read: A reads, then blocks until B has
        // also read; B waits for A to have read, then reads itself and unblocks A. Neither
        // side is allowed to proceed to its write (FEFO decrement + SaveChanges) until BOTH
        // have read the same pre-decrement Quantity — this deterministically reproduces the
        // "two cashiers, last unit" race instead of hoping for timing luck.
        var aHasRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bHasRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var svcA = BuildService(dbA, stock => new RendezvousStockRepository(stock, SignalSelf: aHasRead, WaitFor: bHasRead.Task));
        var svcB = BuildService(dbB, stock => new RendezvousStockRepository(stock, SignalSelf: bHasRead, WaitFor: aHasRead.Task, WaitBeforeRead: true));

        var request = new CreateSaleRequest(
            ShiftId: _shiftId,
            Items: [new SaleItemRequest(_barcode, 1)],
            PaymentType: "Cash",
            PaymentAmount: 50m);

        var taskA = svcA.CreateSaleAsync(_tenantId, Guid.NewGuid(), request);
        var taskB = svcB.CreateSaleAsync(_tenantId, Guid.NewGuid(), request);

        var results = await Task.WhenAll(taskA, taskB);

        _output.WriteLine(string.Join(" | ", results.Select(r =>
            r.Error is null ? $"OK sale={r.Sale!.TransactionId}" : $"ERROR status={r.StatusCode} msg={r.Error}")));

        var successCount = results.Count(r => r.Error is null);
        var conflictCount = results.Count(r => r.StatusCode == 409);

        // Exactly one cashier gets the sale; the other gets a clean 409 to retry —
        // never both succeeding (oversell) and never both failing outright.
        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        await using var verifyDb = NewContext();
        var finalStock = await verifyDb.ProductStocks.AsNoTracking().SingleAsync(s => s.Id == _stockId);

        // Exactly one decrement landed — not 1 (lost update swallowed both writes), not
        // negative (both decrements landed / oversold).
        Assert.Equal(0m, finalStock.Quantity);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private AppDbContext NewContext()
    {
        var dataSource = new NpgsqlDataSourceBuilder(_connectionString).EnableDynamicJson().Build();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource).Options;
        return new AppDbContext(options);
    }

    private static PosService BuildService(AppDbContext db, Func<IStockRepository, IStockRepository> wrapStock) =>
        new(
            new PosRepository(db),
            wrapStock(new StockRepository(db)),
            new ItemRepository(db),
            new DiscountRepository(db),
            new StaticFiscalServiceFactory(new NoopFiscalService()),
            NullLogger<PosService>.Instance);

    private sealed class StaticFiscalServiceFactory : IFiscalServiceFactory
    {
        private readonly IFiscalService _service;
        public StaticFiscalServiceFactory(IFiscalService service) => _service = service;
        public PrroConnectionConfig? EnvFallback => null;
        public Task<IFiscalService> GetForTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_service);
        public IFiscalService Create(Guid tenantId, PrroConnectionConfig config) => _service;
    }

    /// <summary>
    /// Delegates everything to <paramref name="Inner"/>. Wraps the FEFO stock read
    /// (the read PosService.CreateSaleAsync uses to decide availability) in a two-way
    /// rendezvous with the other cashier's instance: whichever side is NOT
    /// <paramref name="WaitBeforeRead"/> reads first, signals <paramref name="SignalSelf"/>,
    /// then blocks on <paramref name="WaitFor"/>; the <paramref name="WaitBeforeRead"/> side
    /// blocks on <paramref name="WaitFor"/> first, then reads, then signals
    /// <paramref name="SignalSelf"/> to release the other side. Net effect: BOTH cashiers
    /// are guaranteed to observe the same pre-decrement Quantity before EITHER is allowed
    /// to proceed to its write — deterministic, not timing-luck.
    /// </summary>
    private sealed class RendezvousStockRepository(
        IStockRepository Inner,
        TaskCompletionSource SignalSelf,
        Task WaitFor,
        bool WaitBeforeRead = false) : IStockRepository
    {
        public async Task<List<ProductStock>> GetFefoOrderedAsync(Guid productId, Guid storeId, CancellationToken ct = default)
        {
            if (WaitBeforeRead)
                await WaitFor;

            var result = await Inner.GetFefoOrderedAsync(productId, storeId, ct);
            SignalSelf.TrySetResult();

            if (!WaitBeforeRead)
                await WaitFor;

            return result;
        }

        public Task<List<ProductStock>> GetAllAsync(Guid? storeId, string? status, Guid? zoneId, Guid? productId, CancellationToken ct = default) =>
            Inner.GetAllAsync(storeId, status, zoneId, productId, ct);
        public void Update(ProductStock stock) => Inner.Update(stock);
        public Task<ProductStock?> GetByIdAsync(Guid id, CancellationToken ct = default) => Inner.GetByIdAsync(id, ct);
        public Task<List<ProductStock>> GetExpiringAsync(Guid? storeId, int days, CancellationToken ct = default) => Inner.GetExpiringAsync(storeId, days, ct);
        public Task<List<ProductStock>> GetExpiredAsync(Guid? storeId, CancellationToken ct = default) => Inner.GetExpiredAsync(storeId, ct);
        public Task<List<ProductStock>> GetNeedsCheckAsync(Guid? storeId, CancellationToken ct = default) => Inner.GetNeedsCheckAsync(storeId, ct);
        public Task<List<ProductStock>> GetActionRequiredAsync(Guid? storeId, CancellationToken ct = default) => Inner.GetActionRequiredAsync(storeId, ct);
        public Task<List<ProductStock>> GetDeficitStocksAsync(Guid productId, Guid excludeStoreId, CancellationToken ct = default) => Inner.GetDeficitStocksAsync(productId, excludeStoreId, ct);
        public Task<Dictionary<Guid, List<ProductStock>>> GetDeficitStocksBulkAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct = default) => Inner.GetDeficitStocksBulkAsync(productIds, ct);
        public Task<(List<ProductStock> Items, int Total)> GetPagedAsync(Guid? storeId, string? status, Guid? zoneId, Guid? productId, int page, int pageSize, CancellationToken ct = default) => Inner.GetPagedAsync(storeId, status, zoneId, productId, page, pageSize, ct);
        public Task<List<Location>> GetProductionStoresAsync(CancellationToken ct = default) => Inner.GetProductionStoresAsync(ct);
        public Task<Dictionary<string, int>> GetStatusCountsAsync(Guid? storeId, CancellationToken ct = default) => Inner.GetStatusCountsAsync(storeId, ct);
        public Task<List<(Guid? ZoneId, string ZoneName, string ZoneType, string Status)>> GetStockByZoneRawAsync(Guid? storeId, CancellationToken ct = default) => Inner.GetStockByZoneRawAsync(storeId, ct);
        public Task AddAsync(ProductStock stock, CancellationToken ct = default) => Inner.AddAsync(stock, ct);
        public Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) => Inner.AddMovementAsync(movement, ct);
        public Task SaveChangesAsync(CancellationToken ct = default) => Inner.SaveChangesAsync(ct);
    }
}
