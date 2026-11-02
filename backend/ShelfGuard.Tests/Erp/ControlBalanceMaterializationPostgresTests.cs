using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Backfill;
using ShelfGuard.Application.Features.Erp.Contracts;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class ControlBalanceMaterializationPostgresTests(ErpSchemaFixture fixture)
{
    [ErpPostgresFact]
    public async Task Selected_batches_commit_a_complete_graph_with_captured_units_and_no_legacy_or_financial_changes()
    {
        var s = await SeedAsync(2); var request = await RequestAsync(s);
        await using var db = fixture.Context();
        var before = await SourceAsync(db, s);
        var result = await new ControlBalanceMaterializer(db).MaterializeAsync(request);
        Assert.False(result.Replayed); Assert.Equal(2, result.Batches.Count); Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(before, await SourceAsync(db, s));
        Assert.Equal(0, await db.StockMovements.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(0, await db.StockEvents.CountAsync(x => x.TenantId == s.Tenant));
        foreach (var row in result.Batches)
        {
            var document = await db.BusinessDocuments.AsNoTracking().SingleAsync(x => x.Id == row.DocumentId);
            Assert.Equal("posted", document.Status); Assert.Equal("control_opening_balance", document.DocumentType);
            Assert.Equal("legacy.control_balance", document.SourceSystem); Assert.Equal(result.AsOf, document.OccurredAt);
            var line = await db.DocumentLines.AsNoTracking().SingleAsync(x => x.DocumentId == row.DocumentId);
            Assert.Equal("кг", line.Unit); Assert.Equal("Current control item", line.ItemNameSnapshot);
            Assert.Null(line.UnitPrice); Assert.Null(line.UnitCost); Assert.Null(line.VatRate); Assert.Null(line.VatAmount);
            var entry = await db.InventoryLedgerEntries.AsNoTracking().SingleAsync(x => x.Id == row.EntryId);
            Assert.Equal(row.Quantity, entry.QuantityDelta); Assert.Equal(0m, entry.QuantityBefore); Assert.Equal(row.Quantity, entry.QuantityAfter);
            Assert.Equal(result.AsOf, entry.OccurredAt); Assert.True(entry.OccurredAt > s.AddedAt);
            Assert.Null(entry.UnitCost); Assert.Null(entry.ValueDelta);
            var projection = await db.InventoryBalanceProjections.AsNoTracking().SingleAsync(x => x.BatchId == row.BatchId);
            Assert.Equal(row.Quantity, projection.Quantity); Assert.Equal(0m, projection.ReservedQuantity); Assert.Equal(row.EntryId, projection.LastLedgerEntryId);
            var outbox = await db.OutboxEvents.AsNoTracking().SingleAsync(x => x.Id == row.MessageId);
            Assert.Equal("historical", outbox.DeliveryScope); Assert.Equal("suppressed", outbox.Status);
            Assert.Equal(request.RunId, outbox.CorrelationId); ErpEventContracts.FromOutbox(outbox);
            var reconciliation = await db.InventoryReconciliations.AsNoTracking().SingleAsync(x => x.RunId == request.RunId && x.BatchId == row.BatchId);
            Assert.Equal("matched", reconciliation.Status); Assert.Equal(0m, reconciliation.Difference);
            Assert.Contains("synthetic-staging-acceptance", reconciliation.Explanation);
        }
        var after = new InventoryDryRunAnalyzer().Analyze(await new InventorySnapshotReader(db).ReadAsync(s.Tenant, s.Location));
        Assert.All(after.Batches, x => { Assert.Equal("matched", x.LedgerStatus); Assert.Null(x.ControlOpeningIdentity); });
    }

    [ErpPostgresFact]
    public async Task Replay_acknowledges_original_commit_even_after_legacy_activity_without_reopening_or_repairing()
    {
        var s = await SeedAsync(2); var request = await RequestAsync(s);
        var first = await ApplyAsync(request);
        await using (var legacy = fixture.Context())
        {
            var stock = await legacy.ProductStocks.SingleAsync(x => x.Id == s.Batches[0]); stock.Quantity += 1;
            await legacy.SaveChangesAsync();
        }
        var replay = await ApplyAsync(request with { BatchIds = request.BatchIds.Reverse().ToArray(), MaxRows = 1 });
        Assert.True(replay.Replayed); Assert.Equal(first.AsOf, replay.AsOf); Assert.Equal(first.Batches, replay.Batches);
        await using var verify = fixture.Context();
        Assert.Equal(2, await verify.BusinessDocuments.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(2, await verify.InventoryReconciliations.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(3.25m, (await verify.InventoryBalanceProjections.SingleAsync(x => x.BatchId == s.Batches[0])).Quantity);
        Assert.Equal(4.25m, (await verify.ProductStocks.SingleAsync(x => x.Id == s.Batches[0])).Quantity);
    }

    [ErpPostgresFact]
    public async Task Ukrainian_audit_fields_fit_schema_limits_and_survive_exact_replay()
    {
        var s = await SeedAsync(); var r = (await RequestAsync(s)) with { Reason = new string('ї', 500), AcceptanceReference = new string('є', 300) };
        await ApplyAsync(r); Assert.True((await ApplyAsync(r)).Replayed);
        await using var db = fixture.Context();
        var receipt = await db.InventoryReconciliations.SingleAsync(x => x.TenantId == s.Tenant);
        Assert.Contains(r.Reason, receipt.Explanation); Assert.True(receipt.Explanation!.Length <= 2000);
    }

    [ErpPostgresTheory]
    [InlineData("fingerprint")]
    [InlineData("reason")]
    [InlineData("selection")]
    [InlineData("acceptance")]
    public async Task Reused_run_with_different_parameters_is_rejected(string changed)
    {
        var s = await SeedAsync(2); var r = await RequestAsync(s); await ApplyAsync(r);
        r = changed switch { "fingerprint" => r with { SourceFingerprint = new string('b', 64) },
            "reason" => r with { Reason = "other" }, "selection" => r with { BatchIds = [s.Batches[0]] },
            _ => r with { AcceptanceReference = "other" } };
        await Assert.ThrowsAsync<MaterializationConflictException>(() => ApplyAsync(r));
        await using var db = fixture.Context(); Assert.Equal(2, await db.InventoryLedgerEntries.CountAsync(x => x.TenantId == s.Tenant));
    }

    [ErpPostgresTheory]
    [InlineData("quantity")]
    [InlineData("unit")]
    public async Task Stale_preflight_is_rejected_before_any_write(string changed)
    {
        var s = await SeedAsync(); var r = await RequestAsync(s);
        await using (var source = fixture.Context())
        {
            if (changed == "unit") (await source.Items.SingleAsync(x => x.Id == s.Item)).Unit = "л";
            else (await source.ProductStocks.SingleAsync(x => x.Id == s.Batches[0])).Quantity += 1;
            await source.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<MaterializationConflictException>(() => ApplyAsync(r)); await AssertNoGraphAsync(s);
    }

    [ErpPostgresFact]
    public async Task Fresh_run_cannot_add_a_second_opening_to_an_already_backfilled_batch()
    {
        var s = await SeedAsync(); await ApplyAsync(await RequestAsync(s));
        var second = await RequestAsync(s);
        await Assert.ThrowsAsync<MaterializationConflictException>(() => ApplyAsync(second));
        await using var db = fixture.Context(); Assert.Equal(1, await db.InventoryLedgerEntries.CountAsync(x => x.TenantId == s.Tenant));
    }

    [ErpPostgresTheory]
    [InlineData("zero")]
    [InlineData("foreign")]
    [InlineData("projection")]
    [InlineData("reservation_batch")]
    [InlineData("reservation_item")]
    public async Task Ineligible_selection_or_reserves_rejects_all_selected_batches(string invalid)
    {
        var s = await SeedAsync(2); var selection = s.Batches.ToArray();
        await using (var setup = fixture.Context())
        {
            if (invalid == "zero") (await setup.ProductStocks.SingleAsync(x => x.Id == s.Batches[1])).Quantity = 0;
            if (invalid == "foreign") selection[1] = (await SeedAsync()).Batches[0];
            if (invalid == "projection") setup.Add(new InventoryBalanceProjection { TenantId = s.Tenant, ItemId = s.Item,
                LocationId = s.Location, BatchId = s.Batches[1], Quantity = 0 });
            if (invalid.StartsWith("reservation"))
            {
                var doc = new BusinessDocument { TenantId = s.Tenant, DocumentType = "reservation_source" };
                setup.Add(doc); await setup.SaveChangesAsync();
                var line = new DocumentLine { TenantId = s.Tenant, DocumentId = doc.Id, LineNumber = 1, ItemId = s.Item,
                    ToLocationId = s.Location, Quantity = 1, ItemNameSnapshot = "Reserved item" };
                setup.Add(line); await setup.SaveChangesAsync();
                setup.Add(new InventoryReservation { TenantId = s.Tenant, ItemId = s.Item, LocationId = s.Location,
                    DocumentId = doc.Id, DocumentLineId = line.Id, BatchId = invalid == "reservation_batch" ? s.Batches[1] : null,
                    Quantity = 1, RemainingQuantity = 1, IdempotencyKey = Guid.NewGuid().ToString("N") });
            }
            await setup.SaveChangesAsync();
        }
        var request = (await RequestAsync(s)) with { BatchIds = selection };
        await Assert.ThrowsAsync<MaterializationConflictException>(() => ApplyAsync(request));
        await using var verify = fixture.Context(); Assert.Equal(0, await verify.DocumentPostings.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(0, await verify.InventoryLedgerEntries.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(0, await verify.InventoryReconciliations.CountAsync(x => x.TenantId == s.Tenant));
    }

    [ErpPostgresFact]
    public async Task Failure_after_ledger_insert_rolls_back_every_target_and_same_run_can_retry()
    {
        var s = await SeedAsync(2); var r = await RequestAsync(s); var ledgerStaged = false;
        var fail = new Probe(async command =>
        {
            if (!command.CommandText.Contains("INSERT INTO outbox_events", StringComparison.Ordinal)) return;
            await using var count = command.Connection!.CreateCommand(); count.Transaction = command.Transaction;
            count.CommandText = "SELECT count(*) FROM inventory_ledger_entries WHERE \"TenantId\"=@tenant";
            var p = count.CreateParameter(); p.ParameterName = "tenant"; p.Value = s.Tenant; count.Parameters.Add(p);
            ledgerStaged = (long)(await count.ExecuteScalarAsync())! == 2;
            throw new IOException("Synthetic failure before outbox save");
        });
        await using (var db = fixture.Context(fail))
            await Assert.ThrowsAnyAsync<Exception>(() => new ControlBalanceMaterializer(db).MaterializeAsync(r));
        Assert.True(ledgerStaged); await AssertNoGraphAsync(s);
        Assert.False((await ApplyAsync(r)).Replayed);
    }

    [ErpPostgresFact]
    public async Task Active_legacy_writer_prevents_materialization_without_wait_or_partial_writes()
    {
        var s = await SeedAsync(); var r = await RequestAsync(s);
        await using var legacy = fixture.Context(); await using var transaction = await legacy.Database.BeginTransactionAsync();
        await legacy.Database.ExecuteSqlRawAsync("LOCK TABLE product_stock IN ROW EXCLUSIVE MODE");
        await Assert.ThrowsAsync<MaterializationConflictException>(() => ApplyAsync(r));
        await transaction.RollbackAsync(); await AssertNoGraphAsync(s);
    }

    [ErpPostgresFact]
    public async Task Materializer_blocks_competing_materializer_and_noncooperating_legacy_write_until_atomic_commit()
    {
        var s = await SeedAsync(); var r = await RequestAsync(s);
        var staged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new Probe(async command =>
        {
            if (!command.CommandText.Contains("INSERT INTO business_documents", StringComparison.Ordinal)) return;
            staged.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
        });
        await using var first = fixture.Context(probe);
        var running = new ControlBalanceMaterializer(first).MaterializeAsync(r);
        await staged.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            await Assert.ThrowsAsync<MaterializationConflictException>(() => ApplyAsync(r));
            await using var legacy = fixture.Context(); await using var tx = await legacy.Database.BeginTransactionAsync();
            await legacy.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='150ms'");
            var error = await Assert.ThrowsAsync<PostgresException>(() => legacy.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE product_stock SET \"Quantity\"=\"Quantity\"+1 WHERE \"Id\"={s.Batches[0]}"));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
        }
        finally { release.TrySetResult(); }
        Assert.False((await running).Replayed); Assert.True((await ApplyAsync(r)).Replayed);
    }

    [ErpPostgresFact]
    public async Task Ordinary_role_can_materialize_own_scope_but_stale_worker_bypass_never_admits_foreign_location()
    {
        var own = await SeedAsync(); var foreign = await SeedAsync(); var r = await RequestAsync(own);
        await using var db = fixture.Context(); await db.Database.OpenConnectionAsync();
        var roleCommand = $"SET ROLE {fixture.ReaderRole}"; // Random fixture-owned identifier, never caller input.
        await db.Database.ExecuteSqlRawAsync(roleCommand);
        await db.Database.ExecuteSqlRawAsync("SET app.role='worker'");
        var materializer = new ControlBalanceMaterializer(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => materializer.MaterializeAsync(r with { LocationId = foreign.Location }));
        var result = await materializer.MaterializeAsync(r); Assert.Single(result.Batches);
        await using var verify = fixture.Context();
        Assert.Equal(0, await verify.BusinessDocuments.CountAsync(x => x.TenantId == foreign.Tenant));
    }

    private async Task<ControlBalanceResult> ApplyAsync(ControlBalanceRequest r)
    {
        await using var db = fixture.Context(); return await new ControlBalanceMaterializer(db).MaterializeAsync(r);
    }
    private async Task<ControlBalanceRequest> RequestAsync(Seed s)
    {
        await using var db = fixture.Context();
        var report = new InventoryDryRunAnalyzer().Analyze(await new InventorySnapshotReader(db).ReadAsync(s.Tenant, s.Location));
        return new(s.Tenant, s.Location, Guid.NewGuid(), report.SourceFingerprint, s.Batches,
            "Synthetic reviewed opening; historical allocation unavailable", "synthetic-staging-acceptance");
    }
    private async Task<Seed> SeedAsync(int count = 1)
    {
        var tenant = Tenant.Create("W3B synthetic", Guid.NewGuid().ToString("N"));
        var location = new Location { TenantId = tenant.Id, Name = "W3B location" };
        var item = new Item { TenantId = tenant.Id, Name = "Current control item", Unit = "кг", PricePurchase = 99, PriceRetail = 111, VatRate = 7 };
        var at = DateTime.UtcNow.AddDays(-5);
        var batches = Enumerable.Range(0, count).Select(i => new ProductStock { TenantId = tenant.Id, ProductId = item.Id,
            StoreId = location.Id, Quantity = 3.25m + i, QuantityInitial = 9, AddedAt = at, BatchNumber = "w3b-lot",
            ExpiryDate = new DateOnly(2026, 12, 1) }).ToArray();
        await using var db = fixture.Context(); db.AddRange(tenant, location, item); db.AddRange(batches); await db.SaveChangesAsync();
        return new(tenant.Id, location.Id, item.Id, batches.Select(b => b.Id).ToArray(), at);
    }
    private static async Task<string> SourceAsync(AppDbContext db, Seed s) => await db.Database.SqlQuery<string>(
        $"SELECT md5(string_agg(to_jsonb(p)::text,',' ORDER BY p.\"Id\")) AS \"Value\" FROM product_stock p WHERE p.\"TenantId\"={s.Tenant}").SingleAsync();
    private async Task AssertNoGraphAsync(Seed s)
    {
        await using var db = fixture.Context();
        Assert.Equal(0, await db.BusinessDocuments.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(0, await db.DocumentPostings.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(0, await db.InventoryLedgerEntries.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(0, await db.InventoryBalanceProjections.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(0, await db.InventoryReconciliations.CountAsync(x => x.TenantId == s.Tenant));
        Assert.Equal(0, await db.OutboxEvents.CountAsync(x => x.TenantId == s.Tenant));
    }
    private sealed record Seed(Guid Tenant, Guid Location, Guid Item, Guid[] Batches, DateTime AddedAt);
    private sealed class Probe(Func<DbCommand, Task> inspect) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { await inspect(command); return result; }
    }
}
