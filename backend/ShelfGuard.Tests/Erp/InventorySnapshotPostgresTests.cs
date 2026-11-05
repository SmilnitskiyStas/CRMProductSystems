using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Backfill;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class InventorySnapshotPostgresTests(ErpSchemaFixture fixture)
{
    [ErpPostgresFact]
    public async Task Maintenance_sales_limit_retains_all_lines_without_relaxing_other_stream_bounds()
    {
        var seed = await SeedAsync();
        await using (var setup = fixture.Context())
        {
            var user = User.Create(seed.Tenant, Guid.NewGuid()+"@synthetic.invalid", "Synthetic cashier", "unused", "cashier", seed.Location);
            var shift = new PosShift { TenantId = seed.Tenant, StoreId = seed.Location, CashierId = user.Id };
            var sale = new PosTransaction { TenantId = seed.Tenant, StoreId = seed.Location, ShiftId = shift.Id, CashierId = user.Id, ReceiptNumber = "maintenance-bound", PaymentType = "Cash", TenderedAmount = 3, TotalAmount = 3 };
            setup.AddRange(user, shift, sale);
            for (var n=0;n<3;n++) setup.Add(new PosTransactionItem { TransactionId = sale.Id, ProductId = seed.Item, ProductStockId = seed.Batch, Quantity = 1, PriceRetail = 1, PriceFinal = 1, LineTotal = 1 });
            await setup.SaveChangesAsync();
        }
        await using var db = fixture.Context(); var reader = new InventorySnapshotReader(db);
        var error = await Assert.ThrowsAsync<SnapshotLimitExceededException>(() => reader.ReadAsync(seed.Tenant, seed.Location, 2));
        Assert.Contains("sale_lines", error.Message); Assert.Null(db.Database.CurrentTransaction);
        var snapshot = await reader.ReadControlOpeningAsync(seed.Tenant, seed.Location, 2, 3);
        Assert.Equal(3, snapshot.Sales.Count); Assert.All(snapshot.Sales, x => Assert.Equal(seed.Tenant,x.TenantId));
        var report = new InventoryDryRunAnalyzer().Analyze(snapshot);
        await Assert.ThrowsAsync<SnapshotLimitExceededException>(() => new ControlBalanceMaterializer(db).MaterializeAsync(new(seed.Tenant,seed.Location,Guid.NewGuid(),report.SourceFingerprint,[seed.Batch],"Reviewed maintenance bound","synthetic-copy",MaxRows:2)));
        var request = new ControlBalanceRequest(seed.Tenant,seed.Location,Guid.NewGuid(),report.SourceFingerprint,[seed.Batch],"Reviewed maintenance bound","synthetic-copy",MaxRows:2,MaxSaleRows:3);
        Assert.Single((await new ControlBalanceMaterializer(db).MaterializeAsync(request)).Batches);
        Assert.True((await new ControlBalanceMaterializer(db).MaterializeAsync(request)).Replayed);
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadControlOpeningAsync(seed.Tenant,seed.Location,2,500_001));
    }

    [ErpPostgresFact]
    public async Task Snapshot_and_report_leave_source_and_ERP_tables_untouched()
    {
        var seed = await SeedAsync();
        await using var db = fixture.Context();
        var snapshot = await new InventorySnapshotReader(db).ReadAsync(seed.Tenant, seed.Location);
        var report = new InventoryDryRunAnalyzer().Analyze(snapshot);
        var row = Assert.Single(report.Batches);
        Assert.Equal(5m, row.ReconstructedQuantity); Assert.False(report.ApplySupported);
        Assert.Equal(DateTimeKind.Utc, snapshot.CapturedAt.Kind); Assert.NotEmpty(snapshot.DatabaseSnapshot);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(5m, (await db.ProductStocks.SingleAsync(x => x.Id == seed.Batch)).Quantity);
        Assert.Equal(1, await db.StockMovements.CountAsync(x => x.TenantId == seed.Tenant));
        Assert.Equal(0, await db.BusinessDocuments.CountAsync(x => x.TenantId == seed.Tenant));
        Assert.Equal(0, await db.InventoryLedgerEntries.CountAsync(x => x.TenantId == seed.Tenant));
        Assert.Equal(0, await db.InventoryReconciliations.CountAsync(x => x.TenantId == seed.Tenant));
        Assert.Equal(0, await db.OutboxEvents.CountAsync(x => x.TenantId == seed.Tenant));
    }

    [ErpPostgresFact]
    public async Task Explicit_scope_excludes_foreign_tenant_and_other_location_transfer_rows()
    {
        var own = await SeedAsync(); var foreign = await SeedAsync();
        await using (var setup = fixture.Context())
        {
            var other = new Location { TenantId = own.Tenant, Name = "Other location" };
            var batch = new ProductStock { TenantId = own.Tenant, ProductId = own.Item, StoreId = other.Id,
                Quantity = 100, QuantityInitial = 100, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) };
            setup.AddRange(other, batch, new StockMovement { TenantId = own.Tenant, MovementType = "transfer", ProductId = own.Item,
                ProductStockId = batch.Id, FromStoreId = own.Location, ToStoreId = other.Id, Quantity = 100, QuantityBefore = 0, QuantityAfter = 100 });
            await setup.SaveChangesAsync();
        }
        await using var db = fixture.Context();
        await db.Database.OpenConnectionAsync();
        var role = $"SET ROLE {fixture.ReaderRole}"; await db.Database.ExecuteSqlRawAsync(role);
        await db.Database.ExecuteSqlRawAsync("SET app.role='worker'");
        var snapshot = await new InventorySnapshotReader(db).ReadAsync(own.Tenant, own.Location);
        Assert.Equal(own.Batch, Assert.Single(snapshot.Batches).Id);
        Assert.Single(snapshot.Movements);
        Assert.All(snapshot.Items, item => Assert.Equal(own.Tenant, item.TenantId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new InventorySnapshotReader(db).ReadAsync(own.Tenant, foreign.Location));
    }

    [ErpPostgresFact]
    public async Task Foreign_item_identity_is_flagged_without_loading_its_name_or_cost()
    {
        var own = await SeedAsync(); var foreign = await SeedAsync();
        await using (var setup = fixture.Context())
        {
            // Legacy FK checks Item.Id, not tenant. W3 must diagnose this, not create a W2 row.
            setup.ProductStocks.Add(new ProductStock { TenantId = own.Tenant, ProductId = foreign.Item, StoreId = own.Location,
                Quantity = 2, QuantityInitial = 2, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) });
            await setup.SaveChangesAsync();
        }
        await using var db = fixture.Context();
        var snapshot = await new InventorySnapshotReader(db).ReadAsync(own.Tenant, own.Location);
        Assert.DoesNotContain(snapshot.Items, item => item.Id == foreign.Item);
        var row = new InventoryDryRunAnalyzer().Analyze(snapshot).Batches.Single(x => x.ItemId == foreign.Item);
        Assert.Null(row.CurrentItemName); Assert.Equal("blocked", row.Recommendation); Assert.Null(row.ControlOpeningIdentity);
    }

    [ErpPostgresFact]
    public async Task Read_limit_aborts_instead_of_producing_a_partial_report()
    {
        var seed = await SeedAsync();
        await using (var setup = fixture.Context())
        {
            setup.ProductStocks.Add(new ProductStock { TenantId = seed.Tenant, ProductId = seed.Item, StoreId = seed.Location,
                Quantity = 1, QuantityInitial = 1, ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)) });
            await setup.SaveChangesAsync();
        }
        await using var db = fixture.Context();
        var error = await Assert.ThrowsAsync<SnapshotLimitExceededException>(() => new InventorySnapshotReader(db).ReadAsync(seed.Tenant, seed.Location, 1));
        Assert.Contains("batches", error.Message); Assert.Null(db.Database.CurrentTransaction);
    }

    [ErpPostgresFact]
    public async Task Read_only_is_enforced_by_PostgreSQL_and_stale_worker_bypass_is_cleared()
    {
        var seed = await SeedAsync();
        string? readOnly = null; string? isolation = null; string? role = null;
        var probe = new SnapshotProbe(async command =>
        {
            await using (var state = command.Connection!.CreateCommand())
            {
                state.Transaction = command.Transaction;
                state.CommandText = "SELECT current_setting('transaction_read_only'),current_setting('transaction_isolation'),current_setting('app.role',true)";
                await using var result = await state.ExecuteReaderAsync(); await result.ReadAsync();
                readOnly = result.GetString(0); isolation = result.GetString(1); role = result.GetString(2);
            }
            await using var write = command.Connection!.CreateCommand();
            write.Transaction = command.Transaction;
            write.CommandText = "UPDATE product_stock SET \"Quantity\"=\"Quantity\" WHERE \"Id\"=@id";
            var id = write.CreateParameter(); id.ParameterName = "id"; id.Value = seed.Batch; write.Parameters.Add(id);
            await write.ExecuteNonQueryAsync();
        });
        await using var db = fixture.Context(probe); await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET app.role='worker'");
        var error = await Assert.ThrowsAnyAsync<Exception>(() => new InventorySnapshotReader(db).ReadAsync(seed.Tenant, seed.Location));
        Assert.Equal("25006", Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
        Assert.Equal("on", readOnly); Assert.Equal("repeatable read", isolation); Assert.Equal("", role);
    }

    [ErpPostgresFact]
    public async Task Concurrent_commit_cannot_mix_old_balance_with_new_movements()
    {
        var seed = await SeedAsync();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new SnapshotProbe(async _ => { reached.TrySetResult(); await committed.Task.WaitAsync(TimeSpan.FromSeconds(20)); });
        await using var reader = fixture.Context(probe);
        var capture = new InventorySnapshotReader(reader).ReadAsync(seed.Tenant, seed.Location);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            await using var writer = fixture.Context();
            var stock = await writer.ProductStocks.SingleAsync(b => b.Id == seed.Batch); stock.Quantity = 4;
            writer.StockMovements.Add(new StockMovement { TenantId = seed.Tenant, ProductId = seed.Item, ProductStockId = seed.Batch,
                MovementType = "write_off", FromStoreId = seed.Location, Quantity = 1, QuantityBefore = 5, QuantityAfter = 4 });
            await writer.SaveChangesAsync();
        }
        finally { committed.TrySetResult(); }
        var snapshot = await capture;
        Assert.Equal(5m, Assert.Single(snapshot.Batches).Quantity); Assert.Single(snapshot.Movements);
        Assert.Equal("movement_history_candidate", Assert.Single(new InventoryDryRunAnalyzer().Analyze(snapshot).Batches).HistoryStatus);
        await using var verify = fixture.Context();
        Assert.Equal(4m, (await verify.ProductStocks.SingleAsync(b => b.Id == seed.Batch)).Quantity);
        Assert.Equal(2, await verify.StockMovements.CountAsync(m => m.TenantId == seed.Tenant));
    }

    [ErpPostgresFact]
    public async Task Null_batch_movement_survives_read_and_is_reported_as_unallocated()
    {
        var seed = await SeedAsync();
        await using (var setup = fixture.Context())
        {
            setup.StockMovements.Add(new StockMovement { TenantId = seed.Tenant, ProductId = seed.Item, MovementType = "adjustment",
                FromStoreId = seed.Location, Quantity = 1, QuantityBefore = 2, QuantityAfter = 1 });
            await setup.SaveChangesAsync();
        }
        await using var db = fixture.Context();
        var snapshot = await new InventorySnapshotReader(db).ReadAsync(seed.Tenant, seed.Location);
        Assert.Equal(2, snapshot.Movements.Count);
        var report = new InventoryDryRunAnalyzer().Analyze(snapshot);
        Assert.Contains(report.Issues, x => x.Code == "movement_batch_unresolved");
        Assert.Equal("unverifiable", Assert.Single(report.Batches).HistoryStatus);
    }

    private async Task<Seed> SeedAsync()
    {
        var tenant = Tenant.Create("Synthetic W3 preflight", Guid.NewGuid().ToString("N"));
        var location = new Location { TenantId = tenant.Id, Name = "W3 test location" };
        var item = new Item { TenantId = tenant.Id, Name = "Current W3 item", PricePurchase = 9876m };
        var source = Guid.NewGuid(); var at = DateTime.UtcNow;
        var batch = new ProductStock { TenantId = tenant.Id, ProductId = item.Id, StoreId = location.Id, Quantity = 5,
            QuantityInitial = 5, SourceType = "receipt", SourceId = source, AddedAt = at,
            ExpiryDate = DateOnly.FromDateTime(at.AddDays(30)), BatchNumber = "synthetic-w3" };
        var root = new StockMovement { TenantId = tenant.Id, ProductId = item.Id, ProductStockId = batch.Id, MovementType = "receipt",
            ToStoreId = location.Id, Quantity = 5, QuantityBefore = 0, QuantityAfter = 5, ReferenceType = "stock_receipt", ReferenceId = source, CreatedAt = at };
        await using var db = fixture.Context(); db.AddRange(tenant, location, item, batch, root); await db.SaveChangesAsync();
        return new(tenant.Id, location.Id, item.Id, batch.Id);
    }

    private sealed record Seed(Guid Tenant, Guid Location, Guid Item, Guid Batch);
    private sealed class SnapshotProbe(Func<DbCommand, Task> inspect) : DbCommandInterceptor
    {
        private int _visited;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM product_stock", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _visited, 1) == 0) await inspect(command);
            return result;
        }
    }
}
