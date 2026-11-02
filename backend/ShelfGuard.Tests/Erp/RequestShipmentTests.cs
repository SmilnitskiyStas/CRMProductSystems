using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
public sealed class RequestShipmentTests(ErpSchemaFixture fixture)
{
    private sealed class Access(WorkAccessScope scope):IWorkAccessContext{public Task<WorkAccessScope?> GetAsync(CancellationToken ct=default)=>Task.FromResult<WorkAccessScope?>(scope);}
    private DbContextOptions<AppDbContext> Options(params IInterceptor[] interceptors)=>new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).AddInterceptors(interceptors).IgnoreManyServiceProvidersWarning().Options;
    private TransferPostingStore Transfers(TransferPostingPostgresTests.Scenario s,bool enabled=true)=>new(Options(),s.Gate(),TimeProvider.System,new(enabled));
    private RequestShipmentStore Store(TransferPostingPostgresTests.Scenario s,WorkAccessScope? scope=null,bool enabled=true,params IInterceptor[] interceptors)=>new(Options(interceptors),new Access(scope??s.Scope),Transfers(s,enabled),TimeProvider.System);
    private async Task<(TransferPostingPostgresTests.Scenario S,Guid Id,Guid Order)> Seed(bool complete=true,bool link=true)
    {
        var production=await ProductionPostingPreviewPostgresTests.Seed(fixture);var tenant=production.S.Tenant;var actor=production.S.Actor;var from=production.S.Location;
        var destination=new Location{TenantId=tenant,Name="Receiving store"};var stock=new ProductStock{TenantId=tenant,StoreId=destination.Id,ProductId=production.OutputId,Quantity=1,QuantityInitial=1,ExpiryDate=new(2028,1,1)};
        var r=new ProductionRequest{TenantId=tenant,StoreLocationId=destination.Id,ProductionLocationId=from,Status="accepted",NeededBy=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),CreatedBy=actor};var line=new ProductionRequestLine{TenantId=tenant,RequestId=r.Id,ItemId=production.OutputId,RequestedQty=2,AcceptedQty=2};
        await using(var db=fixture.Context()){(await db.Locations.SingleAsync(l=>l.Id==from)).LocationType="production";db.AddRange(destination,stock,UserLocation.Create(tenant,actor,destination.Id,null),r,line);if(link)db.Add(new ProductionOrderRequestLine{TenantId=tenant,ProductionOrderId=production.OrderId,RequestLineId=line.Id,Qty=2});await db.SaveChangesAsync();}
        await using(var db=fixture.Context()){var snapshot=await new InventorySnapshotReader(db).ReadAsync(tenant,destination.Id,operatorId:actor);var report=new ShelfGuard.Application.Features.Erp.Backfill.InventoryDryRunAnalyzer().Analyze(snapshot);await new ControlBalanceMaterializer(db).MaterializeAsync(new(tenant,destination.Id,Guid.NewGuid(),report.SourceFingerprint,[stock.Id],"Reviewed receiving baseline","shipment-disposable",OperatorId:actor));await InventoryFenceTestHelper.Activate(db,tenant,destination.Id,actor);}
        var batch=Guid.NewGuid();if(complete){var reader=new ProductionPostingPreviewReader(Options(),production.Stock.Gate,new(true),TimeProvider.System);var plan=await reader.ReadAsync(production.Scope,production.OrderId,default);Assert.True(plan.CanPost);batch=(await new ProductionPostingStore(Options(),production.Stock.Gate,new(true),TimeProvider.System,reader).CompleteAsync(production.Scope,production.OrderId,new(Guid.NewGuid(),plan.Fingerprint,"Reviewed real finished shipment output"),default)).OutputBatchId;}
        var source=new InventoryFenceScenario(tenant,from,actor,production.OutputId,batch);return(new(source,destination.Id,new(Guid.NewGuid(),from,destination.Id,"production_to_store","Reviewed shipment",[new(batch,2)])),r.Id,production.OrderId);
    }
    private async Task<RequestShipmentCommand> Reviewed(TransferPostingPostgresTests.Scenario s,Guid id,Guid? operation=null)
    {var p=await Store(s).PreviewAsync(id,operation??Guid.NewGuid(),s.Source.Location,default);Assert.True(p.CanShip,string.Join(',',p.Issues));return new(p.OperationId,p.Fingerprint,"Reviewed finished request shipment");}
    private async Task<RequestShipmentResult> Ship(TransferPostingPostgresTests.Scenario s,Guid id)=>await Store(s).ShipAsync(id,s.Source.Location,await Reviewed(s,id),default);
    [ErpPostgresFact]public async Task Readonly_preview_binds_request_and_exact_FEFO_without_inventory_changes()
    {
        var s=await Seed();var op=Guid.NewGuid();var a=await Store(s.S).PreviewAsync(s.Id,op,s.S.Source.Location,default);var b=await Store(s.S).PreviewAsync(s.Id,op,s.S.Source.Location,default);Assert.Equal(a.Fingerprint,b.Fingerprint);var line=Assert.Single(a.Transfer!.Lines);Assert.Equal(s.S.Source.Batch,line.BatchId);Assert.Equal(2.00m,line.Quantity);Assert.Null(line.ExactUnitCost);await using var db=fixture.Context();Assert.Equal(2.00m,(await db.ProductStocks.SingleAsync(x=>x.Id==line.BatchId)).Quantity);Assert.False(await db.StockTransfers.AnyAsync(x=>x.Id==op));Assert.Equal("accepted",(await db.ProductionRequests.SingleAsync(x=>x.Id==s.Id)).Status);
    }
    [ErpPostgresFact]public async Task Ship_receive_conserves_quantity_unknown_cost_and_links_source_in_same_commit()
    {
        var s=await Seed();var posted=await Ship(s.S,s.Id);await using(var db=fixture.Context()){var r=await db.ProductionRequests.SingleAsync(r=>r.Id==s.Id);Assert.Equal("fulfilled",r.Status);Assert.Equal(posted.TransferId,r.ShipmentTransferId);Assert.Equal(0,(await db.ProductStocks.SingleAsync(b=>b.Id==s.S.Source.Batch)).Quantity);var audit=await db.BusinessDocuments.SingleAsync(d=>d.TenantId==s.S.Source.Tenant&&d.DocumentKey==posted.TransferId);Assert.Equal("requests.shipment.v1",audit.SourceSystem);Assert.True(await db.DocumentLinks.AnyAsync(l=>l.FromDocumentId==audit.Id&&l.ToDocumentId==posted.DocumentId));Assert.False(await db.DocumentPostings.AnyAsync(p=>p.DocumentId==audit.Id));Assert.Contains("command",(await db.DocumentLines.SingleAsync(l=>l.DocumentId==audit.Id)).Metadata!);}
        var transfer=Transfers(s.S);var p=await transfer.PreviewAsync(s.S.Scope,posted.TransferId,"receive",default);var received=await transfer.CompleteAsync(s.S.Scope,posted.TransferId,"receive",new(p.Fingerprint),default);Assert.False(received.Replayed);await using(var db=fixture.Context()){Assert.Equal(3.00m,await db.ProductStocks.Where(b=>b.TenantId==s.S.Source.Tenant&&b.ProductId==s.S.Source.Item).SumAsync(b=>b.Quantity));Assert.All(await db.InventoryLedgerEntries.Where(l=>l.PostingId==posted.PostingId||l.PostingId==received.PostingId).ToListAsync(),l=>Assert.Null(l.UnitCost));Assert.Equal("received",(await db.StockTransfers.SingleAsync(t=>t.Id==posted.TransferId)).Status);Assert.Equal(posted.TransferId,(await db.ProductionRequests.SingleAsync(r=>r.Id==s.Id)).ShipmentTransferId);}
    }
    [ErpPostgresFact]public async Task Exact_replay_after_receive_needs_no_live_FEFO_and_different_reason_conflicts()
    {
        var s=await Seed();var c=await Reviewed(s.S,s.Id);var a=await Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c,default);var p=await Transfers(s.S).PreviewAsync(s.S.Scope,a.TransferId,"receive",default);await Transfers(s.S).CompleteAsync(s.S.Scope,a.TransferId,"receive",new(p.Fingerprint),default);Assert.Equal(a with{Replayed=true},await Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c,default));await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c with{Reason="Different reviewed shipment reason"},default));
    }
    [ErpPostgresFact]public async Task Cancel_restores_request_and_reship_is_safe_while_old_replay_preserves_latest_link()
    {
        var s=await Seed();var c=await Reviewed(s.S,s.Id);var a=await Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c,default);var p=await Transfers(s.S).PreviewAsync(s.S.Scope,a.TransferId,"cancel",default);await Transfers(s.S).CompleteAsync(s.S.Scope,a.TransferId,"cancel",new(p.Fingerprint,"Shipment cancelled after review"),default);await using(var db=fixture.Context()){var r=await db.ProductionRequests.SingleAsync(x=>x.Id==s.Id);Assert.Equal("accepted",r.Status);Assert.Null(r.ShipmentTransferId);Assert.Null(r.ShippedAt);Assert.Equal(2.00m,(await db.ProductStocks.SingleAsync(x=>x.Id==s.S.Source.Batch)).Quantity);}
        var next=await Ship(s.S,s.Id);Assert.NotEqual(a.TransferId,next.TransferId);Assert.True((await Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c,default)).Replayed);await using(var db=fixture.Context()){Assert.Equal(next.TransferId,(await db.ProductionRequests.SingleAsync(r=>r.Id==s.Id)).ShipmentTransferId);}
    }
    [ErpPostgresFact]public async Task Concurrent_exact_retries_create_one_transfer_ledger_and_audit()
    {
        var s=await Seed();var c=await Reviewed(s.S,s.Id);var result=await Task.WhenAll(Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c,default),Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c,default));Assert.Single(result,r=>r.Replayed);Assert.Equal(result[0].PostingId,result[1].PostingId);await using var db=fixture.Context();Assert.Single(await db.StockTransfers.Where(t=>t.Id==c.OperationId).ToListAsync());
    }
    [ErpPostgresFact]public async Task Concurrent_distinct_confirmations_cannot_double_ship_request()
    {
        var s=await Seed();var a=await Reviewed(s.S,s.Id);var b=await Reviewed(s.S,s.Id);var errors=await Task.WhenAll(Record.ExceptionAsync(()=>Store(s.S).ShipAsync(s.Id,s.S.Source.Location,a,default)),Record.ExceptionAsync(()=>Store(s.S).ShipAsync(s.Id,s.S.Source.Location,b,default)));Assert.Single(errors,e=>e==null);Assert.IsType<InventoryPostingConflictException>(Assert.Single(errors,e=>e!=null));
    }
    [ErpPostgresFact]public async Task Stale_acceptance_or_held_finished_stock_cannot_ship()
    {
        var s=await Seed();var c=await Reviewed(s.S,s.Id);await using(var db=fixture.Context()){(await db.ProductStocks.SingleAsync(b=>b.Id==s.S.Source.Batch)).Status="temp_violation";await db.SaveChangesAsync();}var p=await Store(s.S).PreviewAsync(s.Id,c.OperationId,s.S.Source.Location,default);Assert.False(p.CanShip);Assert.Contains("insufficient_usable_finished_stock",p.Issues);await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c,default));
    }
    [ErpPostgresTheory][InlineData("not_done")][InlineData("missing")][InlineData("wrong_output")]
    public async Task Production_allocation_must_cover_exact_request_with_finished_matching_orders(string kind)
    {
        var s=await Seed(kind!="not_done",kind!="missing");if(kind=="wrong_output"){await using var db=fixture.Context();var other=new Item{TenantId=s.S.Source.Tenant,Name="Wrong requested output",Unit="kg"};db.Add(other);db.Entry(await db.ProductionRequestLines.SingleAsync(l=>l.RequestId==s.Id)).Property(l=>l.ItemId).CurrentValue=other.Id;await db.SaveChangesAsync();}var p=await Store(s.S).PreviewAsync(s.Id,Guid.NewGuid(),s.S.Source.Location,default);Assert.False(p.CanShip);Assert.Contains("production_not_ready",p.Issues);
    }
    [ErpPostgresFact]public async Task Scope_permission_and_live_assignment_are_required_even_for_replay()
    {
        var s=await Seed();var c=await Reviewed(s.S,s.Id);await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Store(s.S,s.S.Scope with{LocationIds=[]}).ShipAsync(s.Id,s.S.Source.Location,c,default));await using(var db=fixture.Context()){(await db.Users.SingleAsync(u=>u.Id==s.S.Source.Actor)).SetPermissions(new(){{"production.manage",false}});await db.SaveChangesAsync();}await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c,default));
    }
    [ErpPostgresFact]public async Task Disabled_transfer_scopes_do_not_create_audit_or_transfer()
    {
        var s=await Seed();var c=await Reviewed(s.S,s.Id);var p=await Store(s.S,enabled:false).PreviewAsync(s.Id,c.OperationId,s.S.Source.Location,default);Assert.False(p.CanShip);Assert.Contains("transfer_scope_disabled",p.Issues);await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s.S,enabled:false).ShipAsync(s.Id,s.S.Source.Location,c,default));
    }
    private sealed class RejectAudit:SaveChangesInterceptor
    {public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default){if(e.Context!.ChangeTracker.Entries<BusinessDocument>().Any(x=>x.State==EntityState.Added&&x.Entity.SourceSystem=="requests.shipment.v1"))throw new InvalidOperationException("Synthetic request audit persistence failure");return ValueTask.FromResult(result);}}
    [ErpPostgresFact]public async Task Audit_failure_rolls_back_already_executed_transfer_stock_ledger_and_outbox()
    {
        var s=await Seed();var c=await Reviewed(s.S,s.Id);await Assert.ThrowsAsync<InvalidOperationException>(()=>Store(s.S,null,true,new RejectAudit()).ShipAsync(s.Id,s.S.Source.Location,c,default));await using var db=fixture.Context();Assert.False(await db.StockTransfers.AnyAsync(t=>t.Id==c.OperationId));Assert.Equal(2.00m,(await db.ProductStocks.SingleAsync(b=>b.Id==s.S.Source.Batch)).Quantity);Assert.Equal("accepted",(await db.ProductionRequests.SingleAsync(r=>r.Id==s.Id)).Status);Assert.False(await db.DocumentPostings.AnyAsync(p=>p.IdempotencyKey==TransferPostingRules.Key(c.OperationId,"dispatch")));
    }
    [ErpPostgresFact]public async Task Cancel_permission_revocation_rolls_back_stock_return_and_source_transition()
    {
        var s=await Seed();var a=await Ship(s.S,s.Id);var p=await Transfers(s.S).PreviewAsync(s.S.Scope,a.TransferId,"cancel",default);await using(var db=fixture.Context()){(await db.Users.SingleAsync(u=>u.Id==s.S.Source.Actor)).SetPermissions(new(){{"production.cancel",false}});await db.SaveChangesAsync();}await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Transfers(s.S).CompleteAsync(s.S.Scope,a.TransferId,"cancel",new(p.Fingerprint,"Reviewed source shipment cancellation"),default));await using(var db=fixture.Context()){Assert.Equal("fulfilled",(await db.ProductionRequests.SingleAsync(r=>r.Id==s.Id)).Status);Assert.Equal("in_transit",(await db.StockTransfers.SingleAsync(t=>t.Id==a.TransferId)).Status);Assert.Equal(0,(await db.ProductStocks.SingleAsync(b=>b.Id==s.S.Source.Batch)).Quantity);}
    }
    [ErpPostgresFact]public async Task Dispatch_replay_racing_cancellation_uses_consistent_lock_order()
    {
        var s=await Seed();var c=await Reviewed(s.S,s.Id);var a=await Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c,default);var p=await Transfers(s.S).PreviewAsync(s.S.Scope,a.TransferId,"cancel",default);await Task.WhenAll(Store(s.S).ShipAsync(s.Id,s.S.Source.Location,c,default),Transfers(s.S).CompleteAsync(s.S.Scope,a.TransferId,"cancel",new(p.Fingerprint,"Reviewed racing shipment cancellation"),default)).WaitAsync(TimeSpan.FromSeconds(20));await using var db=fixture.Context();Assert.Equal("accepted",(await db.ProductionRequests.SingleAsync(r=>r.Id==s.Id)).Status);Assert.Equal(2.00m,(await db.ProductStocks.SingleAsync(b=>b.Id==s.S.Source.Batch)).Quantity);
    }
}
