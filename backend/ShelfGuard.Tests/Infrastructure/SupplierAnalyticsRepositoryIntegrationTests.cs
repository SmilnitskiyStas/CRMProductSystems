using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// Supplier-portal expansion #7 (Phase 6b): live-Postgres coverage for
/// <see cref="SupplierAnalyticsRepository"/> — the <c>marketplace_order_items ⋈ marketplace_orders</c>
/// window read and the available-catalog projection. Needs real Postgres for the
/// <c>DateOnly</c>/<c>timestamptz</c> window boundary and the join. Plain <c>crm</c> connection
/// (RLS bypassed — SQL correctness under test, not tenant isolation); a unique per-run tenant so
/// assertions ignore pre-existing rows. Same conventions as
/// <see cref="MarketplaceRepositoryMetricsHistoryIntegrationTests"/>.
/// </summary>
public sealed class SupplierAnalyticsRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private string _connectionString = TestPostgres.DefaultConnectionString;
    private bool _dbAvailable;
    private readonly string _run = Guid.NewGuid().ToString("N");

    private Guid _supplierTenantId;
    private Guid _otherSupplierTenantId;
    private Guid _buyerA;
    private Guid _buyerB;
    private Guid _milkItemId;
    private Guid _butterItemId;

    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);

    public SupplierAnalyticsRepositoryIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _connectionString = TestPostgres.ResolveConnectionString();
        try
        {
            await using var probe = new NpgsqlConnection(_connectionString);
            await probe.OpenAsync();
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            _output.WriteLine($"Skipping supplier-analytics repo integration tests — no reachable Postgres: {ex.Message}");
            return;
        }

        await using var db = NewContext();

        var supplierTenant = Tenant.Create($"Analytics Sup {_run}", $"analytics-sup-{_run}");
        supplierTenant.UpdateBusinessType("supplier");
        var otherSupplier = Tenant.Create($"Analytics Other {_run}", $"analytics-oth-{_run}");
        otherSupplier.UpdateBusinessType("supplier");
        var buyerA = Tenant.Create($"Analytics BuyerA {_run}", $"analytics-a-{_run}");
        var buyerB = Tenant.Create($"Analytics BuyerB {_run}", $"analytics-b-{_run}");
        _supplierTenantId = supplierTenant.Id;
        _otherSupplierTenantId = otherSupplier.Id;
        _buyerA = buyerA.Id;
        _buyerB = buyerB.Id;
        db.Tenants.AddRange(supplierTenant, otherSupplier, buyerA, buyerB);

        var supplier = new Supplier { TenantId = _supplierTenantId, Name = $"Sup {_run}" };
        db.Suppliers.Add(supplier);

        var milk = new SupplierItem
        {
            SupplierId = supplier.Id, TenantId = _supplierTenantId,
            CustomName = "Молоко", Unit = "л", IsAvailable = true,
        };
        var butter = new SupplierItem
        {
            SupplierId = supplier.Id, TenantId = _supplierTenantId,
            CustomName = "Масло", Unit = "шт", IsAvailable = true,
        };
        var lard = new SupplierItem
        {
            SupplierId = supplier.Id, TenantId = _supplierTenantId,
            CustomName = "Смалець", Unit = "шт", IsAvailable = false,
        };
        _milkItemId = milk.Id;
        _butterItemId = butter.Id;
        db.SupplierItems.AddRange(milk, butter, lard);

        var agreementA = Agreement(_supplierTenantId, _buyerA);
        var agreementB = Agreement(_supplierTenantId, _buyerB);
        var agreementOther = Agreement(_otherSupplierTenantId, _buyerA);
        db.SupplierAgreements.AddRange(agreementA, agreementB, agreementOther);

        // In window
        db.MarketplaceOrders.Add(Order(agreementA.Id, _supplierTenantId, _buyerA, new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc),
            MarketplaceOrderStatus.Confirmed,
            (milk.Id, "Молоко", 10m, 300m), (null, "Хліб", 5m, 100m)));
        db.MarketplaceOrders.Add(Order(agreementB.Id, _supplierTenantId, _buyerB, new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc),
            MarketplaceOrderStatus.Delivered,
            (milk.Id, "Молоко", 4m, 120m)));
        // Cancelled — must be excluded
        db.MarketplaceOrders.Add(Order(agreementA.Id, _supplierTenantId, _buyerA, new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc),
            MarketplaceOrderStatus.Cancelled,
            (milk.Id, "Молоко", 100m, 3000m)));
        // Out of window (July) — must be excluded
        db.MarketplaceOrders.Add(Order(agreementA.Id, _supplierTenantId, _buyerA, new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc),
            MarketplaceOrderStatus.Delivered,
            (milk.Id, "Молоко", 50m, 1500m)));
        // Different supplier — must be excluded
        db.MarketplaceOrders.Add(Order(agreementOther.Id, _otherSupplierTenantId, _buyerA, new DateTime(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc),
            MarketplaceOrderStatus.Confirmed,
            (null, "Молоко", 9m, 999m)));

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;

        await using var db = NewContext();
        foreach (var tid in new[] { _supplierTenantId, _otherSupplierTenantId, _buyerA, _buyerB })
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM marketplace_order_items WHERE \"SupplierTenantId\" = {tid} OR \"ClientTenantId\" = {tid}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM marketplace_orders WHERE \"SupplierTenantId\" = {tid} OR \"ClientTenantId\" = {tid}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM supplier_agreements WHERE \"SupplierTenantId\" = {tid} OR \"ClientTenantId\" = {tid}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM supplier_items WHERE \"TenantId\" = {tid}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM suppliers WHERE \"TenantId\" = {tid}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\" = {tid}");
        }
    }

    [Fact]
    public async Task GetOrderLinesAsync_ReturnsOnlyInWindowNonCancelledOwnLines()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new SupplierAnalyticsRepository(db);

        var rows = await repo.GetOrderLinesAsync(_supplierTenantId, From, To);

        Assert.Equal(3, rows.Count);                        // 2 lines from order1 + 1 from order2
        Assert.Equal(520m, rows.Sum(r => r.LineTotal));     // cancelled 3000 + July 1500 + other-supplier 999 all excluded
        Assert.Equal(19m, rows.Sum(r => r.Qty));
        Assert.Equal(2, rows.Select(r => r.OrderId).Distinct().Count());
        Assert.All(rows, r => Assert.Contains(r.ClientTenantId, new[] { _buyerA, _buyerB }));
        Assert.Contains(rows, r => r.SupplierItemId == null && r.ItemName == "Хліб"); // deleted-catalog line still returned
    }

    [Fact]
    public async Task GetOrderLinesAsync_WindowBoundaryIsInclusiveOfTheLastDay()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new SupplierAnalyticsRepository(db);

        // Narrow to the single day order2 was placed.
        var rows = await repo.GetOrderLinesAsync(_supplierTenantId, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 20));

        Assert.Single(rows);
        Assert.Equal(120m, rows[0].LineTotal);
    }

    [Fact]
    public async Task GetAvailableCatalogAsync_ReturnsOnlyAvailableOwnItems()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        await using var db = NewContext();
        var repo = new SupplierAnalyticsRepository(db);

        var rows = await repo.GetAvailableCatalogAsync(_supplierTenantId);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { _butterItemId, _milkItemId }.OrderBy(x => x), rows.Select(r => r.SupplierItemId).OrderBy(x => x));
        Assert.DoesNotContain(rows, r => r.ItemName == "Смалець");   // unavailable excluded
    }

    private SupplierAgreement Agreement(Guid supplierTenantId, Guid clientTenantId) => new()
    {
        SupplierTenantId = supplierTenantId,
        ClientTenantId = clientTenantId,
        Status = SupplierAgreementStatus.Active,
        ContractNumber = $"ДС-6b-{_run[..8]}-{Guid.NewGuid().ToString("N")[..4]}",
    };

    private MarketplaceOrder Order(
        Guid agreementId, Guid supplierTenantId, Guid clientTenantId, DateTime createdAtUtc,
        string status, params (Guid? ItemId, string Name, decimal Qty, decimal LineTotal)[] lines)
    {
        var order = new MarketplaceOrder
        {
            OrderNumber = $"MP-6b-{_run[..6]}-{Guid.NewGuid().ToString("N")[..4]}",
            AgreementId = agreementId,
            SupplierTenantId = supplierTenantId,
            ClientTenantId = clientTenantId,
            Status = status,
            TotalAmount = lines.Sum(l => l.LineTotal),
            CreatedAt = new DateTimeOffset(createdAtUtc),
        };
        foreach (var l in lines)
            order.Items.Add(new MarketplaceOrderItem
            {
                OrderId = order.Id,
                SupplierTenantId = supplierTenantId,
                ClientTenantId = clientTenantId,
                SupplierItemId = l.ItemId,
                ItemName = l.Name,
                Unit = "шт",
                Price = l.LineTotal / l.Qty,
                Qty = l.Qty,
                LineTotal = l.LineTotal,
            });
        return order;
    }

    private AppDbContext NewContext() => TestPostgres.NewContext(_connectionString);
}
