using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Backfill;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class WriteOffPostingPostgresTests(ErpSchemaFixture fixture)
{
    [ErpPostgresFact]
    public async Task Desktop_draft_replay_rejects_replaced_product_even_when_line_count_and_quantities_match()
    {
        var s=await Seed(fixture);var item=new Item{TenantId=s.Inventory.Tenant,Name="Second draft product",Unit="kg"};
        await using(var db=fixture.Context()){db.Add(item);await db.SaveChangesAsync();}
        var command=new WriteOffDraftCommand(Guid.NewGuid(),s.Inventory.Location,"expired",null,[new(s.Inventory.Item,1m),new(item.Id,1m)]);
        var store=Store(s);await store.CreateDraftAsync(s.Scope,command,default);
        await using(var db=fixture.Context())
            await db.WriteOffItems.Where(i=>i.WriteOffId==command.RequestId&&i.ProductId==item.Id).ExecuteUpdateAsync(set=>set.SetProperty(i=>i.ProductId,s.Inventory.Item));
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.CreateDraftAsync(s.Scope,command,default));
    }
    [ErpPostgresFact]
    public async Task Desktop_draft_concurrent_replay_creates_one_source_without_stock_posting()
    {
        var s = await Seed(fixture); var command = new WriteOffDraftCommand(Guid.NewGuid(), s.Inventory.Location, "damaged", "Reviewed damaged packaging", [new(s.Inventory.Item, 1.25m)]);
        var results = await Task.WhenAll(Store(s).CreateDraftAsync(s.Scope,command,default),Store(s).CreateDraftAsync(s.Scope,command,default));
        Assert.Equal(results[0].WriteOffId, results[1].WriteOffId); Assert.Single(results,x=>x.Replayed);
        await using var db=fixture.Context(); var source=await db.WriteOffs.Include(x=>x.Items).SingleAsync(x=>x.Id==command.RequestId);
        Assert.Equal("pending_approval",source.Status); Assert.Null(source.TotalLossAmountPurchase); Assert.Null(source.TotalLossAmount);
        Assert.Null(Assert.Single(source.Items).ProductStockId); Assert.Null(source.Items.Single().UnitPricePurchase);
        Assert.False(await db.BusinessDocuments.AnyAsync(x=>x.SourceId==source.Id.ToString()));
        Assert.Equal(3.25m,(await db.ProductStocks.SingleAsync(x=>x.Id==s.Inventory.Batch)).Quantity);
    }
    [ErpPostgresFact]
    public async Task Desktop_draft_changed_intent_and_non_owner_cannot_reuse_identity()
    {
        var s=await Seed(fixture);var command=new WriteOffDraftCommand(Guid.NewGuid(),s.Inventory.Location,"damaged","Reviewed damaged packaging",[new(s.Inventory.Item,1m)]);
        var store=Store(s);await store.CreateDraftAsync(s.Scope,command,default);
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.CreateDraftAsync(s.Scope,command with{Items=[new(s.Inventory.Item,2m)]},default));
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.CreateDraftAsync(s.Scope,command with{Notes="Another reviewed explanation"},default));
        var other=User.Create(s.Inventory.Tenant,Guid.NewGuid()+"@test.local","Other manager","hash","store_manager");
        await using(var db=fixture.Context()){db.AddRange(other,UserLocation.Create(s.Inventory.Tenant,other.Id,s.Inventory.Location,null));await db.SaveChangesAsync();}
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.CreateDraftAsync(s.Scope with{UserId=other.Id},command,default));
    }
    [ErpPostgresFact]
    public async Task Desktop_draft_replays_after_approval_and_review_reports_quality_and_unknown_cost()
    {
        var s=await Seed(fixture);var command=new WriteOffDraftCommand(Guid.NewGuid(),s.Inventory.Location,"damaged","Reviewed damaged packaging",[new(s.Inventory.Item,1m)]);
        var store=Store(s);await store.CreateDraftAsync(s.Scope,command,default);
        var review=await store.PreviewAsync(s.Scope,command.RequestId,"approve",default);Assert.True(review.SupportsDesktopReview);
        Assert.All(review.Batches,x=>{Assert.NotNull(x.QualityStatus);Assert.Null(x.ExactUnitCost);});
        await store.PostAsync(s.Scope,command.RequestId,"approve",new(review.Fingerprint),default);
        Assert.True((await store.CreateDraftAsync(s.Scope,command,default)).Replayed);
    }
    [ErpPostgresFact]
    public async Task Desktop_draft_requires_real_manager_assignment_and_enabled_scope()
    {
        var s=await Seed(fixture);var command=new WriteOffDraftCommand(Guid.NewGuid(),s.Inventory.Location,"expired",null,[new(s.Inventory.Item,1m)]);
        await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Store(s).CreateDraftAsync(s.Scope with{Role="storekeeper"},command,default));
        await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Store(s).CreateDraftAsync(s.Scope with{LocationIds=[]},command,default));
    }
    internal sealed record Scenario(InventoryFenceScenario Inventory, Guid WriteOffId, Guid? SecondBatch)
    {
        internal WorkAccessScope Scope => new(Inventory.Tenant, Inventory.Actor, "store_manager", false, [Inventory.Location], new HashSet<string>(), new Dictionary<string,bool>());
        internal IInventoryPostingGate Gate => new InventoryPostingGate(true,[new(Inventory.Tenant,Inventory.Location)]);
    }
    internal static async Task<Scenario> Seed(ErpSchemaFixture f, bool multi=false, bool activate=true)
    {
        var tenant=Tenant.Create("Synthetic ERP write-off",Guid.NewGuid().ToString("N"));tenant.UpdateModules(["inventory"]);
        var loc=new Location{TenantId=tenant.Id,Name="Write-off warehouse"};var item=new Item{TenantId=tenant.Id,Name="Synthetic milk",Unit="l"};
        var user=User.Create(tenant.Id,Guid.NewGuid()+"@test.local","Write-off manager","hash","store_manager");
        var stock=new ProductStock{TenantId=tenant.Id,StoreId=loc.Id,ProductId=item.Id,Quantity=3.25m,QuantityInitial=3.25m,ExpiryDate=new(2027,12,1),BatchNumber="first-lot"};
        var second=multi?new ProductStock{TenantId=tenant.Id,StoreId=loc.Id,ProductId=item.Id,Quantity=4,QuantityInitial=4,ExpiryDate=new(2028,1,1),BatchNumber="second-lot"}:null;
        await using(var db=f.Context()) {db.AddRange(tenant,loc,item,user,stock,UserLocation.Create(tenant.Id,user.Id,loc.Id,null));if(second!=null)db.Add(second);await db.SaveChangesAsync();}
        await using(var db=f.Context())
        {
            var snapshot=await new InventorySnapshotReader(db).ReadAsync(tenant.Id,loc.Id,operatorId:user.Id);var report=new InventoryDryRunAnalyzer().Analyze(snapshot);
            var request=new ControlBalanceRequest(tenant.Id,loc.Id,Guid.NewGuid(),report.SourceFingerprint,second==null?[stock.Id]:[stock.Id,second.Id],"Reviewed synthetic baseline","writeoff-disposable",OperatorId:user.Id);
            // Maintenance uses NOWAIT. Only setup retries this exact ID/fingerprint after brief incidental locks;
            // posting/race assertions are not retried and production lock behavior is unchanged.
            for(var attempt=0;;attempt++)try{await new ControlBalanceMaterializer(db).MaterializeAsync(request);break;}
                catch(MaterializationConflictException)when(attempt<2){await Task.Delay(100*(attempt+1));}
            if(activate)
            {
                // Activation also uses NOWAIT. Retry only this seed's maintenance setup,
                // with the same identity/fingerprint; concurrency assertions never retry.
                var activation=Guid.NewGuid();var fingerprint=await InventoryFenceTestHelper.Fingerprint(db,tenant.Id,loc.Id);
                for(var attempt=0;;attempt++)try{await InventoryFenceTestHelper.Activate(db,tenant.Id,loc.Id,user.Id,activation,fingerprint);break;}
                    catch(Exception e)when(attempt<2&&(e as PostgresException??e.InnerException as PostgresException)?.SqlState=="55P03")
                    {await Task.Delay(100*(attempt+1));}
            }
        }
        var qty=multi?4.5m:2.25m;
        var w=new WriteOff{TenantId=tenant.Id,StoreId=loc.Id,Status="pending_approval",Reason="expired",Notes="Reviewed physical stock",CreatedBy=user.Id,
            TotalLossAmount=qty*20,TotalLossAmountPurchase=decimal.Round(qty*12.5m,2,MidpointRounding.AwayFromZero),TotalReimbursementAmount=0};
        w.Items.Add(new(){WriteOffId=w.Id,ProductStockId=multi?null:stock.Id,ProductId=item.Id,Quantity=qty,UnitPrice=20,LossAmount=qty*20,
            UnitPricePurchase=12.5m,LossAmountPurchase=decimal.Round(qty*12.5m,2,MidpointRounding.AwayFromZero)});
        await using(var db=f.Context()){db.Add(w);await db.SaveChangesAsync();}
        return new(new(tenant.Id,loc.Id,user.Id,item.Id,stock.Id),w.Id,second?.Id);
    }
    internal static WriteOffPostingStore Store(Scenario s, bool enabled=true, params IInterceptor[] interceptors)
    {
        var options=new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(TestPostgres.DataSource(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!))
            .ConfigureWarnings(w=>w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        if(interceptors.Length>0)options.AddInterceptors(interceptors);
        return new(options.Options,s.Gate,TimeProvider.System,new(enabled));
    }
    internal static async Task<WriteOffPostingCommand> Review(Scenario s,string phase="approve",string? reason=null)=>new((await Store(s).PreviewAsync(s.Scope,s.WriteOffId,phase,default)).Fingerprint,reason);
    [ErpPostgresFact]
    public async Task Approval_commits_once_and_reversal_restores_batches_with_full_compensation()
    {
        var s=await Seed(fixture);var store=Store(s);var body=await Review(s);var first=await store.PostAsync(s.Scope,s.WriteOffId,"approve",body,default);
        Assert.Equal(first with{Replayed=true},await store.PostAsync(s.Scope,s.WriteOffId,"approve",body,default));
        await using(var db=fixture.Context())
        {
            Assert.Equal(1m,(await db.ProductStocks.SingleAsync(x=>x.Id==s.Inventory.Batch)).Quantity);
            var entry=await db.InventoryLedgerEntries.SingleAsync(x=>x.PostingId==first.PostingId);Assert.Equal(-2.25m,entry.QuantityDelta);Assert.Null(entry.UnitCost);Assert.Null(entry.ValueDelta);
            Assert.Equal(45m,(await db.StockMovements.SingleAsync(m=>m.ReferenceId==s.WriteOffId)).TotalAmount);
        }
        var reverseBody=await Review(s,"reverse","Correct erroneous disposal after physical verification");
        var reverse=await store.PostAsync(s.Scope,s.WriteOffId,"reverse",reverseBody,default);
        Assert.Equal(reverse with{Replayed=true},await store.PostAsync(s.Scope,s.WriteOffId,"reverse",reverseBody,default));
        Assert.Equal(first.PostingId,(await store.PostAsync(s.Scope,s.WriteOffId,"approve",body,default)).PostingId);
        await using var check=fixture.Context();Assert.Equal(3.25m,(await check.ProductStocks.SingleAsync(x=>x.Id==s.Inventory.Batch)).Quantity);
        Assert.Equal("reversed",(await check.WriteOffs.SingleAsync(w=>w.Id==s.WriteOffId)).Status);
        var original=await check.InventoryLedgerEntries.SingleAsync(e=>e.PostingId==first.PostingId);var compensation=await check.InventoryLedgerEntries.SingleAsync(e=>e.PostingId==reverse.PostingId);
        Assert.Equal(original.Id,compensation.ReversesEntryId);Assert.Equal(-original.QuantityDelta,compensation.QuantityDelta);
        Assert.Equal("reversed",(await check.BusinessDocuments.SingleAsync(d=>d.Id==first.DocumentId)).Status);
    }
    [ErpPostgresFact]
    public async Task Multi_batch_FEFO_plan_is_reviewed_and_consumes_oldest_lot_first()
    {
        var s=await Seed(fixture,true);var preview=await Store(s).PreviewAsync(s.Scope,s.WriteOffId,"approve",default);
        Assert.Equal(3.25m,preview.Batches.Single(b=>b.BatchId==s.Inventory.Batch).Quantity);Assert.Equal(1.25m,preview.Batches.Single(b=>b.BatchId==s.SecondBatch).Quantity);
        Assert.Equal(90m,preview.Batches.SelectMany(b=>b.Allocations).Sum(a=>a.RetailLoss));
        var result=await Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",new(preview.Fingerprint),default);
        await using var db=fixture.Context();Assert.Equal(0,(await db.ProductStocks.SingleAsync(x=>x.Id==s.Inventory.Batch)).Quantity);Assert.Equal(2.75m,(await db.ProductStocks.SingleAsync(x=>x.Id==s.SecondBatch)).Quantity);
        Assert.Equal(2,await db.InventoryLedgerEntries.CountAsync(e=>e.PostingId==result.PostingId));
    }
    [ErpPostgresFact]
    public async Task Rejection_has_an_immutable_decision_but_no_inventory_posting_or_zero_delta()
    {
        var s=await Seed(fixture);var body=await Review(s,"reject","Source document requires correction before disposal");var first=await Store(s).PostAsync(s.Scope,s.WriteOffId,"reject",body,default);
        Assert.Null(first.PostingId);Assert.Null(first.EventId);Assert.Equal(first with{Replayed=true},await Store(s).PostAsync(s.Scope,s.WriteOffId,"reject",body,default));
        await using var db=fixture.Context();Assert.Equal("rejected",(await db.WriteOffs.SingleAsync(x=>x.Id==s.WriteOffId)).Status);
        Assert.Equal(3.25m,(await db.ProductStocks.SingleAsync(x=>x.Id==s.Inventory.Batch)).Quantity);
        Assert.False(await db.DocumentPostings.AnyAsync(p=>p.DocumentId==first.DocumentId));Assert.Equal("rejected",(await db.DocumentApprovals.SingleAsync(a=>a.DocumentId==first.DocumentId)).Decision);
    }
    [ErpPostgresFact]
    public async Task Approve_reject_race_has_only_one_terminal_winner()
    {
        var s=await Seed(fixture);var approve=await Review(s);var reject=await Review(s,"reject","Document requires additional physical verification");
        async Task<bool> Attempt(string phase,WriteOffPostingCommand body){try{await Store(s).PostAsync(s.Scope,s.WriteOffId,phase,body,default);return true;}catch(InventoryPostingConflictException){return false;}}
        var results=await Task.WhenAll(Attempt("approve",approve),Attempt("reject",reject));Assert.Single(results,x=>x);
        await using var db=fixture.Context();var w=await db.WriteOffs.SingleAsync(x=>x.Id==s.WriteOffId);Assert.Equal(w.Status=="approved"?1m:3.25m,(await db.ProductStocks.SingleAsync(x=>x.Id==s.Inventory.Batch)).Quantity);
    }
    [ErpPostgresFact]
    public async Task Concurrent_exact_approvals_deduct_only_once()
    {
        var s=await Seed(fixture);var body=await Review(s);var results=await Task.WhenAll(Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",body,default),Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",body,default));
        Assert.Equal(results[0].PostingId,results[1].PostingId);Assert.Single(results,x=>x.Replayed);
    }
    [ErpPostgresTheory] [InlineData(false,true)] [InlineData(true,false)]
    public async Task Both_application_flag_and_database_fence_are_required(bool enabled,bool fenced)
    {
        var s=await Seed(fixture,activate:fenced);var body=await Review(s);var store=Store(s,enabled);
        if(!enabled)await Assert.ThrowsAsync<InventoryPostingDisabledException>(()=>store.PostAsync(s.Scope,s.WriteOffId,"approve",body,default));
        else await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.PostAsync(s.Scope,s.WriteOffId,"approve",body,default));await Pending(s);
    }
    [ErpPostgresTheory]
    [InlineData("movement")] [InlineData("projection")] [InlineData("marker")] [InlineData("metadata")] [InlineData("source")]
    public async Task Incomplete_approval_rolls_back_stock_source_and_ERP_graph(string fault)
    {
        var s=await Seed(fixture);var body=await Review(s);var error=await Assert.ThrowsAnyAsync<Exception>(()=>Store(s,true,new Sabotage(s.WriteOffId,"approve",fault)).PostAsync(s.Scope,s.WriteOffId,"approve",body,default));
        var pg=error as PostgresException??error.InnerException as PostgresException;Assert.NotNull(pg);Assert.Equal("P4F01",pg.SqlState);await Pending(s);
    }
    [ErpPostgresTheory] [InlineData("movement")] [InlineData("projection")] [InlineData("metadata")]
    public async Task Failed_reversal_retains_the_original_approved_writeoff(string fault)
    {
        var s=await Seed(fixture);var posted=await Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",await Review(s),default);
        var body=await Review(s,"reverse","Correct erroneous disposal after verification");
        var error=await Assert.ThrowsAnyAsync<Exception>(()=>Store(s,true,new Sabotage(s.WriteOffId,"reverse",fault)).PostAsync(s.Scope,s.WriteOffId,"reverse",body,default));
        var pg=error as PostgresException??error.InnerException as PostgresException;Assert.NotNull(pg);Assert.Equal("P4F01",pg.SqlState);
        await using var db=fixture.Context();Assert.Equal("approved",(await db.WriteOffs.SingleAsync(x=>x.Id==s.WriteOffId)).Status);Assert.Equal("posted",(await db.BusinessDocuments.SingleAsync(d=>d.Id==posted.DocumentId)).Status);
        Assert.Equal(1m,(await db.ProductStocks.SingleAsync(x=>x.Id==s.Inventory.Batch)).Quantity);
    }
    [ErpPostgresFact]
    public async Task Source_change_after_preview_needs_fresh_review()
    {
        var s=await Seed(fixture);var body=await Review(s);await using(var db=fixture.Context()){var w=await db.WriteOffs.SingleAsync(w=>w.Id==s.WriteOffId);w.Notes="Changed source explanation";await db.SaveChangesAsync();}
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",body,default));await Pending(s);
    }
    [ErpPostgresFact]
    public async Task An_explicit_non_FEFO_batch_is_not_silently_substituted()
    {
        var s=await Seed(fixture,true);await using(var db=fixture.Context())await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE write_off_items SET \"ProductStockId\"={s.SecondBatch},\"Quantity\"=2 WHERE \"WriteOffId\"={s.WriteOffId}");
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).PreviewAsync(s.Scope,s.WriteOffId,"approve",default));await Pending(s);
    }
    [ErpPostgresFact]
    public async Task A_pre_posting_repeatable_read_snapshot_cannot_rewrite_source_items()
    {
        var s=await Seed(fixture);var body=await Review(s);await using var old=fixture.Context();await using var tx=await old.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await old.WriteOffItems.Where(i=>i.WriteOffId==s.WriteOffId).ToListAsync();await Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",body,default);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>old.Database.ExecuteSqlInterpolatedAsync($"UPDATE write_off_items SET \"LossAmount\"=1 WHERE \"WriteOffId\"={s.WriteOffId}"));Assert.Equal("40001",error.SqlState);await tx.RollbackAsync();
    }
    [ErpPostgresTheory] [InlineData(false)] [InlineData(true)]
    public async Task Live_permission_is_rechecked_and_temporary_grant_can_override_denial(bool grant)
    {
        var s=await Seed(fixture);var body=await Review(s);await using(var db=fixture.Context()){var u=await db.Users.SingleAsync(u=>u.Id==s.Inventory.Actor);u.SetPermissions(new(){{"write-offs",false}});
            if(grant)db.Add(UserPermissionGrant.Create(s.Inventory.Tenant,u.Id,"write-offs",DateTime.UtcNow.AddHours(1),u.Id));await db.SaveChangesAsync();}
        if(grant)await Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",body,default);
        else {await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",body,default));await Pending(s);}
    }
    [ErpPostgresFact]
    public async Task Supplier_return_cannot_be_misclassified_as_a_writeoff()
    {
        var s=await Seed(fixture);await using(var db=fixture.Context())await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE write_off_items SET \"IsReturnedToSupplier\"=true WHERE \"WriteOffId\"={s.WriteOffId}");
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).PreviewAsync(s.Scope,s.WriteOffId,"approve",default));await Pending(s);
    }
    [ErpPostgresFact]
    public async Task Foreign_writeoff_is_not_visible()
    {var s=await Seed(fixture);var foreign=await Seed(fixture);await Assert.ThrowsAsync<InventoryPostingNotFoundException>(()=>Store(s).PreviewAsync(s.Scope,foreign.WriteOffId,"approve",default));}
    [ErpPostgresTheory] [InlineData(false)] [InlineData(true)]
    public async Task Active_reservations_block_approval_and_reversal(bool reverse)
    {
        var s=await Seed(fixture);if(reverse)await Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",await Review(s),default);
        var phase=reverse?"reverse":"approve";var body=await Review(s,phase,reverse?"Restore verified incorrectly disposed inventory":null);
        await using(var db=fixture.Context())
        {
            var d=new BusinessDocument{TenantId=s.Inventory.Tenant,DocumentType="reservation_source"};db.Add(d);await db.SaveChangesAsync();
            var line=new DocumentLine{TenantId=s.Inventory.Tenant,DocumentId=d.Id,LineNumber=1,ItemId=s.Inventory.Item,Quantity=1,FromLocationId=s.Inventory.Location,ItemNameSnapshot="Reserved"};db.Add(line);await db.SaveChangesAsync();
            db.Add(new InventoryReservation{TenantId=s.Inventory.Tenant,DocumentId=d.Id,DocumentLineId=line.Id,ItemId=s.Inventory.Item,LocationId=s.Inventory.Location,Quantity=1,RemainingQuantity=1,IdempotencyKey=Guid.NewGuid().ToString("N")});await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).PostAsync(s.Scope,s.WriteOffId,phase,body,default));
        if(!reverse)await Pending(s);
    }
    [ErpPostgresTheory] [InlineData("header")] [InlineData("line")] [InlineData("truncate")]
    public async Task Posted_source_and_history_are_immutable_to_legacy_writes(string target)
    {
        var s=await Seed(fixture);await Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",await Review(s),default);await using var db=fixture.Context();
        var error=await Assert.ThrowsAsync<PostgresException>(async()=>{
            if(target=="header")await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE write_offs SET \"Status\"='rejected' WHERE \"Id\"={s.WriteOffId}");
            if(target=="line")await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM write_off_items WHERE \"WriteOffId\"={s.WriteOffId}");
            if(target=="truncate")await db.Database.ExecuteSqlRawAsync("TRUNCATE write_off_items");
        });Assert.Equal("P4F01",error.SqlState);
    }
    [ErpPostgresFact]
    public async Task Known_cost_is_carried_from_receipt_ledger_and_not_current_catalog()
    {
        var s=await Seed(fixture);Guid receiptId;string fingerprint;
        await using(var db=fixture.Context())
        {
            (await db.Tenants.SingleAsync(t=>t.Id==s.Inventory.Tenant)).UpdateModules(["inventory","procurement"]);
            var r=new StockReceipt{TenantId=s.Inventory.Tenant,DestinationStoreId=s.Inventory.Location,Status="ordered",CreatedBy=s.Inventory.Actor};
            r.Items.Add(new(){ReceiptId=r.Id,ProductId=s.Inventory.Item,QuantityOrdered=2.25m,PricePurchase=12.50m,ExpiryDate=new(2027,1,1),BatchNumber="priced-oldest"});
            db.Add(r);await db.SaveChangesAsync();receiptId=r.Id;var loaded=await db.StockReceipts.AsNoTracking().Include(x=>x.Items).ThenInclude(x=>x.Product).SingleAsync(r=>r.Id==receiptId);fingerprint=ReceiptPostingRules.Fingerprint(loaded);
        }
        await ReceiptPostingPostgresTests.Store(fixture,new(s.Inventory,receiptId,fingerprint)).ReceiveAsync(s.Scope,receiptId,fingerprint,default);
        await using(var db=fixture.Context())
        {
            (await db.Items.SingleAsync(i=>i.Id==s.Inventory.Item)).PricePurchase=999;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE write_off_items SET \"ProductStockId\"=NULL WHERE \"WriteOffId\"={s.WriteOffId}");await db.SaveChangesAsync();
        }
        var result=await Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",await Review(s),default);await using var check=fixture.Context();
        var entry=await check.InventoryLedgerEntries.SingleAsync(e=>e.PostingId==result.PostingId);Assert.Equal(12.50m,entry.UnitCost);Assert.Equal(-28.1250m,entry.ValueDelta);Assert.Equal("priced-oldest",entry.BatchNumberSnapshot);
    }
    [ErpPostgresFact]
    public async Task Multiple_source_lines_on_one_batch_produce_one_physical_delta()
    {
        var s=await Seed(fixture);await using(var db=fixture.Context())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE write_off_items SET \"Quantity\"=1,\"LossAmount\"=20,\"LossAmountPurchase\"=12.50 WHERE \"WriteOffId\"={s.WriteOffId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE write_offs SET \"TotalLossAmount\"=30,\"TotalLossAmountPurchase\"=18.75 WHERE \"Id\"={s.WriteOffId}");
            db.Add(new WriteOffItem{WriteOffId=s.WriteOffId,ProductId=s.Inventory.Item,ProductStockId=s.Inventory.Batch,Quantity=.5m,UnitPrice=20,LossAmount=10,UnitPricePurchase=12.5m,LossAmountPurchase=6.25m});await db.SaveChangesAsync();
        }
        var result=await Store(s).PostAsync(s.Scope,s.WriteOffId,"approve",await Review(s),default);await using var check=fixture.Context();
        Assert.Equal(-1.5m,(await check.InventoryLedgerEntries.SingleAsync(e=>e.PostingId==result.PostingId)).QuantityDelta);Assert.Equal(1.75m,(await check.ProductStocks.SingleAsync(x=>x.Id==s.Inventory.Batch)).Quantity);
    }
    [ErpPostgresFact]
    public async Task Rejection_without_the_review_marker_rolls_back_the_decision_and_source()
    {
        var s=await Seed(fixture);var body=await Review(s,"reject","Additional physical stock inspection required");
        var error=await Assert.ThrowsAnyAsync<Exception>(()=>Store(s,true,new Sabotage(s.WriteOffId,"reject","review-marker")).PostAsync(s.Scope,s.WriteOffId,"reject",body,default));
        var pg=error as PostgresException??error.InnerException as PostgresException;Assert.NotNull(pg);Assert.Equal("P4F01",pg.SqlState);await Pending(s);
    }
    private async Task Pending(Scenario s){await using var db=fixture.Context();Assert.Equal("pending_approval",(await db.WriteOffs.SingleAsync(w=>w.Id==s.WriteOffId)).Status);Assert.Equal(3.25m,(await db.ProductStocks.SingleAsync(x=>x.Id==s.Inventory.Batch)).Quantity);Assert.False(await db.BusinessDocuments.AnyAsync(d=>d.TenantId==s.Inventory.Tenant&&d.SourceId==s.WriteOffId.ToString()&&d.SourceSystem.StartsWith("write_off.")));}
    private sealed class Sabotage(Guid id,string phase,string fault):SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            var db=(AppDbContext)data.Context!;if(!db.ChangeTracker.Entries<BusinessDocument>().Any(e=>e.Entity.SourceSystem==WriteOffPostingRules.Source(phase)&&e.Entity.Status==(phase=="reject"?"cancelled":"posted")))return result;
            if(fault=="review-marker")await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.erp_review_document_id','',true)",ct);
            if(fault=="movement")await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM stock_movements WHERE \"ReferenceId\"={id}",ct);
            if(fault=="projection")await db.Database.ExecuteSqlRawAsync("DELETE FROM inventory_balance_projections WHERE \"LastLedgerEntryId\" IN (SELECT \"Id\" FROM inventory_ledger_entries WHERE \"PostingId\"=current_setting('app.erp_posting_id')::uuid)",ct);
            if(fault=="marker")await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.erp_posting_id','',true)",ct);
            if(fault=="metadata")await db.Database.ExecuteSqlRawAsync("UPDATE product_stock SET \"ShelfNumber\"=42 WHERE \"Id\" IN (SELECT \"BatchId\" FROM inventory_ledger_entries WHERE \"PostingId\"=current_setting('app.erp_posting_id')::uuid)",ct);
            if(fault=="source")await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE write_offs SET \"Notes\"='Unreviewed header change' WHERE \"Id\"={id}",ct);
            return result;
        }
    }
}
