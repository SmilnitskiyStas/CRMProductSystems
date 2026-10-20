using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ShelfGuard.Application.Features.Pos;
using ShelfGuard.Application.Features.Pos.Dtos;
using ShelfGuard.Application.Features.Pos.Fiscal;
using ShelfGuard.Application.Features.Registers;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Promotions;

/// <summary>
/// Real-Postgres flow for promotion rules (Phase 1): rule + immutable version (circular FK insert
/// order) -> published -> 2+1 sale through the real PosService/repositories (sub-lines, FEFO,
/// execution log committed in the same SaveChanges) -> idempotent batch replay -> version UPDATE
/// refused by trigger -> sync/promotions etag -> RLS policy shape on the three new tables.
/// Connects as the crm superuser (SQL/EF correctness, not isolation). Soft-skips without Postgres
/// or when the AddPromotionRules migration has not been applied yet.
/// </summary>
public sealed class PromotionRulesIntegrationTests : IAsyncLifetime
{
    private static readonly string[] NewTables = ["promotion_rules", "promotion_rule_versions", "promotion_rule_applications"];

    private readonly ITestOutputHelper _output;
    private string _cs = string.Empty;
    private bool _ready;

    private Guid _tenantId;
    private readonly Guid _storeId = Guid.NewGuid();
    private Guid _cashierId;
    private Guid _itemId;
    private Guid _earlyBatchId;
    private Guid _lateBatchId;
    private readonly string _run = Guid.NewGuid().ToString("N");
    private string Barcode => $"PR-{_run}"[..20];

    public PromotionRulesIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _cs = TestPostgres.ResolveConnectionString();
        try
        {
            await using var probe = new NpgsqlConnection(_cs);
            await probe.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'pos_transaction_items' AND column_name = 'PromotionRuleId'", probe);
            if ((long)(await cmd.ExecuteScalarAsync())! == 0)
            {
                _output.WriteLine("Skipping promotion integration test - AddPromotionRules migration not applied.");
                return;
            }
            _ready = true;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Skipping promotion integration test - no Postgres at '{_cs}': {ex.Message}");
            return;
        }

        await using var db = NewContext();
        var tenant = Tenant.Create($"Promo Flow {_run}", $"promo-flow-{_run}");
        tenant.UpdateModules(["inventory", "pos"]);
        _tenantId = tenant.Id;
        var store = new Location { Id = _storeId, TenantId = tenant.Id, Name = "Promo Flow Store" };
        var cashier = User.Create(tenant.Id, $"cashier-{_run}@promo.test", "Promo Cashier", "pw", AppRoles.Cashier, _storeId);
        _cashierId = cashier.Id;
        var item = new Item { TenantId = tenant.Id, Name = "Promo Item", Barcodes = [Barcode], PriceRetail = 30m };
        _itemId = item.Id;
        var early = new ProductStock
        {
            TenantId = tenant.Id, ProductId = item.Id, StoreId = _storeId, Quantity = 2m, QuantityInitial = 2m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), Status = "safe",
        };
        var late = new ProductStock
        {
            TenantId = tenant.Id, ProductId = item.Id, StoreId = _storeId, Quantity = 10m, QuantityInitial = 10m,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), Status = "safe",
        };
        _earlyBatchId = early.Id;
        _lateBatchId = late.Id;

        db.Tenants.Add(tenant);
        db.Locations.Add(store);
        db.Users.Add(cashier);
        db.Items.Add(item);
        db.ProductStocks.AddRange(early, late);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_ready) return;
        await using var db = NewContext();
        foreach (var sql in new[]
        {
            "DELETE FROM promotion_rule_applications WHERE \"TenantId\" = {0}",
            "DELETE FROM pos_transaction_items WHERE \"TransactionId\" IN (SELECT \"Id\" FROM pos_transactions WHERE \"TenantId\" = {0})",
            "DELETE FROM stock_events WHERE \"TenantId\" = {0}",
            "DELETE FROM pos_transactions WHERE \"TenantId\" = {0}",
            "DELETE FROM pos_shifts WHERE \"TenantId\" = {0}",
            "DELETE FROM promotion_rules WHERE \"TenantId\" = {0}",
            "DELETE FROM product_stock WHERE \"TenantId\" = {0}",
            "DELETE FROM items WHERE \"TenantId\" = {0}",
            "DELETE FROM users WHERE \"TenantId\" = {0}",
            "DELETE FROM locations WHERE \"TenantId\" = {0}",
            "DELETE FROM tenants WHERE \"Id\" = {0}",
        })
        {
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(sql, _tenantId);
#pragma warning restore EF1002
        }
    }

    [Fact]
    public async Task Published_two_plus_one_rule_prices_a_real_sale_and_is_synced()
    {
        if (!_ready) { _output.WriteLine("skipped"); return; }

        // 1. Rule (draft) -> immutable version -> publish (circular FK: rule first, then version).
        var rule = new PromotionRule { TenantId = _tenantId, Name = "2+1" };
        Guid versionId;
        await using (var db = NewContext())
        {
            var repo = new PromotionRuleRepository(db);
            await repo.AddRuleAsync(rule);
            await repo.SaveChangesAsync();
            var version = PromotionRuleVersion.Create(_tenantId, rule.Id, 1,
                $$$"""{"type":"buy_n_get_m","targets":{"itemIds":["{{{_itemId}}}"]},"params":{"buyQty":2,"getQty":1,"getPrice":0.01}}""");
            versionId = version.Id;
            await repo.AddVersionAsync(version);
            await repo.SaveChangesAsync();
            rule.CurrentVersionId = version.Id;
            rule.PublishedVersionId = version.Id;
            rule.Status = PromotionRule.StatusPublished;
            await repo.SaveChangesAsync();

            var published = Assert.Single(await repo.GetPublishedAsync(_tenantId));
            Assert.Equal((rule.Id, versionId), (published.RuleId, published.VersionId));
        }

        // 2. Shift + sale of 3 through the real service stack.
        var shift = (await WithPos(p => p.OpenShiftAsync(_tenantId, _cashierId, new OpenShiftRequest(_storeId, 0m)))).Shift!;
        var key = Guid.NewGuid();
        var request = new CreateSaleRequest(shift.ShiftId, [new SaleItemRequest(Barcode, 3)], "Cash", 100m,
            ClientTransactionId: key);
        var (sale, error, _) = await WithPos(p => p.CreateSaleAsync(_tenantId, _cashierId, request));
        Assert.Null(error);
        Assert.Equal(60.01m, sale!.Subtotal);

        await using (var verify = NewContext())
        {
            var tx = await verify.PosTransactions.AsNoTracking().Include(t => t.Items)
                .SingleAsync(t => t.TenantId == _tenantId);
            Assert.Equal(60.01m, tx.TotalAmount);
            var items = tx.Items.OrderBy(i => i.PriceFinal).ToList();
            Assert.Equal(2, items.Count);
            Assert.Equal((1m, 0.01m, 29.99m, rule.Id, "2+1"),
                (items[0].Quantity, items[0].PriceFinal, items[0].DiscountAmount, items[0].PromotionRuleId!.Value, items[0].PromotionLabel!));
            Assert.Equal(_lateBatchId, items[0].ProductStockId);
            Assert.Equal((2m, 30m, (Guid?)null, _earlyBatchId),
                (items[1].Quantity, items[1].PriceFinal, items[1].PromotionRuleId, items[1].ProductStockId!.Value));

            Assert.Equal(0m, (await verify.ProductStocks.AsNoTracking().SingleAsync(s => s.Id == _earlyBatchId)).Quantity);
            Assert.Equal(9m, (await verify.ProductStocks.AsNoTracking().SingleAsync(s => s.Id == _lateBatchId)).Quantity);
            Assert.Equal(-3m, await verify.StockEvents.Where(e => e.TenantId == _tenantId).SumAsync(e => e.QuantityDelta));

            var app = await verify.PromotionRuleApplications.AsNoTracking().SingleAsync(a => a.TenantId == _tenantId);
            Assert.Equal((rule.Id, versionId, tx.Id, _storeId, 1m, 29.99m),
                (app.RuleId, app.VersionId, app.TransactionId, app.StoreId, app.Units, app.DiscountAmount));
        }

        // 3. Offline replay of the same key is a duplicate, nothing new is written.
        var (batch, batchError, _) = await WithPos(p => p.CreateSalesBatchAsync(
            _tenantId, _cashierId, new BatchSalesRequest([request])));
        Assert.Null(batchError);
        var replay = Assert.Single(batch!.Results);
        Assert.Equal("duplicate", replay.Status);
        Assert.Equal(sale.TransactionId, replay.Sale!.TransactionId);
        await using (var verify = NewContext())
        {
            Assert.Equal(1, await verify.PosTransactions.CountAsync(t => t.TenantId == _tenantId));
            Assert.Equal(1, await verify.PromotionRuleApplications.CountAsync(a => a.TenantId == _tenantId));
        }

        // 4. Versions are immutable at the DB level.
        await using (var db = NewContext())
        {
#pragma warning disable EF1002
            var ex = await Assert.ThrowsAnyAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE promotion_rule_versions SET \"ChangeComment\" = 'x' WHERE \"Id\" = {0}", versionId));
#pragma warning restore EF1002
            Assert.Contains("immutable", ex.MessageText);
        }

        // 5. Sync endpoint: rule for this store + stable etag -> notModified.
        await using (var db = NewContext())
        {
            var sync = new PosSyncService(new CatalogSyncRepository(db), new PromotionRuleRepository(db), new CashRegisterRepository(db));
            var (first, err, _) = await sync.GetPromotionsAsync(_tenantId, null, _storeId, null);
            Assert.Null(err);
            var synced = Assert.Single(first!.Rules);
            Assert.Equal((rule.Id, versionId, "2+1"), (synced.Id, synced.VersionId, synced.Name));
            Assert.Equal("buy_n_get_m", synced.Definition.GetProperty("type").GetString());
            Assert.False(first.NotModified);

            var (second, _, _) = await sync.GetPromotionsAsync(_tenantId, null, _storeId, first.Etag);
            Assert.True(second!.NotModified);
            Assert.Empty(second.Rules);
            Assert.Equal(first.Etag, second.Etag);

            // Only recent changes: a shared dev DB holds far more than one page of items.
            var catalog = await sync.GetCatalogAsync(DateTime.UtcNow.AddMinutes(-30), null, 2000);
            Assert.Contains(catalog.Items, i => i.Id == _itemId && i.CategoryId == null);
        }
    }

    [Fact]
    public async Task New_tables_have_force_rls_and_the_canonical_policy_triad()
    {
        if (!_ready) { _output.WriteLine("skipped"); return; }

        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        foreach (var table in NewTables)
        {
            await using var flags = new NpgsqlCommand(
                "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname = @t", conn);
            flags.Parameters.AddWithValue("t", table);
            await using (var r = await flags.ExecuteReaderAsync())
            {
                Assert.True(await r.ReadAsync());
                Assert.True(r.GetBoolean(0), $"{table}: RLS not enabled");
                Assert.True(r.GetBoolean(1), $"{table}: RLS not forced");
            }

            await using var pol = new NpgsqlCommand(
                "SELECT policyname, qual FROM pg_policies WHERE tablename = @t ORDER BY policyname", conn);
            pol.Parameters.AddWithValue("t", table);
            var policies = new Dictionary<string, string>();
            await using (var r = await pol.ExecuteReaderAsync())
                while (await r.ReadAsync()) policies[r.GetString(0)] = r.GetString(1);

            Assert.Equal(["provider_bypass", "tenant_isolation", "worker_bypass"], policies.Keys.Order().ToArray());
            Assert.Contains("NULLIF", policies["tenant_isolation"], StringComparison.OrdinalIgnoreCase);
        }
    }

    private AppDbContext NewContext() => TestPostgres.NewContext(_cs);

    private async Task<T> WithPos<T>(Func<PosService, Task<T>> action)
    {
        await using var db = NewContext();
        var svc = new PosService(
            new PosRepository(db),
            new StockRepository(db),
            new ItemRepository(db),
            new DiscountRepository(db),
            new StaticFiscalFactory(new NoopFiscalService()),
            new LoyaltyRepository(db),
            new CustomerRepository(db),
            NullLogger<PosService>.Instance,
            new UserRepository(db),
            new CashRegisterRepository(db),
            new PromotionRuleRepository(db));
        return await action(svc);
    }

    private sealed class StaticFiscalFactory(IFiscalService service) : IFiscalServiceFactory
    {
        public PrroConnectionConfig? EnvFallback => null;
        public Task<IFiscalService> GetForTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(service);
        public IFiscalService Create(Guid tenantId, PrroConnectionConfig config) => service;
    }
}
