using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.SupplierInventory;
using ShelfGuard.Application.Features.SupplierInventory.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace ShelfGuard.Tests.Infrastructure;

/// <summary>
/// TASK-697 regression: <see cref="SupplierStockReceiptService.AddLineAsync"/> 500'd in prod with
/// <c>DbUpdateConcurrencyException</c> ("expected to affect 1 row(s), but actually affected 0").
/// Root cause: the method called <c>_repo.Update(receipt)</c> after adding a brand-new
/// <see cref="SupplierStockReceiptItem"/> to the tracked navigation — EF walked the graph and,
/// because the item's <c>Id</c> already carried a client-side <c>Guid.NewGuid()</c> default,
/// marked it Modified instead of Added, emitting <c>UPDATE … WHERE Id=&lt;new guid&gt;</c> (0 rows).
///
/// The pre-existing <see cref="SupplierStockReceiptServiceTests"/> use NSubstitute mocks and so
/// structurally cannot exercise EF's change-tracking. This test runs the real service + real
/// repositories against live dev Postgres (no RLS session vars — write-graph correctness under
/// test, not isolation). Same connection/skip pattern as the other non-RLS integration tests.
/// </summary>
public sealed class SupplierStockReceiptFlowIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private bool _dbAvailable;
    private string _cs = TestPostgres.DefaultConnectionString;

    private Guid _tenantId, _supplierId, _supplierItemId, _warehouseId;
    private readonly Guid _receiptScopeId = Guid.NewGuid();

    public SupplierStockReceiptFlowIntegrationTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        _cs = TestPostgres.ResolveConnectionString();
        try
        {
            await using var db = TestPostgres.NewContext(_cs);
            await db.Database.OpenConnectionAsync();

            // Own every fixture row. Borrowing another test's supplier caused both false passes
            // on an empty CI database and races with that test's cleanup in the full suite.
            var tenant = Tenant.Create($"Receipt regression {_receiptScopeId:N}", $"receipt-regression-{_receiptScopeId:N}");
            Assert.Null(tenant.UpdateBusinessType("supplier"));
            var supplier = new Supplier { TenantId = tenant.Id, Name = "Synthetic receipt supplier" };
            var item = new SupplierItem { TenantId = tenant.Id, SupplierId = supplier.Id, CustomName = "Synthetic item", Unit = "шт" };
            db.AddRange(tenant, supplier, item);
            _tenantId = tenant.Id; _supplierId = supplier.Id; _supplierItemId = item.Id;

            var wh = new Location
            {
                TenantId = _tenantId,
                Name = $"TASK-697 wh {_receiptScopeId:N}",
                Type = "warehouse",
                LocationType = "warehouse",
                IsActive = true,
            };
            db.Locations.Add(wh);
            await db.SaveChangesAsync();
            _warehouseId = wh.Id;
            _dbAvailable = true;
        }
        catch (Exception ex)
        {
            _dbAvailable = false;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SHELFGUARD_TEST_DB_CONNECTION"))) throw;
            _output.WriteLine($"Dev Postgres unavailable — skipped: {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (!_dbAvailable) return;
        await using var db = TestPostgres.NewContext(_cs);
        await db.Database.ExecuteSqlRawAsync(
            @"DELETE FROM supplier_stock_movements WHERE ""ReferenceId"" IN (SELECT ""Id"" FROM supplier_stock_receipts WHERE ""WarehouseId"" = {0});
              DELETE FROM supplier_stock WHERE ""WarehouseId"" = {0};
              DELETE FROM supplier_stock_receipt_items WHERE ""ReceiptId"" IN (SELECT ""Id"" FROM supplier_stock_receipts WHERE ""WarehouseId"" = {0});
              DELETE FROM supplier_stock_receipts WHERE ""WarehouseId"" = {0};
              DELETE FROM locations WHERE ""Id"" = {0};", _warehouseId);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM supplier_items WHERE \"Id\"={_supplierItemId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM suppliers WHERE \"Id\"={_supplierId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tenants WHERE \"Id\"={_tenantId}");
    }

    private (SupplierStockReceiptService Svc, IDisposable Ctx) Build()
    {
        var db = TestPostgres.NewContext(_cs);
        var receiptRepo = new SupplierStockReceiptRepository(db);
        var stockRepo = new SupplierStockRepository(db);
        return (new SupplierStockReceiptService(receiptRepo, stockRepo), db);
    }

    [Fact]
    public async Task Draft_AddTwoLines_SameProduct_Persists()
    {
        if (!_dbAvailable) { _output.WriteLine("DB not available — skipped."); return; }

        Guid receiptId;
        {
            var (svc, ctx) = Build();
            using (ctx)
            {
                var (draft, err) = await svc.CreateDraftAsync(_tenantId, _warehouseId, "TASK-697", null, Guid.NewGuid());
                Assert.Null(err);
                Assert.NotNull(draft);
                receiptId = draft!.Id;
            }
        }

        // Each call gets a fresh context — mirrors the per-request lifetime in prod.
        {
            var (svc, ctx) = Build();
            using (ctx)
            {
                var (r1, e1) = await svc.AddLineAsync(_tenantId, receiptId,
                    new AddSupplierReceiptLineRequest(_supplierItemId,
                        new DateOnly(2027, 1, 1), 100m, "B1", null, null));
                Assert.Null(e1);
                Assert.NotNull(r1);
                Assert.Single(r1!.Items);
            }
        }
        {
            var (svc, ctx) = Build();
            using (ctx)
            {
                var (r2, e2) = await svc.AddLineAsync(_tenantId, receiptId,
                    new AddSupplierReceiptLineRequest(_supplierItemId,
                        new DateOnly(2027, 3, 1), 50m, "B2", null, null));
                Assert.Null(e2);
                Assert.NotNull(r2);
                Assert.Equal(2, r2!.Items.Count);
            }
        }

        {
            var (svc, ctx) = Build();
            using (ctx)
            {
                var reloaded = await svc.GetAsync(_tenantId, receiptId);
                Assert.NotNull(reloaded);
                Assert.Equal(2, reloaded!.Items.Count);
                Assert.All(reloaded.Items, i => Assert.Equal(_supplierItemId, i.SupplierItemId));
            }
        }
    }

}
