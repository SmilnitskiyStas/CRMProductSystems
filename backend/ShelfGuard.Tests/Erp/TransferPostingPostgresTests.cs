using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Backfill;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;

namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class TransferPostingPostgresTests(ErpSchemaFixture fixture)
{
    internal sealed record Scenario(InventoryFenceScenario Source, Guid Destination, TransferDispatchCommand Command)
    {
        internal WorkAccessScope Scope => new(Source.Tenant, Source.Actor, "store_manager", false, [Source.Location, Destination], new HashSet<string>(), new Dictionary<string, bool>());
        internal IInventoryPostingGate Gate(bool enabled = true) => new InventoryPostingGate(enabled, [new(Source.Tenant, Source.Location), new(Source.Tenant, Destination)]);
    }
    internal static async Task<Scenario> Seed(ErpSchemaFixture fixture, bool destinationFence = true)
    {
        var source = await InventoryFenceScenario.Seed(fixture);
        var location = new Location { TenantId = source.Tenant, Name = "Synthetic receiving warehouse" };
        var stock = new ProductStock { TenantId = source.Tenant, StoreId = location.Id, ProductId = source.Item, Quantity = 1, QuantityInitial = 1, ExpiryDate = new(2028, 1, 1) };
        await using (var db = fixture.Context()) { db.AddRange(location, stock, UserLocation.Create(source.Tenant, source.Actor, location.Id, null)); await db.SaveChangesAsync(); }
        await using (var db = fixture.Context())
        {
            var snapshot = await new InventorySnapshotReader(db).ReadAsync(source.Tenant, location.Id, operatorId: source.Actor);
            var report = new InventoryDryRunAnalyzer().Analyze(snapshot);
            await new ControlBalanceMaterializer(db).MaterializeAsync(new(source.Tenant, location.Id, Guid.NewGuid(), report.SourceFingerprint,
                [stock.Id], "Reviewed destination baseline", "transfer-disposable", OperatorId: source.Actor));
            if (destinationFence) await InventoryFenceTestHelper.Activate(db, source.Tenant, location.Id, source.Actor);
        }
        return new(source, location.Id, new(Guid.NewGuid(), source.Location, location.Id, "store_to_store", "Reviewed shipment", [new(source.Batch, 2.25m)]));
    }
    internal static TransferPostingStore Store(ErpSchemaFixture fixture, Scenario s, bool enabled = true, params IInterceptor[] interceptors)
    {
        var b = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(TestPostgres.DataSource(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!))
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (interceptors.Length > 0) b.AddInterceptors(interceptors);
        return new(b.Options, s.Gate(), TimeProvider.System, new(enabled));
    }
    internal static async Task<TransferDispatchCommand> Reviewed(TransferPostingStore store, Scenario s) =>
        s.Command with { ExpectedFingerprint = (await store.PreviewDispatchAsync(s.Scope, s.Command, default)).Fingerprint };
    [ErpPostgresFact]
    public async Task Dispatch_then_receive_conserves_quantity_and_original_batch_snapshots_and_replays_each_phase()
    {
        var s = await Seed(fixture); var store = Store(fixture, s); var command = await Reviewed(store, s);
        var dispatched = await store.DispatchAsync(s.Scope, command, default);
        Assert.Equal(dispatched with { Replayed = true }, await store.DispatchAsync(s.Scope, command, default));
        await using (var db = fixture.Context()) { Assert.Equal(1m, (await db.ProductStocks.SingleAsync(x => x.Id == s.Source.Batch)).Quantity); Assert.Equal("in_transit", (await db.StockTransfers.SingleAsync(x => x.Id == command.RequestId)).Status); }
        var preview = await store.PreviewAsync(s.Scope, command.RequestId, "receive", default);
        var received = await store.CompleteAsync(s.Scope, command.RequestId, "receive", new(preview.Fingerprint), default);
        Assert.Equal(received with { Replayed = true }, await store.CompleteAsync(s.Scope, command.RequestId, "receive", new(preview.Fingerprint), default));
        Assert.Equal(dispatched.PostingId, (await store.DispatchAsync(s.Scope, command, default)).PostingId);
        await using var check = fixture.Context();
        Assert.Equal(4.25m, await check.ProductStocks.Where(x => x.TenantId == s.Source.Tenant).SumAsync(x => x.Quantity));
        var incoming = await check.ProductStocks.SingleAsync(x => x.SourceId == command.RequestId);
        Assert.Equal("w4b-lot", incoming.BatchNumber); Assert.Equal(new DateOnly(2027, 12, 1), incoming.ExpiryDate);
        Assert.Equal(2.25m, incoming.Quantity); Assert.Equal("received", (await check.StockTransfers.SingleAsync(x => x.Id == command.RequestId)).Status);
        Assert.Equal(2, await check.DocumentPostings.CountAsync(x => x.TenantId == s.Source.Tenant && x.IdempotencyKey.StartsWith("transfer.")));
        Assert.Single(await check.DocumentLinks.Where(x => x.TenantId == s.Source.Tenant && x.RelationType == "receives").ToListAsync());
    }
    [ErpPostgresFact]
    public async Task Cancel_restores_original_batch_and_appends_linked_reversal_without_editing_dispatch()
    {
        var s = await Seed(fixture); var store = Store(fixture, s); var command = await Reviewed(store, s);
        var sent = await store.DispatchAsync(s.Scope, command, default);
        var preview = await store.PreviewAsync(s.Scope, command.RequestId, "cancel", default);
        var body = new TransferActionCommand(preview.Fingerprint, "Shipment recalled before receipt");
        var cancelled = await store.CompleteAsync(s.Scope, command.RequestId, "cancel", body, default);
        Assert.Equal(cancelled with { Replayed = true }, await store.CompleteAsync(s.Scope, command.RequestId, "cancel", body, default));
        await using var db = fixture.Context(); Assert.Equal(3.25m, (await db.ProductStocks.SingleAsync(x => x.Id == s.Source.Batch)).Quantity);
        Assert.Equal("cancelled", (await db.StockTransfers.SingleAsync(x => x.Id == command.RequestId)).Status);
        var entry = await db.InventoryLedgerEntries.SingleAsync(x => x.PostingId == cancelled.PostingId);
        Assert.Equal((await db.InventoryLedgerEntries.SingleAsync(x => x.PostingId == sent.PostingId)).Id, entry.ReversesEntryId);
        await Assert.ThrowsAsync<InventoryPostingConflictException>(() => store.CompleteAsync(s.Scope, command.RequestId, "cancel", body with { Reason = "Different reason" }, default));
    }
    [ErpPostgresFact]
    public async Task Concurrent_identical_dispatch_creates_one_transfer_and_posting()
    {
        var s = await Seed(fixture); var command = await Reviewed(Store(fixture, s), s);
        var results = await Task.WhenAll(Store(fixture, s).DispatchAsync(s.Scope, command, default), Store(fixture, s).DispatchAsync(s.Scope, command, default));
        Assert.Equal(results[0].PostingId, results[1].PostingId); Assert.Single(results, x => x.Replayed);
    }
    [ErpPostgresFact]
    public async Task Receive_cancel_race_has_one_terminal_winner_and_conserves_total()
    {
        var s = await Seed(fixture); var store = Store(fixture, s); var command = await Reviewed(store, s); await store.DispatchAsync(s.Scope, command, default);
        var receive = await store.PreviewAsync(s.Scope, command.RequestId, "receive", default); var cancel = await store.PreviewAsync(s.Scope, command.RequestId, "cancel", default);
        async Task<bool> Attempt(string phase, TransferActionCommand body) { try { await Store(fixture, s).CompleteAsync(s.Scope, command.RequestId, phase, body, default); return true; } catch (InventoryPostingConflictException) { return false; } }
        var results = await Task.WhenAll(Attempt("receive", new(receive.Fingerprint)), Attempt("cancel", new(cancel.Fingerprint, "Recall shipment")));
        Assert.Single(results, x => x); await using var db = fixture.Context(); Assert.Equal(4.25m, await db.ProductStocks.Where(x => x.TenantId == s.Source.Tenant).SumAsync(x => x.Quantity));
    }
    [ErpPostgresTheory]
    [InlineData(false, true)] [InlineData(true, false)]
    public async Task Separate_writer_gate_and_both_fences_are_required(bool enabled, bool destinationFence)
    {
        var s = await Seed(fixture, destinationFence); var store = Store(fixture, s, enabled); var command = await Reviewed(store, s);
        if (!enabled) await Assert.ThrowsAsync<InventoryPostingDisabledException>(() => store.DispatchAsync(s.Scope, command, default));
        else await Assert.ThrowsAsync<InventoryPostingConflictException>(() => store.DispatchAsync(s.Scope, command, default));
        await Empty(s);
    }
    [ErpPostgresFact]
    public async Task Changed_review_cannot_consume_stock_or_create_a_header()
    {
        var s = await Seed(fixture); var store = Store(fixture, s); var command = await Reviewed(store, s);
        await Assert.ThrowsAsync<InventoryPostingConflictException>(() => store.DispatchAsync(s.Scope, command with { Notes = "Unreviewed change" }, default)); await Empty(s);
    }
    [ErpPostgresFact]
    public async Task Destination_only_receiver_can_receive_but_cannot_cancel_or_dispatch_from_source()
    {
        var s = await Seed(fixture); var store = Store(fixture, s); var command = await Reviewed(store, s); await store.DispatchAsync(s.Scope, command, default);
        var user = User.Create(s.Source.Tenant, Guid.NewGuid()+"@test.local", "Destination receiver", "hash", "storekeeper");
        await using (var db = fixture.Context()) { db.AddRange(user, UserLocation.Create(s.Source.Tenant, user.Id, s.Destination, null)); await db.SaveChangesAsync(); }
        var scope = s.Scope with { UserId = user.Id, Role = "storekeeper", LocationIds = [s.Destination] };
        var preview = await store.PreviewAsync(scope, command.RequestId, "receive", default);
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => store.PreviewAsync(scope, command.RequestId, "cancel", default));
        await Assert.ThrowsAsync<WorkAccessDeniedException>(() => store.PreviewDispatchAsync(scope, s.Command, default));
        await store.CompleteAsync(scope, command.RequestId, "receive", new(preview.Fingerprint), default);
    }
    [ErpPostgresTheory]
    [InlineData("movement")] [InlineData("projection")] [InlineData("marker")] [InlineData("metadata")]
    public async Task Incomplete_dispatch_graph_rolls_back_header_stock_and_ledger(string fault)
    {
        var s = await Seed(fixture); var command = await Reviewed(Store(fixture, s), s);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Store(fixture, s, true, new Sabotage(fault)).DispatchAsync(s.Scope, command, default));
        var pg = error as PostgresException ?? error.InnerException as PostgresException; Assert.NotNull(pg); Assert.Equal("P4F01", pg.SqlState); await Empty(s);
    }
    [ErpPostgresTheory]
    [InlineData("header")] [InlineData("line")] [InlineData("truncate")]
    public async Task Committed_transfer_header_lines_and_history_cannot_be_rewritten_by_legacy_SQL(string target)
    {
        var s = await Seed(fixture); var store = Store(fixture, s); var command = await Reviewed(store, s); await store.DispatchAsync(s.Scope, command, default);
        await using var db = fixture.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(async () => {
            if (target == "header") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE stock_transfers SET \"Status\"='cancelled' WHERE \"Id\"={command.RequestId}");
            if (target == "line") await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_transfer_items WHERE \"TransferId\"={command.RequestId}");
            if (target == "truncate") await db.Database.ExecuteSqlRawAsync("TRUNCATE stock_transfer_items");
        }); Assert.Equal("P4F01", error.SqlState);
    }
    private async Task Empty(Scenario s) { await using var db = fixture.Context(); Assert.False(await db.StockTransfers.AnyAsync(t => t.Id == s.Command.RequestId)); Assert.Equal(3.25m, (await db.ProductStocks.SingleAsync(x => x.Id == s.Source.Batch)).Quantity); }
    [ErpPostgresTheory]
    [InlineData("receive","movement")] [InlineData("receive","projection")] [InlineData("receive","metadata")]
    [InlineData("cancel","movement")] [InlineData("cancel","projection")] [InlineData("cancel","metadata")]
    public async Task Terminal_fault_rolls_back_the_whole_operation_and_retains_in_transit_allocation(string phase,string fault)
    {
        var s=await Seed(fixture);var store=Store(fixture,s);var command=await Reviewed(store,s);var dispatched=await store.DispatchAsync(s.Scope,command,default);
        var preview=await store.PreviewAsync(s.Scope,command.RequestId,phase,default);
        var error=await Assert.ThrowsAnyAsync<Exception>(()=>Store(fixture,s,true,new Sabotage(fault,phase)).CompleteAsync(s.Scope,command.RequestId,phase,new(preview.Fingerprint,phase=="cancel"?"Shipment recalled":null),default));
        var pg=error as PostgresException??error.InnerException as PostgresException;Assert.NotNull(pg);Assert.Equal("P4F01",pg.SqlState);
        await using var db=fixture.Context();Assert.Equal("in_transit",(await db.StockTransfers.SingleAsync(t=>t.Id==command.RequestId)).Status);
        Assert.Equal("posted",(await db.BusinessDocuments.SingleAsync(d=>d.Id==dispatched.DocumentId)).Status);
        Assert.Equal(1m,(await db.ProductStocks.SingleAsync(x=>x.Id==s.Source.Batch)).Quantity);
        Assert.False(await db.ProductStocks.AnyAsync(x=>x.SourceId==command.RequestId));
    }
    [ErpPostgresTheory] [InlineData(false)] [InlineData(true)]
    public async Task Live_transfer_permission_denial_can_only_be_overridden_by_a_current_grant(bool grant)
    {
        var s=await Seed(fixture);var store=Store(fixture,s);var command=await Reviewed(store,s);
        await using(var db=fixture.Context()) { var u=await db.Users.SingleAsync(x=>x.Id==s.Source.Actor);u.SetPermissions(new(){{"transfers",false}});
          if(grant)db.Add(UserPermissionGrant.Create(s.Source.Tenant,u.Id,"transfers",DateTime.UtcNow.AddHours(1),u.Id));await db.SaveChangesAsync(); }
        if(grant)await store.DispatchAsync(s.Scope,command,default);
        else {await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>store.DispatchAsync(s.Scope,command,default));await Empty(s);}
    }
    [ErpPostgresFact]
    public async Task Two_distinct_shipments_cannot_oversell_one_reviewed_batch()
    {
        var s=await Seed(fixture);var first=await Reviewed(Store(fixture,s),s);
        var second=s.Command with {RequestId=Guid.NewGuid()};second=second with {ExpectedFingerprint=(await Store(fixture,s).PreviewDispatchAsync(s.Scope,second,default)).Fingerprint};
        async Task<bool> Attempt(TransferDispatchCommand c){try{await Store(fixture,s).DispatchAsync(s.Scope,c,default);return true;}catch(InventoryPostingConflictException){return false;}}
        var results=await Task.WhenAll(Attempt(first),Attempt(second));Assert.Single(results,x=>x);
        await using var db=fixture.Context();Assert.Equal(1m,(await db.ProductStocks.SingleAsync(x=>x.Id==s.Source.Batch)).Quantity);
        Assert.Equal(1,await db.StockTransfers.CountAsync(x=>x.TenantId==s.Source.Tenant));
    }
    [ErpPostgresFact]
    public async Task Multi_batch_receipt_transfer_and_cancel_preserve_confirmed_cost_and_exact_value()
    {
        var s=await Seed(fixture);Guid receiptId;string fingerprint;
        await using(var db=fixture.Context())
        {
            (await db.Tenants.SingleAsync(t=>t.Id==s.Source.Tenant)).UpdateModules(["inventory","procurement"]);
            var r=new StockReceipt{TenantId=s.Source.Tenant,DestinationStoreId=s.Source.Location,Status="ordered",CreatedBy=s.Source.Actor};
            r.Items.Add(new(){ReceiptId=r.Id,ProductId=s.Source.Item,QuantityOrdered=2.25m,PricePurchase=12.50m,ExpiryDate=new(2028,11,1),BatchNumber="priced-A"});
            r.Items.Add(new(){ReceiptId=r.Id,ProductId=s.Source.Item,QuantityOrdered=3.25m,PricePurchase=18.99m,ExpiryDate=new(2028,12,1),BatchNumber="priced-B"});
            db.Add(r);await db.SaveChangesAsync();receiptId=r.Id;
            var loaded=await db.StockReceipts.AsNoTracking().Include(x=>x.Items).ThenInclude(x=>x.Product).SingleAsync(x=>x.Id==r.Id);fingerprint=ReceiptPostingRules.Fingerprint(loaded);
        }
        await ReceiptPostingPostgresTests.Store(fixture,new(s.Source,receiptId,fingerprint)).ReceiveAsync(s.Scope,receiptId,fingerprint,default);
        List<TransferDispatchItem> batchRequests;
        await using(var db=fixture.Context())batchRequests=await db.ProductStocks.Where(x=>x.SourceId==receiptId).OrderBy(x=>x.Id).Select(x=>new TransferDispatchItem(x.Id,1.25m)).ToListAsync();
        var command=s.Command with {Items=batchRequests};var store=Store(fixture,s);command=command with {ExpectedFingerprint=(await store.PreviewDispatchAsync(s.Scope,command,default)).Fingerprint};
        var sent=await store.DispatchAsync(s.Scope,command,default);var cancel=await store.PreviewAsync(s.Scope,command.RequestId,"cancel",default);
        Assert.True(cancel.SupportsDesktopReview);
        Assert.Equal(new[] { "12.5000", "18.9900" }, cancel.Lines.OrderBy(x => x.UnitCost).Select(x => x.ExactUnitCost));
        Assert.All(cancel.Lines, line => Assert.False(string.IsNullOrWhiteSpace(line.QualityStatus)));
        var returned=await store.CompleteAsync(s.Scope,command.RequestId,"cancel",new(cancel.Fingerprint,"Recall all batches"),default);
        await using var check=fixture.Context();var outgoing=await check.InventoryLedgerEntries.Where(x=>x.PostingId==sent.PostingId).OrderBy(x=>x.UnitCost).ToListAsync();
        var incoming=await check.InventoryLedgerEntries.Where(x=>x.PostingId==returned.PostingId).OrderBy(x=>x.UnitCost).ToListAsync();
        Assert.Equal(new decimal?[]{12.50m,18.99m},outgoing.Select(x=>x.UnitCost));Assert.Equal(new decimal?[]{-15.625m,-23.7375m},outgoing.Select(x=>x.ValueDelta));
        Assert.Equal(outgoing.Select(x=>-x.ValueDelta),incoming.Select(x=>x.ValueDelta));Assert.Equal("reversed",(await check.BusinessDocuments.SingleAsync(x=>x.Id==sent.DocumentId)).Status);
    }
    [ErpPostgresTheory] [InlineData(false)] [InlineData(true)]
    public async Task Active_reservation_at_either_location_blocks_dispatch(bool destination)
    {
        var s=await Seed(fixture);var store=Store(fixture,s);var command=await Reviewed(store,s);
        await using(var db=fixture.Context())
        {
            var d=new ShelfGuard.Domain.Entities.Erp.BusinessDocument{TenantId=s.Source.Tenant,DocumentType="reservation_source"};db.Add(d);await db.SaveChangesAsync();
            var line=new ShelfGuard.Domain.Entities.Erp.DocumentLine{TenantId=s.Source.Tenant,DocumentId=d.Id,LineNumber=1,ItemId=s.Source.Item,Quantity=1,ToLocationId=destination?s.Destination:s.Source.Location,ItemNameSnapshot="Reserved transfer item"};db.Add(line);await db.SaveChangesAsync();
            db.Add(new ShelfGuard.Domain.Entities.Erp.InventoryReservation{TenantId=s.Source.Tenant,DocumentId=d.Id,DocumentLineId=line.Id,ItemId=s.Source.Item,
                LocationId=destination?s.Destination:s.Source.Location,Quantity=1,RemainingQuantity=1,IdempotencyKey=Guid.NewGuid().ToString("N")});await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.DispatchAsync(s.Scope,command,default));await Empty(s);
    }
    [ErpPostgresFact]
    public async Task A_foreign_tenant_cannot_preview_or_complete_an_existing_transfer()
    {
        var s=await Seed(fixture);var foreign=await Seed(fixture);var store=Store(fixture,s);var command=await Reviewed(store,s);
        await store.DispatchAsync(s.Scope,command,default);
        await Assert.ThrowsAsync<InventoryPostingNotFoundException>(()=>Store(fixture,foreign).PreviewAsync(foreign.Scope,command.RequestId,"receive",default));
        await Assert.ThrowsAsync<InventoryPostingNotFoundException>(()=>Store(fixture,foreign).CompleteAsync(foreign.Scope,command.RequestId,"receive",new(new string('a',64)),default));
    }
    private sealed class Sabotage(string fault,string phase="dispatch") : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            var db = (AppDbContext)data.Context!;
            if (!db.ChangeTracker.Entries<ShelfGuard.Domain.Entities.Erp.BusinessDocument>().Any(x => x.Entity.Status == "posted" && x.Entity.SourceSystem == TransferPostingRules.Source(phase))) return result;
            if (fault == "movement") await db.Database.ExecuteSqlRawAsync("DELETE FROM stock_movements WHERE \"MovementType\"='transfer'", ct);
            if (fault == "projection") await db.Database.ExecuteSqlRawAsync("DELETE FROM inventory_balance_projections WHERE \"LastLedgerEntryId\" IN (SELECT \"Id\" FROM inventory_ledger_entries WHERE \"PostingId\"=current_setting('app.erp_posting_id')::uuid)", ct);
            if (fault == "marker") await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.erp_posting_id','',true)", ct);
            if (fault == "metadata") await db.Database.ExecuteSqlRawAsync("UPDATE product_stock SET \"ShelfNumber\"=42 WHERE \"Id\" IN (SELECT \"BatchId\" FROM inventory_ledger_entries WHERE \"PostingId\"=current_setting('app.erp_posting_id')::uuid)", ct);
            return result;
        }
    }
}
