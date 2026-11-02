using Microsoft.Extensions.Configuration;
using NSubstitute;
using ShelfGuard.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pricing;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
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
/// Uses only the explicit disposable SHELFGUARD_RECOVERY_TEST_DB fixture.
/// </summary>
[Collection("Session database")]
public sealed class PosConcurrencySalesIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private string _connectionString = string.Empty;
    private bool _dbAvailable;

    private Guid _tenantId;
    private Guid _stockId;
    private Guid _shiftId;
    private string _barcode = string.Empty;

    public PosConcurrencySalesIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("SHELFGUARD_RECOVERY_TEST_DB") ?? string.Empty;
        if (string.IsNullOrEmpty(_connectionString)) return;

        try
        {
            await using var probe = new NpgsqlConnection(_connectionString);
            await probe.OpenAsync();
            _dbAvailable = true;
        }
        catch
        {
            _dbAvailable = false;
            throw new InvalidOperationException("Explicit disposable PostgreSQL fixture is unavailable.");
        }

        _barcode = $"CONC-{Guid.NewGuid():N}"[..16];

        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

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
        // Accepted archives are immutable. This explicit disposable recovery database is removed by the harness.
        if (await db.Set<ShelfGuard.Domain.Entities.Erp.PosPricingSnapshot>().AnyAsync(a => a.TenantId == _tenantId)) return;

        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM promotion_rule_applications WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE promotion_rules SET \"CurrentVersionId\" = NULL, \"PublishedVersionId\" = NULL WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM promotion_rule_versions WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM promotion_rules WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_transaction_items WHERE \"ProductStockId\" = {_stockId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_events WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_transactions WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM pos_shifts WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cash_registers WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM product_stock WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM items WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM locations WHERE \"TenantId\" = {_tenantId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {_tenantId}");
    }

    [RecoveryPostgresFact]
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

    [RecoveryPostgresFact]
    public async Task Pricing_shadow_matches_real_Postgres_commit_and_skips_replay_after_catalog_change()
    {
        await using var db = NewContext();
        var stock = await db.ProductStocks.SingleAsync(b => b.Id == _stockId);
        var productId = stock.ProductId; var store = stock.StoreId;
        var observer = new PricingShadow(true, new HashSet<(Guid, Guid)> { (_tenantId, store) });
        var svc = BuildService(db, s => s, observer);
        var request = new CreateSaleRequest(_shiftId, [new(_barcode, 1)], "Cash", 50, ClientTransactionId: Guid.NewGuid());
        var cashier = Guid.NewGuid();
        var first = await svc.CreateSaleAsync(_tenantId, cashier, request);
        Assert.Null(first.Error); Assert.NotNull(first.Sale); Assert.Equal(1, observer.Counts.Matched); Assert.Equal(0, observer.Counts.Mismatch);
        var product = await db.Items.SingleAsync(i => i.Id == productId); product.PriceRetail = 500; await db.SaveChangesAsync();
        var replay = await svc.CreateSaleAsync(_tenantId, cashier, request);
        Assert.Null(replay.Error); Assert.Equal(50, replay.Sale!.Subtotal); Assert.Equal(first.Sale.TransactionId, replay.Sale.TransactionId);
        Assert.Equal(1, observer.Counts.Replays); Assert.Equal(1, observer.Counts.Matched);
        Assert.Equal(1, await db.PosTransactions.CountAsync(t => t.TenantId == _tenantId));
        Assert.Equal(0, (await db.ProductStocks.SingleAsync(b => b.Id == _stockId)).Quantity);
        Assert.False(await db.Set<ShelfGuard.Domain.Entities.Erp.PriceList>().AnyAsync(p => p.TenantId == _tenantId));
        Assert.False(await db.Set<ShelfGuard.Domain.Entities.Erp.PriceResolutionSnapshot>().AnyAsync(p => p.TenantId == _tenantId));
    }

    [RecoveryPostgresFact]
    public async Task Offline_promotion_lease_commits_original_version_and_replays_from_a_fresh_context()
    {
        await using var db = NewContext();
        var stock = await db.ProductStocks.SingleAsync(b => b.Id == _stockId);
        var product = await db.Items.SingleAsync(i => i.Id == stock.ProductId);
        var register = new CashRegister { TenantId = _tenantId, StoreId = stock.StoreId, Number = 1, Name = "Synthetic offline proof" };
        db.CashRegisters.Add(register); await db.SaveChangesAsync();
        var shift = new PosShift { TenantId = _tenantId, StoreId = stock.StoreId, RegisterId = register.Id };
        var rule = new PromotionRule { TenantId = _tenantId, Name = "Original -10%" };
        db.PosShifts.Add(shift); db.PromotionRules.Add(rule); await db.SaveChangesAsync();
        var version = PromotionRuleVersion.Create(_tenantId, rule.Id, 1,
            $$$"""{"type":"percent_off","targets":{"itemIds":["{{{product.Id}}}"]},"params":{"percent":10}}""");
        db.PromotionRuleVersions.Add(version); await db.SaveChangesAsync();
        rule.CurrentVersionId = version.Id; rule.PublishedVersionId = version.Id; rule.Status = PromotionRule.StatusPublished;
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Jwt:Secret"] = "synthetic-postgres-promotions-012345678901" }).Build();
        var proofs = new PosPromotionSnapshots(config); var prices = new PosPriceSnapshots(config);
        var at = DateTime.UtcNow;
        var repo = new PromotionRuleRepository(db);
        var proof = proofs.Issue(_tenantId, stock.StoreId, register.Id, null, await repo.GetPublishedAsync(_tenantId), at)!;
        var price = prices.Issue(new CatalogSyncRow(product.Id, product.Name, product.Barcodes.ToArray(), 50,
            product.VatRate, product.Unit, true, at, TenantId: _tenantId), at);
        // Both mutable sources change before the queued receipt arrives.
        rule.Status = PromotionRule.StatusPaused; product.PriceRetail = 500; await db.SaveChangesAsync();
        var registers = Substitute.For<ICashRegisterRepository>();
        registers.HadSessionAtAsync(default, default, default, default, default).ReturnsForAnyArgs(true);
        var svc = new PosService(new PosRepository(db), new StockRepository(db), new ItemRepository(db), new DiscountRepository(db),
            new StaticFiscalServiceFactory(new NoopFiscalService()), new LoyaltyRepository(db), new CustomerRepository(db),
            NullLogger<PosService>.Instance, registers: registers, promotions: repo, priceSnapshots: prices, promotionSnapshots: proofs);
        var request = new CreateSaleRequest(shift.Id, [new(_barcode, 1, price.Token)], "Cash", 50,
            ClientTransactionId: Guid.NewGuid(), OccurredAt: at, ExpectedTotal: 45, PromotionSnapshot: proof.Token,
            ExpectedPriceSegments: [new(0, 1, 45, rule.Id, version.Id)]);
        var cashier = Guid.NewGuid();
        var (batch, error, _) = await svc.CreateSalesBatchAsync(_tenantId, cashier, new([request]), registerId: register.Id);
        Assert.Null(error); var first = Assert.Single(batch!.Results); Assert.Equal("created", first.Status);
        Assert.Equal(version.Id, (await db.PromotionRuleApplications.SingleAsync(a => a.TenantId == _tenantId)).VersionId);
        await using var fresh = NewContext();
        // No proof reader at all: committed replay uses the immutable stored intent hash.
        var replayService = BuildService(fresh, s => s);
        var (replayed, _, _) = await replayService.CreateSalesBatchAsync(_tenantId, cashier, new([request]), registerId: register.Id);
        var replay = Assert.Single(replayed!.Results); Assert.Equal("duplicate", replay.Status);
        Assert.Equal(45, replay.Sale!.Subtotal); Assert.Equal(first.Sale!.TransactionId, replay.Sale.TransactionId);
        var changed = request with { ExpectedPriceSegments = [new(0, 1, 45, rule.Id, Guid.NewGuid())] };
        var (conflict, _, _) = await replayService.CreateSalesBatchAsync(_tenantId, cashier, new([changed]), registerId: register.Id);
        Assert.Equal(409, Assert.Single(conflict!.Results).ErrorStatusCode);
        Assert.Equal(1, await fresh.PosTransactions.CountAsync(t => t.TenantId == _tenantId));
        Assert.Equal(1, await fresh.PromotionRuleApplications.CountAsync(a => a.TenantId == _tenantId));
        Assert.Equal(0, (await fresh.ProductStocks.SingleAsync(b => b.Id == _stockId)).Quantity);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // KI-035: this used to build (and never dispose) a brand-new NpgsqlDataSource on EVERY call —
    // each one stranding a physical Postgres backend for the rest of the run. Now one shared,
    // process-wide pool (see TestPostgres). The two cashier contexts this test races against each
    // other still get two DISTINCT physical connections: a pooled connection cannot be handed to a
    // second context while the first still holds it open, so the rendezvous is unaffected.
    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);

    private static PosService BuildService(AppDbContext db, Func<IStockRepository, IStockRepository> wrapStock, IPricingShadow? shadow = null) =>
        new(
            new PosRepository(db),
            wrapStock(new StockRepository(db)),
            new ItemRepository(db),
            new DiscountRepository(db),
            new StaticFiscalServiceFactory(new NoopFiscalService()),
            new LoyaltyRepository(db),
            new CustomerRepository(db),
            NullLogger<PosService>.Instance, pricingShadow: shadow);

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
        public Task<(List<ProductStock> Items, int Total)> GetPagedAsync(Guid[]? storeIds, string? status, Guid? zoneId, Guid? productId, string? search, string? sortBy, bool? sortDescending, int page, int pageSize, Guid? categoryId = null, decimal? minQuantity = null, decimal? maxQuantity = null, CancellationToken ct = default) => Inner.GetPagedAsync(storeIds, status, zoneId, productId, search, sortBy, sortDescending, page, pageSize, categoryId, minQuantity, maxQuantity, ct);
        public Task<List<Location>> GetProductionStoresAsync(CancellationToken ct = default) => Inner.GetProductionStoresAsync(ct);
        public Task<Dictionary<string, int>> GetStatusCountsAsync(Guid[]? storeIds, CancellationToken ct = default) => Inner.GetStatusCountsAsync(storeIds, ct);
        public Task<List<(Guid? ZoneId, string ZoneName, string ZoneType, string Status)>> GetStockByZoneRawAsync(Guid[]? storeIds, CancellationToken ct = default) => Inner.GetStockByZoneRawAsync(storeIds, ct);
        public Task AddAsync(ProductStock stock, CancellationToken ct = default) => Inner.AddAsync(stock, ct);
        public Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) => Inner.AddMovementAsync(movement, ct);
        public Task SaveChangesAsync(CancellationToken ct = default) => Inner.SaveChangesAsync(ct);
    }
}
