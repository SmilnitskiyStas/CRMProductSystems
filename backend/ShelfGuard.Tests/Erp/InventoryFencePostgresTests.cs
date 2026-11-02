using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Infrastructure.Migrations;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class InventoryFencePostgresTests(ErpSchemaFixture fixture)
{
    [ErpPostgresFact]
    public async Task Gate_alone_cannot_enable_an_unfenced_scope()
    {
        var s = await InventoryFenceScenario.Seed(fixture, false);
        await using var db = fixture.Context();
        await Assert.ThrowsAsync<InventoryPostingConflictException>(() => Store(db, s).PostAsync(s.Plan()));
        await Count(s, 3.25m, 0);
    }

    [ErpPostgresFact]
    public async Task Activation_is_atomic_idempotent_and_refuses_changed_identity_or_reason()
    {
        var s = await InventoryFenceScenario.Seed(fixture, false); var id = Guid.NewGuid();
        await using var db = fixture.Context(); var fingerprint = await InventoryFenceTestHelper.Fingerprint(db, s.Tenant, s.Location);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await InventoryFenceTestHelper.Activate(db, s.Tenant, s.Location, s.Actor, id, fingerprint);
            Assert.True(await Ready(db, s)); await tx.RollbackAsync();
        }
        Assert.False(await Ready(db, s));
        Assert.Equal(id, await InventoryFenceTestHelper.Activate(db, s.Tenant, s.Location, s.Actor, id, fingerprint));
        Assert.Equal(id, await InventoryFenceTestHelper.Activate(db, s.Tenant, s.Location, s.Actor, id, fingerprint));
        await Assert.ThrowsAsync<PostgresException>(() => InventoryFenceTestHelper.Activate(db, s.Tenant, s.Location, s.Actor, id, fingerprint, "changed reason"));
        Assert.True(await Ready(db, s));
        Assert.Throws<NotSupportedException>(() => _ = new AddInventoryPostingScopeFence().DownOperations);
    }

    [ErpPostgresFact]
    public async Task Activation_blocks_on_old_writers_and_rejects_drift_or_stale_fingerprint()
    {
        var s = await InventoryFenceScenario.Seed(fixture, false);
        await using var db = fixture.Context(); var hash = await InventoryFenceTestHelper.Fingerprint(db, s.Tenant, s.Location);
        await using (var legacy = fixture.Context())
        await using (var tx = await legacy.Database.BeginTransactionAsync())
        {
            await legacy.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"Quantity\"=\"Quantity\" WHERE \"Id\"={s.Batch}");
            var error = await Record.ExceptionAsync(() => InventoryFenceTestHelper.Activate(db, s.Tenant, s.Location, s.Actor, fingerprint: hash));
            Assert.NotNull(error);
            var pg = error as PostgresException ?? error.InnerException as PostgresException;
            Assert.NotNull(pg); Assert.Equal(PostgresErrorCodes.LockNotAvailable, pg.SqlState); await tx.RollbackAsync();
        }
        await Assert.ThrowsAsync<PostgresException>(() => InventoryFenceTestHelper.Activate(db, s.Tenant, s.Location, s.Actor, fingerprint: new string('0', 64)));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"Quantity\"=4 WHERE \"Id\"={s.Batch}");
        await Assert.ThrowsAsync<PostgresException>(() => InventoryFenceTestHelper.Activate(db, s.Tenant, s.Location, s.Actor));
        Assert.False(await Ready(db, s));
    }

    [ErpPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transactions_whose_snapshot_predates_activation_cannot_bypass_the_fence(bool serializable)
    {
        var s = await InventoryFenceScenario.Seed(fixture, false);
        await using var old = fixture.Context();
        await using var tx = await old.Database.BeginTransactionAsync(serializable ? IsolationLevel.Serializable : IsolationLevel.RepeatableRead);
        _ = await old.ProductStocks.AsNoTracking().SingleAsync(p => p.Id == s.Batch);
        await old.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM inventory_posting_scopes WHERE \"TenantId\"={s.Tenant}").SingleAsync();
        await using (var admin = fixture.Context()) await InventoryFenceTestHelper.Activate(admin, s.Tenant, s.Location, s.Actor);
        var error = await Assert.ThrowsAsync<PostgresException>(() => old.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"Quantity\"=6 WHERE \"Id\"={s.Batch}"));
        Assert.Equal("P4F01", error.SqlState); await tx.RollbackAsync(); await Count(s, 3.25m, 0);
    }

    [ErpPostgresTheory]
    [InlineData("quantity")]
    [InlineData("identity")]
    [InlineData("initial")]
    [InlineData("delete")]
    [InlineData("insert")]
    public async Task Legacy_writes_are_blocked_with_no_partial_changes(string operation)
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var db = fixture.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            if (operation == "quantity") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"Quantity\"=9 WHERE \"Id\"={s.Batch}");
            if (operation == "identity") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"ProductId\"={Guid.NewGuid()} WHERE \"Id\"={s.Batch}");
            if (operation == "initial") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"QuantityInitial\"=9 WHERE \"Id\"={s.Batch}");
            if (operation == "delete") await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM product_stock WHERE \"Id\"={s.Batch}");
            if (operation == "insert") await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO product_stock (\"Id\",\"TenantId\",\"LocationId\",\"ProductId\",\"Quantity\",\"QuantityInitial\",\"ExpiryDate\") VALUES ({Guid.NewGuid()},{s.Tenant},{s.Location},{s.Item},0,0,'2027-01-01')");
        });
        Assert.Equal("P4F01", error.SqlState); await Count(s, 3.25m, 0);
    }

    [ErpPostgresFact]
    public async Task Outside_scopes_and_nonquantity_metadata_updates_remain_usable()
    {
        var s = await InventoryFenceScenario.Seed(fixture); var outside = await InventoryFenceScenario.Seed(fixture, false);
        await using var db = fixture.Context();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"ShelfNumber\"=7,\"Status\"='warning' WHERE \"Id\"={s.Batch}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"Quantity\"=9 WHERE \"Id\"={outside.Batch}");
        Assert.Equal(7, (await db.ProductStocks.AsNoTracking().SingleAsync(p => p.Id == s.Batch)).ShelfNumber);
        Assert.Equal(9m, (await db.ProductStocks.AsNoTracking().SingleAsync(p => p.Id == outside.Batch)).Quantity);
        await Count(s, 3.25m, 0);
    }

    [ErpPostgresTheory]
    [InlineData("true")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public async Task A_boolean_or_random_posting_marker_is_not_a_permit(string marker)
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var db = fixture.Context(); await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.role','worker',true),set_config('app.erp_posting_id',{marker},true)");
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"Quantity\"=5 WHERE \"Id\"={s.Batch}"));
        Assert.Equal("P4F01", error.SqlState); await tx.RollbackAsync(); await Count(s, 3.25m, 0);
    }

    [ErpPostgresFact]
    public async Task Committed_posting_cannot_be_reused_and_turning_application_gate_off_keeps_the_database_fenced()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var db = fixture.Context();
        var result = await Store(db, s).PostAsync(s.Plan());
        await Assert.ThrowsAsync<InventoryPostingDisabledException>(() => new InventoryAdjustmentStore(db,
            new InventoryPostingGate(false, []), TimeProvider.System).PostAsync(s.Plan(5, 6)));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.erp_posting_id',{result.PostingId.ToString()},true),set_config('app.user_id',{s.Actor.ToString()},true)");
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"Quantity\"=6 WHERE \"Id\"={s.Batch}"));
        Assert.Equal("P4F01", error.SqlState); await tx.RollbackAsync(); await Count(s, 5, 1);
    }

    [ErpPostgresFact]
    public async Task Runtime_role_cannot_activate_or_erase_registry_even_if_it_was_overgranted_table_DML()
    {
        var s = await InventoryFenceScenario.Seed(fixture); await using var db = fixture.Context(); await db.Database.OpenConnectionAsync();
        var roleSql = $"SET ROLE {fixture.ReaderRole}"; await db.Database.ExecuteSqlRawAsync(roleSql);
        await db.Database.ExecuteSqlRawAsync("SET app.role='worker'");
        var activation = await Assert.ThrowsAsync<PostgresException>(() => InventoryFenceTestHelper.Activate(db, s.Tenant, s.Location, s.Actor));
        Assert.Equal("42501", activation.SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM inventory_posting_scopes WHERE \"TenantId\"={s.Tenant}"));
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE inventory_posting_scopes"));
        var insert = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO inventory_posting_scopes VALUES ({s.Tenant},{s.Location},{Guid.NewGuid()},{s.Actor},{new string('a',64)},'forged',now())"));
        Assert.Equal("42501", insert.SqlState);
    }

    [ErpPostgresFact]
    public async Task Clearing_the_marker_before_commit_rolls_back_stock_and_the_complete_graph()
    {
        var s = await InventoryFenceScenario.Seed(fixture);
        await using var db = fixture.Context(new ClearMarker());
        var error = await Assert.ThrowsAsync<PostgresException>(() => Store(db, s).PostAsync(s.Plan()));
        Assert.Equal("P4F01", error.SqlState); Assert.Empty(db.ChangeTracker.Entries()); await Count(s, 3.25m, 0);
    }

    private static InventoryAdjustmentStore Store(AppDbContext db, InventoryFenceScenario s) => new(db,
        new InventoryPostingGate(true, [new(s.Tenant, s.Location)]), TimeProvider.System);
    private static Task<bool> Ready(AppDbContext db, InventoryFenceScenario s) =>
        db.Database.SqlQuery<bool>($"SELECT public.erp_w4_scope_ready({s.Tenant},{s.Location}) AS \"Value\"").SingleAsync();
    private async Task Count(InventoryFenceScenario s, decimal quantity, int documents)
    {
        await using var db = fixture.Context();
        Assert.Equal(quantity, (await db.ProductStocks.AsNoTracking().SingleAsync(p => p.Id == s.Batch)).Quantity);
        Assert.Equal(documents, await db.BusinessDocuments.CountAsync(d => d.TenantId == s.Tenant && d.DocumentType == "stock_count_adjustment"));
    }
    private sealed class ClearMarker : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        {
            await using var command = transaction.Connection!.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SET LOCAL app.erp_posting_id=''"; await command.ExecuteNonQueryAsync(ct); return result;
        }
    }
}
