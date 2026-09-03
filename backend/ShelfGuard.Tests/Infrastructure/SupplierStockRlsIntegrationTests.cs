using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Supplier-portal expansion — Phase 2 (plan `1-partitioned-book.md`, decisions D2/D3, D8).
/// Live-Postgres proof that the four new supplier inventory tables enforce tenant isolation:
/// a supplier tenant A, under a real <c>rls_audit_test_role</c> (NOSUPERUSER NOBYPASSRLS)
/// session, cannot <c>SELECT</c> supplier tenant B's <c>supplier_stock</c> /
/// <c>supplier_stock_receipts</c> — not even with tenant B's id forged straight into the
/// WHERE clause — and a fully-RESET session sees zero rows (fail-closed).
///
/// The RLS-audit triad check (<c>AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass</c>
/// in <see cref="RlsCrossTenantIntegrationTests"/>) automatically covers the 4 new FORCE-RLS
/// tables. Same harness / collection / skip conventions as
/// <see cref="MarketplaceProviderBypassScopeRlsIntegrationTests"/>.
/// </summary>
[Collection("TENANT_ISOLATION_TESTS")]
public sealed class SupplierStockRlsIntegrationTests : IAsyncLifetime
{
    private readonly RlsAuditRoleFixture _fixture;
    private readonly ITestOutputHelper _output;
    private bool _dbAvailable;
    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    private readonly string _run = Guid.NewGuid().ToString("N");

    public SupplierStockRlsIntegrationTests(RlsAuditRoleFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        if (!_fixture.DbAvailable)
        {
            _dbAvailable = false;
            _output.WriteLine($"Skipping supplier-stock RLS tests — no reachable Postgres: {_fixture.UnavailableReason}");
            return Task.CompletedTask;
        }

        try
        {
            _dataSource = new NpgsqlDataSourceBuilder(_fixture.ConnectionString).EnableDynamicJson().Build();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_dataSource)
                .IgnoreManyServiceProvidersWarning()
                .Options;
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine($"Skipping supplier-stock RLS tests — no reachable Postgres: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    // ── supplier_stock ───────────────────────────────────────────────────────

    [Fact]
    public async Task SupplierStock_CrossTenantForgedFilter_ReturnsOnlyOwnRows()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedAsync();
        try
        {
            await using var session = await OpenSessionAsync(f.TenantA);

            var own = await ScalarAsync(session, $"SELECT count(*) FROM supplier_stock WHERE \"TenantId\" = '{f.TenantA:D}';");
            Assert.Equal(1L, own);

            // Forged filter for tenant B's id while connected as tenant A — RLS must still win.
            var leaked = await ScalarAsync(session, $"SELECT count(*) FROM supplier_stock WHERE \"TenantId\" = '{f.TenantB:D}';");
            Assert.Equal(0L, leaked);

            // Unfiltered SELECT must not return tenant B's row either.
            var total = await ScalarAsync(session, "SELECT count(*) FROM supplier_stock;");
            Assert.Equal(1L, total);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    [Fact]
    public async Task SupplierStockReceipts_CrossTenantForgedFilter_ReturnsOnlyOwnRows()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedAsync();
        try
        {
            await using var session = await OpenSessionAsync(f.TenantA);

            var own = await ScalarAsync(session, $"SELECT count(*) FROM supplier_stock_receipts WHERE \"TenantId\" = '{f.TenantA:D}';");
            Assert.Equal(1L, own);

            var leaked = await ScalarAsync(session, $"SELECT count(*) FROM supplier_stock_receipts WHERE \"TenantId\" = '{f.TenantB:D}';");
            Assert.Equal(0L, leaked);

            var total = await ScalarAsync(session, "SELECT count(*) FROM supplier_stock_receipts;");
            Assert.Equal(1L, total);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    [Fact]
    public async Task SupplierStock_FullyResetSession_ReturnsZeroRows_NotEveryRow()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        var f = await SeedAsync();
        try
        {
            await using var db = NewContext();
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
            await db.Database.ExecuteSqlRawAsync("RESET app.tenant_id; RESET app.role;");

            var stock = await ScalarAsync(db, "SELECT count(*) FROM supplier_stock;");
            var receipts = await ScalarAsync(db, "SELECT count(*) FROM supplier_stock_receipts;");
            var movements = await ScalarAsync(db, "SELECT count(*) FROM supplier_stock_movements;");
            var items = await ScalarAsync(db, "SELECT count(*) FROM supplier_stock_receipt_items;");

            await db.Database.ExecuteSqlRawAsync("RESET ROLE;");

            Assert.Equal(0L, stock);
            Assert.Equal(0L, receipts);
            Assert.Equal(0L, movements);
            Assert.Equal(0L, items);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    // ── seed / session / cleanup ─────────────────────────────────────────────

    private sealed record Fixture(
        Guid TenantA, Guid TenantB,
        Guid SupplierAId, Guid SupplierBId,
        Guid WarehouseAId, Guid WarehouseBId,
        Guid SupplierItemAId, Guid SupplierItemBId)
    {
        public Guid[] AllTenantIds => [TenantA, TenantB];
    }

    private async Task<Fixture> SeedAsync()
    {
        await using var db = NewContext();

        var tenantA = Tenant.Create($"Supplier Stock RLS A {_run}", $"sstock-rls-a-{_run}");
        tenantA.UpdateBusinessType("supplier");
        var tenantB = Tenant.Create($"Supplier Stock RLS B {_run}", $"sstock-rls-b-{_run}");
        tenantB.UpdateBusinessType("supplier");
        db.Tenants.AddRange(tenantA, tenantB);

        var supplierA = new Supplier { TenantId = tenantA.Id, Name = $"Постачальник A {_run}" };
        var supplierB = new Supplier { TenantId = tenantB.Id, Name = $"Постачальник B {_run}" };
        db.Suppliers.AddRange(supplierA, supplierB);

        var whA = new Location { TenantId = tenantA.Id, Name = "Склад A", Type = "warehouse" };
        var whB = new Location { TenantId = tenantB.Id, Name = "Склад B", Type = "warehouse" };
        db.Locations.AddRange(whA, whB);

        var itemA = new SupplierItem { SupplierId = supplierA.Id, TenantId = tenantA.Id, CustomName = "Товар A", Unit = "шт" };
        var itemB = new SupplierItem { SupplierId = supplierB.Id, TenantId = tenantB.Id, CustomName = "Товар B", Unit = "шт" };
        db.SupplierItems.AddRange(itemA, itemB);

        db.SupplierStocks.AddRange(
            NewBatch(tenantA.Id, itemA.Id, whA.Id),
            NewBatch(tenantB.Id, itemB.Id, whB.Id));

        db.SupplierStockReceipts.AddRange(
            new SupplierStockReceipt { TenantId = tenantA.Id, WarehouseId = whA.Id, Status = "draft" },
            new SupplierStockReceipt { TenantId = tenantB.Id, WarehouseId = whB.Id, Status = "draft" });

        await db.SaveChangesAsync();

        return new Fixture(
            tenantA.Id, tenantB.Id, supplierA.Id, supplierB.Id,
            whA.Id, whB.Id, itemA.Id, itemB.Id);
    }

    private static SupplierStock NewBatch(Guid tenantId, Guid supplierItemId, Guid warehouseId) => new()
    {
        TenantId = tenantId,
        SupplierItemId = supplierItemId,
        WarehouseId = warehouseId,
        ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)),
        Quantity = 100,
        QuantityInitial = 100,
        Status = "safe",
    };

    private async Task<RlsSession> OpenSessionAsync(Guid tenantId)
    {
        var db = NewContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET ROLE rls_audit_test_role;");
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync(
            $"SET app.tenant_id = '{tenantId:D}'; SET app.role = 'store_manager'; RESET app.consumer_account_id;");
#pragma warning restore EF1002
        return new RlsSession(db);
    }

    private static async Task<long> ScalarAsync(AppDbContext db, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private static Task<long> ScalarAsync(RlsSession session, string sql) => ScalarAsync(session.Db, sql);

    private async Task CleanupAsync(Fixture f)
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_stock_movements WHERE \"TenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_stock_receipt_items WHERE \"TenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_stock_receipts WHERE \"TenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_stock WHERE \"TenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM supplier_items WHERE \"TenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM suppliers WHERE \"TenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM locations WHERE \"TenantId\" = ANY({f.AllTenantIds})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM tenants WHERE \"Id\" = ANY({f.AllTenantIds})");
    }

    private AppDbContext NewContext() => new(_options!);

    private sealed class RlsSession : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public RlsSession(AppDbContext db) => Db = db;

        public async ValueTask DisposeAsync()
        {
            try { await Db.Database.ExecuteSqlRawAsync("RESET ROLE;"); }
            catch { /* best-effort cleanup only */ }
            await Db.DisposeAsync();
        }
    }
}
