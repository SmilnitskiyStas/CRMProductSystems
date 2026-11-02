using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
using Scenario=ShelfGuard.Tests.Erp.ProductionPostingPreviewPostgresTests.Scenario;
namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class ProductionPostingPostgresTests(ErpSchemaFixture fixture)
{
    private DbContextOptions<AppDbContext> Options(params IInterceptor[] interceptors)=>new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!))
        .AddInterceptors(interceptors).IgnoreManyServiceProvidersWarning().Options;
    private ProductionPostingPreviewReader Reader(Scenario s,bool enabled=true)=>new(Options(new Role(fixture.ReaderRole)),s.Stock.Gate,new(enabled),TimeProvider.System);
    private ProductionPostingStore Writer(Scenario s,bool enabled=true,params IInterceptor[] interceptors)=>new(Options([new Role(fixture.ReaderRole),..interceptors]),s.Stock.Gate,new(enabled),TimeProvider.System,Reader(s,enabled));
    private async Task<ProductionPostingCommand> Review(Scenario s)=>new(Guid.NewGuid(),(await Reader(s).ReadAsync(s.Scope,s.OrderId,default)).Fingerprint,"Reviewed production issue and finished output");
    private Task<Scenario> Seed(bool fenced=true)=>ProductionPostingPreviewPostgresTests.Seed(fixture,fenced);
    private sealed class Role(string role):DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection connection,ConnectionEndEventData e,CancellationToken ct=default)
        {await using var command=connection.CreateCommand();command.CommandText="SET ROLE "+role;await command.ExecuteNonQueryAsync(ct);}
    }
    [ErpPostgresFact]
    public async Task Completion_commits_FEFO_issue_output_ledger_projection_and_live_event_once_under_RLS()
    {
        var s=await Seed();var c=await Review(s);var a=await Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default);
        Assert.Equal(a with{Replayed=true},await Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default));
        await using var db=fixture.Context();var doc=await db.BusinessDocuments.SingleAsync(d=>d.Id==a.DocumentId);Assert.Equal("posted",doc.Status);
        Assert.Equal(ProductionOrderStatus.Done,(await db.ProductionOrders.SingleAsync(o=>o.Id==s.OrderId)).Status);
        Assert.Equal(0,(await db.ProductStocks.SingleAsync(b=>b.Id==s.S.Batch)).Quantity);Assert.Equal(2.75m,(await db.ProductStocks.SingleAsync(b=>b.Id==s.Stock.SecondBatch)).Quantity);
        var output=await db.ProductStocks.SingleAsync(b=>b.Id==a.OutputBatchId);Assert.Equal(2,output.Quantity);Assert.Equal(s.OutputId,output.ProductId);Assert.Equal(s.OrderId,output.SourceId);
        var entries=await db.InventoryLedgerEntries.Where(e=>e.PostingId==a.PostingId).OrderBy(e=>e.EntryNumber).ToListAsync();Assert.Equal(new[]{-3.25m,-1.25m,2m},entries.Select(e=>e.QuantityDelta));
        Assert.All(entries,e=>{Assert.Null(e.UnitCost);Assert.Null(e.ValueDelta);});
        foreach(var e in entries){var b=await db.InventoryBalanceProjections.SingleAsync(b=>b.TenantId==s.S.Tenant&&b.BatchId==e.BatchId);Assert.Equal(e.QuantityAfter,b.Quantity);Assert.Equal(e.Id,b.LastLedgerEntryId);}
        Assert.Equal(2,await db.ProductionOrderConsumptions.CountAsync(x=>x.ProductionOrderId==s.OrderId));Assert.Equal(3,await db.StockEvents.CountAsync(x=>x.TenantId==s.S.Tenant));
        Assert.Equal(3,await db.StockMovements.CountAsync(x=>x.TenantId==s.S.Tenant));Assert.Equal("live",(await db.OutboxEvents.SingleAsync(x=>x.Id==a.EventId)).DeliveryScope);
    }
    [ErpPostgresFact]
    public async Task Simultaneous_identical_confirmation_returns_the_same_durable_result()
    {
        var s=await Seed();var c=await Review(s);var a=await Task.WhenAll(Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default),Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default));
        Assert.Single(a,x=>x.Replayed);Assert.Single(a,x=>!x.Replayed);Assert.Equal(a[0].DocumentId,a[1].DocumentId);
    }
    [ErpPostgresTheory][InlineData("request")][InlineData("reason")][InlineData("fingerprint")]
    public async Task Different_confirmation_cannot_duplicate_completed_production(string kind)
    {
        var s=await Seed();var c=await Review(s);await Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default);
        var changed=kind=="request"?c with{RequestId=Guid.NewGuid()}:kind=="reason"?c with{Reason="Another reason for the same production"}:c with{ExpectedFingerprint=new('a',64)};
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Writer(s).CompleteAsync(s.Scope,s.OrderId,changed,default));
    }
    [ErpPostgresTheory][InlineData("recipe")][InlineData("request")][InlineData("permission")][InlineData("disabled")][InlineData("unfenced")]
    public async Task Changed_review_live_rights_and_inactive_gates_do_not_mutate_any_stock(string kind)
    {
        var s=await Seed(kind!="unfenced");var c=await Review(s);
        await using(var db=fixture.Context())
        {
            if(kind=="recipe")(await db.RecipeIngredients.SingleAsync(i=>i.RecipeId==s.RecipeId)).Qty=4;
            if(kind=="permission")(await db.Users.SingleAsync(u=>u.Id==s.S.Actor)).SetPermissions(new(){{"production.complete",false}});
            if(kind=="request")await Link(db,s,2);
            await db.SaveChangesAsync();
        }
        if(kind=="permission")await Assert.ThrowsAsync<ShelfGuard.Application.Services.WorkAccessDeniedException>(()=>Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default));
        else if(kind=="disabled")await Assert.ThrowsAsync<InventoryPostingDisabledException>(()=>Writer(s,false).CompleteAsync(s.Scope,s.OrderId,c,default));
        else await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default));
        await Unchanged(s);
    }
    [ErpPostgresTheory][InlineData("output")][InlineData("ledger")][InlineData("outbox")][InlineData("notification")][InlineData("seal")]
    public async Task Failure_at_each_phase_rolls_back_materials_output_document_and_notifications(string phase)
    {
        var s=await Seed();await using(var db=fixture.Context()){await Link(db,s,2);await db.SaveChangesAsync();}
        var c=await Review(s);await Assert.ThrowsAsync<InvalidOperationException>(()=>Writer(s,true,new Fail(phase)).CompleteAsync(s.Scope,s.OrderId,c,default));await Unchanged(s);
    }
    private sealed class Fail(string phase):SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
        {
            var db=e.Context!;
            if(phase=="output"&&db.ChangeTracker.Entries<ProductStock>().Any(x=>x.State==EntityState.Added&&x.Entity.SourceType=="production")
                ||phase=="ledger"&&db.ChangeTracker.Entries<InventoryLedgerEntry>().Any(x=>x.State==EntityState.Added)
                ||phase=="outbox"&&db.ChangeTracker.Entries<OutboxEvent>().Any(x=>x.State==EntityState.Added)
                ||phase=="notification"&&db.ChangeTracker.Entries<NotificationQueue>().Any(x=>x.State==EntityState.Added)
                ||phase=="seal"&&db.ChangeTracker.Entries<BusinessDocument>().Any(x=>x.Entity.SourceSystem==ProductionPostingStore.Source&&x.Entity.Status=="posted"))throw new InvalidOperationException("Synthetic production fault");
            return ValueTask.FromResult(result);
        }
    }
    private async Task Unchanged(Scenario s)
    {
        await using var db=fixture.Context();Assert.Equal(7.25m,await db.ProductStocks.Where(b=>b.TenantId==s.S.Tenant).SumAsync(b=>b.Quantity));
        Assert.Equal(ProductionOrderStatus.InProgress,(await db.ProductionOrders.SingleAsync(o=>o.Id==s.OrderId)).Status);
        Assert.False(await db.BusinessDocuments.AnyAsync(d=>d.TenantId==s.S.Tenant&&d.SourceSystem==ProductionPostingStore.Source));Assert.False(await db.ProductionOrderConsumptions.AnyAsync(c=>c.ProductionOrderId==s.OrderId));
        Assert.False(await db.StockEvents.AnyAsync(v=>v.TenantId==s.S.Tenant));Assert.False(await db.NotificationQueues.AnyAsync(v=>v.TenantId==s.S.Tenant));
    }
    private static async Task<Guid> Link(AppDbContext db,Scenario s,decimal qty)
    {
        var r=new ProductionRequest{TenantId=s.S.Tenant,StoreLocationId=s.S.Location,ProductionLocationId=s.S.Location,Status=ProductionRequestStatus.Accepted,NeededBy=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))};
        var line=new ProductionRequestLine{TenantId=s.S.Tenant,RequestId=r.Id,ItemId=s.OutputId,RequestedQty=qty,AcceptedQty=qty};
        db.AddRange(r,line,new ProductionOrderRequestLine{TenantId=s.S.Tenant,ProductionOrderId=s.OrderId,RequestLineId=line.Id,Qty=qty});await Task.CompletedTask;return r.Id;
    }
    [ErpPostgresFact]
    public async Task Readiness_notification_is_atomic_and_replay_does_not_notify_twice()
    {
        var s=await Seed();Guid request;await using(var db=fixture.Context()){request=await Link(db,s,2);await db.SaveChangesAsync();}
        var c=await Review(s);await Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default);await Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default);
        await using var check=fixture.Context();var n=await check.NotificationQueues.SingleAsync(x=>x.TenantId==s.S.Tenant);Assert.Equal("production_request.ready",n.EventType);
        Assert.Equal(request,JsonDocument.Parse(n.Payload!).RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal(ProductionRequestStatus.Accepted,(await check.ProductionRequests.SingleAsync(x=>x.Id==request)).Status);
    }
    [ErpPostgresTheory][InlineData("order")][InlineData("consumption")][InlineData("link")][InlineData("event")]
    public async Task Completed_source_facts_cannot_be_rewritten(string kind)
    {
        var s=await Seed();await using(var setup=fixture.Context()){await Link(setup,s,2);await setup.SaveChangesAsync();}
        await Writer(s).CompleteAsync(s.Scope,s.OrderId,await Review(s),default);
        await using var db=fixture.Context();var id=s.OrderId;
        var sql=kind switch{
            "order"=>$"UPDATE public.production_orders SET \"Notes\"='rewritten' WHERE \"Id\"='{id}'",
            "consumption"=>$"DELETE FROM public.production_order_consumptions WHERE \"ProductionOrderId\"='{id}'",
            "link"=>$"DELETE FROM public.production_order_request_lines WHERE \"ProductionOrderId\"='{id}'",
            _=>$"DELETE FROM public.stock_events WHERE \"TenantId\"='{s.S.Tenant}' AND \"Meta\"->>'production_order_id'='{id}'"};
        Assert.Equal("P4F01",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(sql))).SqlState);
    }
    [ErpPostgresFact]
    public async Task Shared_recipe_can_evolve_after_posting_without_changing_the_historical_snapshot_or_replay()
    {
        var s=await Seed();var c=await Review(s);var a=await Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default);
        string? snapshot;await using(var db=fixture.Context())
        {
            snapshot=(await db.DocumentLines.SingleAsync(l=>l.DocumentId==a.DocumentId&&l.ToLocationId!=null)).Metadata;
            (await db.RecipeIngredients.SingleAsync(i=>i.RecipeId==s.RecipeId)).Qty=5;await db.SaveChangesAsync();
        }
        Assert.True((await Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default)).Replayed);
        await using var check=fixture.Context();Assert.Equal(snapshot,(await check.DocumentLines.SingleAsync(l=>l.DocumentId==a.DocumentId&&l.ToLocationId!=null)).Metadata);
    }
    [ErpPostgresFact]
    public async Task Legacy_completion_is_rejected_inside_a_fenced_scope()
    {
        var s=await Seed();await using var db=fixture.Context();
        var ex=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.production_orders SET \"Status\"='Done',\"CompletedAt\"=now() WHERE \"Id\"={s.OrderId}"));Assert.Equal("P4F01",ex.SqlState);
        await Unchanged(s);
    }
    [ErpPostgresTheory][InlineData(false)][InlineData(true)]
    public async Task Receipt_ledger_cost_is_carried_exactly_and_mixed_unknown_cost_is_not_fabricated(bool mixed)
    {
        var received=await ReceiptPostingPostgresTests.Seed(fixture,1);await ReceiptPostingPostgresTests.Store(fixture,received).ReceiveAsync(received.Scope,received.Receipt,received.Fingerprint,default);
        var stock=new WriteOffPostingPostgresTests.Scenario(received.Inventory,Guid.Empty,null);var s0=received.Inventory;
        var output=new Item{TenantId=s0.Tenant,Name="Valued finished product",Unit="kg",ShelfLifeDays=5};
        var recipe=new Recipe{TenantId=s0.Tenant,Name="Valued recipe",OutputItemId=output.Id,OutputQty=2,Unit="kg"};
        recipe.Ingredients.Add(new(){RecipeId=recipe.Id,ItemId=s0.Item,Qty=mixed?5:2.25m,Unit="kg"});
        var order=new ProductionOrder{TenantId=s0.Tenant,LocationId=s0.Location,RecipeId=recipe.Id,PlannedQty=2,Status=ProductionOrderStatus.InProgress,StartedAt=DateTimeOffset.UtcNow};
        await using(var db=fixture.Context()){(await db.Tenants.SingleAsync(t=>t.Id==s0.Tenant)).UpdateModules(["inventory","production","procurement"]);db.AddRange(output,recipe,order);await db.SaveChangesAsync();}
        var s=new Scenario(stock,recipe.Id,order.Id,output.Id);var result=await Writer(s).CompleteAsync(s.Scope,s.OrderId,await Review(s),default);
        await using var check=fixture.Context();var entries=await check.InventoryLedgerEntries.Where(e=>e.PostingId==result.PostingId).ToListAsync();var made=entries.Single(e=>e.QuantityDelta>0);
        if(mixed){Assert.Null(made.UnitCost);Assert.Null(made.ValueDelta);Assert.Contains(entries,e=>e.QuantityDelta<0&&e.UnitCost==12.5m);Assert.Contains(entries,e=>e.QuantityDelta<0&&e.UnitCost==null);}
        else{Assert.Equal(14.0625m,made.UnitCost);Assert.Equal(28.125m,made.ValueDelta);Assert.Equal(0,entries.Sum(e=>e.ValueDelta));}
    }
    [ErpPostgresTheory][InlineData("event")][InlineData("consumption")][InlineData("projection")][InlineData("movement")][InlineData("notification")]
    public async Task Deferred_database_guards_reject_silently_omitted_companion_facts(string kind)
    {
        var s=await Seed();await using(var setup=fixture.Context()){await Link(setup,s,2);await setup.SaveChangesAsync();}
        var command=await Review(s);await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Writer(s,true,new Omit(kind)).CompleteAsync(s.Scope,s.OrderId,command,default));
        await Unchanged(s);
    }
    private sealed class Omit(string kind):SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
        {
            foreach(var entry in e.Context!.ChangeTracker.Entries().ToArray())
                if(entry.State==EntityState.Added&&(kind=="event"&&entry.Entity is StockEvent||kind=="consumption"&&entry.Entity is ProductionOrderConsumption
                    ||kind=="projection"&&entry.Entity is InventoryBalanceProjection||kind=="movement"&&entry.Entity is StockMovement||kind=="notification"&&entry.Entity is NotificationQueue))entry.State=EntityState.Detached;
            return ValueTask.FromResult(result);
        }
    }
    [ErpPostgresFact]
    public async Task Stock_history_counts_each_material_issue_and_output_only_once()
    {
        var s=await Seed();var today=DateOnly.FromDateTime(DateTime.UtcNow);await using var db=fixture.Context();var repo=new MovementRepository(db);
        var before=await repo.GetStockHistoryAsync(s.S.Tenant,s.S.Item,s.S.Location,today);
        await Writer(s).CompleteAsync(s.Scope,s.OrderId,await Review(s),default);
        var material=await repo.GetStockHistoryAsync(s.S.Tenant,s.S.Item,s.S.Location,today);var output=await repo.GetStockHistoryAsync(s.S.Tenant,s.OutputId,s.S.Location,today);
        Assert.Equal(before.DailyChanges.GetValueOrDefault(today)-4.5m,material.DailyChanges.GetValueOrDefault(today));Assert.Equal(2,output.DailyChanges.GetValueOrDefault(today));
    }
    [ErpPostgresFact]
    public async Task A_request_notifies_only_when_its_last_live_order_is_complete()
    {
        var s=await Seed();var next=new ProductionOrder{TenantId=s.S.Tenant,LocationId=s.S.Location,RecipeId=s.RecipeId,PlannedQty=1,Status=ProductionOrderStatus.InProgress,StartedAt=DateTimeOffset.UtcNow};
        await using(var db=fixture.Context())
        {
            var id=await Link(db,s,2);var line=db.ChangeTracker.Entries<ProductionRequestLine>().Single().Entity;line.AcceptedQty=3;line.RequestedQty=3;
            db.AddRange(next,new ProductionOrderRequestLine{TenantId=s.S.Tenant,ProductionOrderId=next.Id,RequestLineId=line.Id,Qty=1});await db.SaveChangesAsync();
        }
        await Writer(s).CompleteAsync(s.Scope,s.OrderId,await Review(s),default);
        await using(var db=fixture.Context())Assert.False(await db.NotificationQueues.AnyAsync(n=>n.TenantId==s.S.Tenant));
        var second=s with{OrderId=next.Id};await Writer(second).CompleteAsync(second.Scope,second.OrderId,await Review(second),default);
        await using var check=fixture.Context();Assert.Single(await check.NotificationQueues.Where(n=>n.TenantId==s.S.Tenant).ToListAsync());
    }
    [ErpPostgresFact]
    public async Task Client_request_identity_cannot_be_reused_for_another_order()
    {
        var s=await Seed();var c=await Review(s);await Writer(s).CompleteAsync(s.Scope,s.OrderId,c,default);
        var order=new ProductionOrder{TenantId=s.S.Tenant,LocationId=s.S.Location,RecipeId=s.RecipeId,PlannedQty=1,Status=ProductionOrderStatus.InProgress,StartedAt=DateTimeOffset.UtcNow};
        await using(var db=fixture.Context()){db.Add(order);await db.SaveChangesAsync();}
        var other=s with{OrderId=order.Id};var changed=(await Review(other)) with{RequestId=c.RequestId};
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Writer(other).CompleteAsync(other.Scope,other.OrderId,changed,default));
    }
    [ErpPostgresFact]
    public async Task Readiness_rejects_a_disabled_source_guard()
    {
        var s=await Seed();await using var db=fixture.Context();await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE public.production_order_consumptions DISABLE TRIGGER erp_w4_production_consumptions_before");
        Assert.False(await db.Database.SqlQuery<bool>($"SELECT public.erp_w4_production_ready({s.S.Tenant},{s.S.Location}) AS \"Value\"").SingleAsync());await tx.RollbackAsync();
    }
}

