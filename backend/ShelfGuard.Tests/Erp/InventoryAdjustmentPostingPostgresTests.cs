using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Backfill;
using ShelfGuard.Application.Features.Erp.Contracts;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class InventoryAdjustmentPostingPostgresTests(ErpSchemaFixture fixture)
{
    [ErpPostgresTheory]
    [InlineData("3.25", "4.50")]
    [InlineData("3.25", "1.10")]
    [InlineData("3.25", "0")]
    [InlineData("0", "2.25")]
    public async Task Adjustment_commits_one_complete_graph_and_preserves_batch_metadata(string beforeText, string afterText)
    {
        var before = decimal.Parse(beforeText, System.Globalization.CultureInfo.InvariantCulture);
        var after = decimal.Parse(afterText, System.Globalization.CultureInfo.InvariantCulture);
        var s = await SeedAsync(before); var plan = Plan(s, before, after);
        var result = await ApplyAsync(plan);
        await using var db = fixture.Context();
        var stock = await db.ProductStocks.AsNoTracking().SingleAsync(x => x.Id == s.Batch);
        Assert.Equal(after, stock.Quantity); Assert.Equal(9, stock.QuantityInitial);
        Assert.Equal("w4a-lot", stock.BatchNumber); Assert.Equal(new DateOnly(2027, 1, 1), stock.ExpiryDate);
        Assert.Equal(s.Zone, stock.ZoneId); Assert.Equal(3, stock.ShelfNumber);
        Assert.False(result.Replayed); Assert.Equal(before, result.QuantityBefore); Assert.Equal(after, result.QuantityAfter);
        var document = await db.BusinessDocuments.SingleAsync(d => d.Id == result.DocumentId);
        Assert.Equal("posted", document.Status); Assert.Equal(s.Actor, document.CreatedBy);
        var approval = await db.DocumentApprovals.SingleAsync(a => a.DocumentId == document.Id);
        Assert.Equal(s.Actor, approval.ApproverId); Assert.Equal(document.ContentHash, approval.ContentHash);
        var line = await db.DocumentLines.SingleAsync(l => l.DocumentId == document.Id);
        Assert.Equal("кг", line.Unit); Assert.Equal("Борошно", line.ItemNameSnapshot);
        Assert.Equal(Math.Abs(after - before), line.Quantity);
        Assert.Null(line.UnitCost); Assert.Null(line.UnitPrice); Assert.Null(line.VatRate); Assert.Null(line.VatAmount);
        var entry = await db.InventoryLedgerEntries.SingleAsync(e => e.Id == result.EntryId);
        Assert.Equal(after - before, entry.QuantityDelta); Assert.Null(entry.UnitCost); Assert.Null(entry.ValueDelta);
        var projection = await db.InventoryBalanceProjections.SingleAsync(p => p.BatchId == s.Batch);
        Assert.Equal(after, projection.Quantity); Assert.Equal(entry.Id, projection.LastLedgerEntryId);
        Assert.Equal(after, await db.InventoryLedgerEntries.Where(e => e.TenantId == s.Tenant && e.BatchId == s.Batch).SumAsync(e => e.QuantityDelta));
        var message = await db.OutboxEvents.SingleAsync(e => e.Id == result.EventId);
        Assert.Equal("live", message.DeliveryScope); Assert.Equal("pending", message.Status); Assert.Equal(0, message.Attempts);
        var envelope = ErpEventContracts.FromOutbox(message);
        Assert.Equal(plan.Request.RequestId, envelope.CorrelationId);
        var movement = await db.StockMovements.SingleAsync(m => m.ReferenceId == document.Id);
        Assert.Equal(before, movement.QuantityBefore); Assert.Equal(after, movement.QuantityAfter);
        Assert.Equal(s.Actor, movement.PerformedBy); Assert.Equal(plan.Request.Reason, movement.Notes);
        var repeated = await ApplyAsync(plan);
        Assert.Equal(result with { Replayed = true }, repeated);
        Assert.Equal(1, await db.StockMovements.CountAsync(m => m.ReferenceId == document.Id));
    }

    [ErpPostgresFact]
    public async Task Replay_returns_the_original_receipt_after_later_adjustments()
    {
        var s = await SeedAsync(); var first = Plan(s, 3.25m, 5);
        var original = await ApplyAsync(first);
        await ApplyAsync(Plan(s, 5, 8));
        Assert.Equal(original with { Replayed = true }, await ApplyAsync(first));
        await using var db = fixture.Context();
        Assert.Equal(8, (await db.ProductStocks.SingleAsync(x => x.Id == s.Batch)).Quantity);
        Assert.Equal(2, await db.DocumentPostings.CountAsync(p => p.TenantId == s.Tenant && p.IdempotencyKey.StartsWith("inventory.adjustment.v1:")));
    }

    [ErpPostgresTheory]
    [InlineData("target")]
    [InlineData("expected")]
    [InlineData("reason")]
    [InlineData("batch")]
    public async Task Reusing_request_identity_with_changed_parameters_is_a_conflict(string field)
    {
        var s = await SeedAsync(); var first = Plan(s, 3.25m, 5); await ApplyAsync(first);
        var changed = field switch { "target" => first.Request with { Quantity = 7 }, "expected" => first.Request with { ExpectedQuantity = 2 },
            "reason" => first.Request with { Reason = "Інша причина" }, _ => first.Request with { BatchId = Guid.NewGuid() } };
        await Assert.ThrowsAsync<InventoryPostingConflictException>(() => ApplyAsync(InventoryAdjustmentPlanner.Prepare(s.Tenant, s.Actor, changed)));
        await AssertAdjustmentCountAsync(s, 1, 5);
    }

    [ErpPostgresFact]
    public async Task Stale_expected_quantity_does_not_create_a_document_or_overwrite_the_new_count()
    {
        var s = await SeedAsync(); await ApplyAsync(Plan(s, 3.25m, 5));
        await Assert.ThrowsAsync<InventoryPostingConflictException>(() => ApplyAsync(Plan(s, 3.25m, 2)));
        await AssertAdjustmentCountAsync(s, 1, 5);
    }

    [ErpPostgresTheory]
    [InlineData("legacy")]
    [InlineData("projection")]
    [InlineData("missing_projection")]
    [InlineData("missing_zero_projection")]
    public async Task Drift_and_missing_provenance_are_rejected_before_any_new_posting(string kind)
    {
        var s = await SeedAsync(); var expected = 3.25m; var target = 5m; var priorCount = 0;
        if (kind == "missing_zero_projection") { await ApplyAsync(Plan(s, 3.25m, 0)); expected = 0; target = 2; priorCount = 1; }
        await using (var db = fixture.Context())
        {
            if (kind == "legacy")
            {
                // Owner-only fault injection in the disposable fixture, to exercise drift detection behind an intact fence.
                await using var tx = await db.Database.BeginTransactionAsync();
                var trigger = InventoryFenceTestHelper.BeforeTrigger(s.Tenant, s.Location);
                var afterTrigger = trigger.Replace("erp_w4_b_", "erp_w4_a_", StringComparison.Ordinal);
                var disable = $"ALTER TABLE public.product_stock DISABLE TRIGGER {trigger}";
                var enable = $"ALTER TABLE public.product_stock ENABLE TRIGGER {trigger}";
                var disableAfter = $"ALTER TABLE public.product_stock DISABLE TRIGGER {afterTrigger}";
                var enableAfter = $"ALTER TABLE public.product_stock ENABLE TRIGGER {afterTrigger}";
                await db.Database.ExecuteSqlRawAsync(disable);
                await db.Database.ExecuteSqlRawAsync(disableAfter);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"Quantity\"=4 WHERE \"Id\"={s.Batch}");
                await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
                await db.Database.ExecuteSqlRawAsync(enable);
                await db.Database.ExecuteSqlRawAsync(enableAfter);
                await tx.CommitAsync();
            }
            else if (kind == "projection") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory_balance_projections SET \"Quantity\"=4 WHERE \"BatchId\"={s.Batch}");
            else await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM inventory_balance_projections WHERE \"BatchId\"={s.Batch}");
        }
        await Assert.ThrowsAsync<InventoryPostingConflictException>(() => ApplyAsync(Plan(s, expected, target)));
        await AssertAdjustmentCountAsync(s, priorCount, kind == "legacy" ? 4 : expected);
    }

    [ErpPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_batch_or_item_reservations_prevent_adjustment(bool batchless)
    {
        var s = await SeedAsync();
        await using (var db = fixture.Context())
        {
            var document = new BusinessDocument { TenantId = s.Tenant, DocumentType = "reservation_source" };
            db.Add(document); await db.SaveChangesAsync();
            var line = new DocumentLine { TenantId = s.Tenant, DocumentId = document.Id, ItemId = s.Item,
                ToLocationId = s.Location, Quantity = 1, LineNumber = 1, ItemNameSnapshot = "Reserved" };
            db.Add(line); await db.SaveChangesAsync();
            db.Add(new InventoryReservation { TenantId = s.Tenant, DocumentId = document.Id, DocumentLineId = line.Id,
                ItemId = s.Item, LocationId = s.Location, BatchId = batchless ? null : s.Batch,
                Quantity = 1, RemainingQuantity = 1, IdempotencyKey = Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InventoryPostingConflictException>(() => ApplyAsync(Plan(s, 3.25m, 2)));
        await AssertAdjustmentCountAsync(s, 0, 3.25m);
    }

    [ErpPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_before_outbox_or_after_all_writes_rolls_back_the_graph_stock_and_movement(bool atCommit)
    {
        var s = await SeedAsync(); var plan = Plan(s, 3.25m, 5); var observed = false;
        IInterceptor probe = atCommit ? new CommitFailure(async transaction =>
        {
            await using var command = transaction.Connection!.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT count(*) FROM outbox_events WHERE \"PostingId\" IN (SELECT \"Id\" FROM document_postings WHERE \"IdempotencyKey\"=@key)";
            var parameter = command.CreateParameter(); parameter.ParameterName = "key"; parameter.Value = plan.IdempotencyKey; command.Parameters.Add(parameter);
            observed = (long)(await command.ExecuteScalarAsync())! == 1;
            throw new IOException("Synthetic failure after final SaveChanges, before commit");
        }) : new Probe(command =>
        {
            if (command.CommandText.Contains("INSERT INTO outbox_events", StringComparison.Ordinal))
            { observed = true; throw new IOException("Synthetic outbox failure"); }
            return Task.CompletedTask;
        });
        await using (var db = fixture.Context(probe))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Store(db, s).PostAsync(plan));
            Assert.Empty(db.ChangeTracker.Entries()); Assert.Null(db.Database.CurrentTransaction);
            await db.SaveChangesAsync(); // A losing request cannot commit staged work later.
        }
        Assert.True(observed); await AssertAdjustmentCountAsync(s, 0, 3.25m);
        Assert.False((await ApplyAsync(plan)).Replayed);
    }

    [ErpPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_identical_requests_replay_and_distinct_stale_requests_conflict(bool identical)
    {
        var s = await SeedAsync(); var plan = Plan(s, 3.25m, 5);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new Probe(async command =>
        {
            if (!command.CommandText.Contains("INSERT INTO business_documents", StringComparison.Ordinal)) return;
            paused.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
        });
        await using var one = fixture.Context(probe);
        var first = Store(one, s).PostAsync(plan);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Task<InventoryAdjustmentReceipt> second;
        try
        {
            second = ApplyAsync(identical ? plan : Plan(s, 3.25m, 7));
            await Task.Delay(150); Assert.False(second.IsCompleted);
        }
        finally { release.TrySetResult(); }
        var result = await first;
        if (identical) Assert.Equal(result with { Replayed = true }, await second);
        else await Assert.ThrowsAsync<InventoryPostingConflictException>(() => second);
        await AssertAdjustmentCountAsync(s, 1, 5);
    }

    [ErpPostgresFact]
    public async Task Batch_lock_excludes_an_uncoordinated_legacy_update_during_posting()
    {
        var s = await SeedAsync(); var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var db = fixture.Context(new Probe(async command =>
        {
            if (!command.CommandText.Contains("INSERT INTO business_documents", StringComparison.Ordinal)) return;
            paused.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }));
        var first = Store(db, s).PostAsync(Plan(s, 3.25m, 5));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            await using var legacy = fixture.Context(); await using var tx = await legacy.Database.BeginTransactionAsync();
            await legacy.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='150ms'");
            var error = await Assert.ThrowsAsync<PostgresException>(() => legacy.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE product_stock SET \"Quantity\"=9 WHERE \"Id\"={s.Batch}"));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
        }
        finally { release.TrySetResult(); }
        await first; await AssertAdjustmentCountAsync(s, 1, 5);
    }

    [ErpPostgresFact]
    public async Task Lock_timeout_is_a_retryable_conflict_and_the_same_request_can_succeed_after_release()
    {
        var s = await SeedAsync(); var plan = Plan(s, 3.25m, 5);
        await using var legacy = fixture.Context();
        await using (var tx = await legacy.Database.BeginTransactionAsync())
        {
            await legacy.Database.ExecuteSqlInterpolatedAsync($"UPDATE product_stock SET \"Quantity\"=\"Quantity\" WHERE \"Id\"={s.Batch}");
            await Assert.ThrowsAsync<InventoryPostingConflictException>(() => ApplyAsync(plan));
            await tx.RollbackAsync();
        }
        await AssertAdjustmentCountAsync(s, 0, 3.25m);
        Assert.False((await ApplyAsync(plan)).Replayed);
    }

    [ErpPostgresFact]
    public async Task Cancellation_during_posting_rolls_back_and_clears_the_context()
    {
        var s = await SeedAsync(); var plan = Plan(s, 3.25m, 5);
        using var cancelled = new CancellationTokenSource();
        await using var db = fixture.Context(new Probe(command =>
        {
            if (command.CommandText.Contains("INSERT INTO inventory_ledger_entries", StringComparison.Ordinal)) cancelled.Cancel();
            return Task.CompletedTask;
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store(db, s).PostAsync(plan, cancelled.Token));
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Null(db.Database.CurrentTransaction);
        await db.SaveChangesAsync();
        await AssertAdjustmentCountAsync(s, 0, 3.25m);
        Assert.False((await ApplyAsync(plan)).Replayed);
    }

    [ErpPostgresTheory]
    [InlineData("user")]
    [InlineData("tenant")]
    [InlineData("module")]
    [InlineData("location")]
    [InlineData("assignment")]
    [InlineData("role")]
    public async Task Store_rechecks_live_actor_tenant_module_and_location_before_replay_or_write(string revoked)
    {
        var s = await SeedAsync(); var plan = Plan(s, 3.25m, 5);
        await using (var db = fixture.Context())
        {
            if (revoked == "user") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET \"IsActive\"=false WHERE \"Id\"={s.Actor}");
            if (revoked == "tenant") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tenants SET \"IsActive\"=false WHERE \"Id\"={s.Tenant}");
            if (revoked == "module") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tenants SET \"Modules\"='[]'::jsonb WHERE \"Id\"={s.Tenant}");
            if (revoked == "location") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE locations SET \"IsActive\"=false WHERE \"Id\"={s.Location}");
            if (revoked == "assignment") await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM user_locations WHERE \"UserId\"={s.Actor}");
            if (revoked == "role") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET \"Role\"='cashier' WHERE \"Id\"={s.Actor}");
        }
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => ApplyAsync(plan));
        await AssertAdjustmentCountAsync(s, 0, 3.25m);
    }

    [ErpPostgresFact]
    public async Task Ordinary_role_posts_own_scope_and_inherited_worker_context_does_not_admit_a_foreign_batch()
    {
        var own = await SeedAsync(); var foreign = await SeedAsync();
        await using var db = fixture.Context(); await db.Database.OpenConnectionAsync();
        var roleCommand = $"SET ROLE {fixture.ReaderRole}"; // Random fixture-owned role identifier, never request input.
        await db.Database.ExecuteSqlRawAsync(roleCommand);
        await db.Database.ExecuteSqlRawAsync("SET app.role='worker'");
        var plan = Plan(own, 3.25m, 5);
        Assert.False((await Store(db, own).PostAsync(plan)).Replayed);
        var invalid = InventoryAdjustmentPlanner.Prepare(own.Tenant, own.Actor, plan.Request with { RequestId = Guid.NewGuid(), BatchId = foreign.Batch });
        await Assert.ThrowsAsync<InventoryPostingNotFoundException>(() => Store(db, own).PostAsync(invalid));
        await AssertAdjustmentCountAsync(own, 1, 5); await AssertAdjustmentCountAsync(foreign, 0, 3.25m);
    }

    [ErpPostgresFact]
    public async Task Disabled_scope_forged_plan_dirty_context_and_ambient_transaction_fail_closed()
    {
        var s = await SeedAsync(); var plan = Plan(s, 3.25m, 5);
        await using var db = fixture.Context();
        await Assert.ThrowsAsync<InventoryPostingDisabledException>(() => new InventoryAdjustmentStore(db,
            new InventoryPostingGate(false, []), TimeProvider.System).PostAsync(plan));
        await Assert.ThrowsAsync<ArgumentException>(() => Store(db, s).PostAsync(plan with { RequestHash = new string('0', 64) }));
        await db.ProductStocks.SingleAsync(x => x.Id == s.Batch);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(db, s).PostAsync(plan));
        db.ChangeTracker.Clear();
        await using (var tx = await db.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(db, s).PostAsync(plan));
        await AssertAdjustmentCountAsync(s, 0, 3.25m);
    }

    private InventoryAdjustmentStore Store(AppDbContext db, Seed s) => new(db,
        new InventoryPostingGate(true, [new(s.Tenant, s.Location)]), TimeProvider.System);
    private async Task<InventoryAdjustmentReceipt> ApplyAsync(InventoryAdjustmentPlan plan)
    {
        await using var db = fixture.Context();
        var result = await new InventoryAdjustmentStore(db, new InventoryPostingGate(true,
            [new(plan.TenantId, plan.Request.LocationId)]), TimeProvider.System).PostAsync(plan);
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Null(db.Database.CurrentTransaction);
        return result;
    }
    private static InventoryAdjustmentPlan Plan(Seed s, decimal before, decimal after) => InventoryAdjustmentPlanner.Prepare(s.Tenant,
        s.Actor, new(Guid.NewGuid(), s.Location, s.Batch, before, after, "Звірено фізичний залишок"));
    private async Task<Seed> SeedAsync(decimal quantity = 3.25m)
    {
        var tenant = Tenant.Create("W4A synthetic", Guid.NewGuid().ToString("N")); tenant.UpdateModules(["inventory"]);
        var location = new Location { TenantId = tenant.Id, Name = "W4A store" };
        var zone = new LocationZone { LocationId = location.Id, Name = "W4A shelf" };
        var item = new Item { TenantId = tenant.Id, Name = "Борошно", Unit = "кг", PricePurchase = 99, PriceRetail = 111, VatRate = 7 };
        var user = User.Create(tenant.Id, Guid.NewGuid().ToString("N") + "@test.local", "W4A manager", "hash", "store_manager");
        var stock = new ProductStock { TenantId = tenant.Id, StoreId = location.Id, ZoneId = zone.Id, ShelfNumber = 3,
            ProductId = item.Id, Quantity = quantity, QuantityInitial = 9, BatchNumber = "w4a-lot", ExpiryDate = new(2027, 1, 1) };
        await using (var db = fixture.Context())
        {
            db.AddRange(tenant, location, zone, item, user, stock, UserLocation.Create(tenant.Id, user.Id, location.Id, null));
            await db.SaveChangesAsync();
        }
        var seed = new Seed(tenant.Id, user.Id, location.Id, item.Id, zone.Id, stock.Id);
        if (quantity > 0)
        {
            await using var db = fixture.Context();
            var snapshot = await new InventorySnapshotReader(db).ReadAsync(tenant.Id, location.Id, operatorId: user.Id);
            var report = new InventoryDryRunAnalyzer().Analyze(snapshot);
            await new ControlBalanceMaterializer(db).MaterializeAsync(new(tenant.Id, location.Id, Guid.NewGuid(), report.SourceFingerprint,
                [stock.Id], "Synthetic reviewed control baseline", "w4a-disposable-acceptance", OperatorId: user.Id));
        }
        await using (var db = fixture.Context())
            await InventoryFenceTestHelper.Activate(db, tenant.Id, location.Id, user.Id);
        return seed;
    }
    private async Task AssertAdjustmentCountAsync(Seed s, int count, decimal quantity)
    {
        await using var db = fixture.Context();
        Assert.Equal(quantity, (await db.ProductStocks.AsNoTracking().SingleAsync(p => p.Id == s.Batch)).Quantity);
        Assert.Equal(count, await db.BusinessDocuments.CountAsync(d => d.TenantId == s.Tenant && d.DocumentType == "stock_count_adjustment"));
        Assert.Equal(count, await db.DocumentPostings.CountAsync(p => p.TenantId == s.Tenant && p.IdempotencyKey.StartsWith("inventory.adjustment.v1:")));
        Assert.Equal(count, await db.OutboxEvents.CountAsync(p => p.TenantId == s.Tenant && p.DeliveryScope == "live"));
        Assert.Equal(count, await db.StockMovements.CountAsync(p => p.TenantId == s.Tenant));
    }
    private sealed record Seed(Guid Tenant, Guid Actor, Guid Location, Guid Item, Guid Zone, Guid Batch);
    private sealed class Probe(Func<DbCommand, Task> inspect) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { await inspect(command); return result; }
    }
    private sealed class CommitFailure(Func<DbTransaction, Task> inspect) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        { await inspect(transaction); return result; }
    }
}
