using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
namespace ShelfGuard.Tests.Erp;
[Collection("ERP schema acceptance")]
public sealed class ProductionDesktopTests(ErpSchemaFixture fixture)
{
    private sealed class Access(WorkAccessScope scope):IWorkAccessContext{public Task<WorkAccessScope?> GetAsync(CancellationToken ct=default)=>Task.FromResult<WorkAccessScope?>(scope);}
    private DbContextOptions<AppDbContext> Options()=>new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).IgnoreManyServiceProvidersWarning().Options;
    private ProductionDesktopStore Store(ProductionPostingPreviewPostgresTests.Scenario s,WorkAccessScope? scope=null,bool enabled=true)=>new(Options(),new Access(scope??s.Scope),s.Stock.Gate,new(enabled),TimeProvider.System);
    private async Task<ProductionPostingPreviewPostgresTests.Scenario> Seed(){var s=await ProductionPostingPreviewPostgresTests.Seed(fixture);await using var db=fixture.Context();(await db.Locations.SingleAsync(l=>l.Id==s.S.Location)).LocationType="production";await db.SaveChangesAsync();return s;}
    private static ProductionDesktopCreate Draft(ProductionPostingPreviewPostgresTests.Scenario s)=>new(Guid.NewGuid(),s.S.Location,s.RecipeId,2,"Reviewed manual production order");
    [ErpPostgresFact]public async Task Desktop_create_concurrent_retry_creates_one_planned_order_and_no_inventory_facts()
    {
        var s=await Seed();var c=Draft(s);var result=await Task.WhenAll(Store(s).CreateAsync(c,default),Store(s).CreateAsync(c,default));Assert.Single(result,x=>x.Replayed);Assert.Equal(result[0].OrderId,result[1].OrderId);
        var rows=await Store(s).OrdersAsync(s.S.Location,"planned",null,1,default);Assert.Equal(c.RequestId,Assert.Single(rows.Items).Id);Assert.Equal(50,rows.PageSize);
        await using var db=fixture.Context();Assert.False(await db.ProductStocks.AnyAsync(b=>b.SourceId==c.RequestId));Assert.False(await db.DocumentPostings.AnyAsync(p=>p.IdempotencyKey.Contains(c.RequestId.ToString("N"))));
    }
    [ErpPostgresFact]public async Task Desktop_create_rejects_changed_intent_and_disabled_scope()
    {
        var s=await Seed();var c=Draft(s);await Store(s).CreateAsync(c,default);
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).CreateAsync(c with{Quantity=3},default));
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).CreateAsync(c with{Notes="Other reviewed explanation"},default));
        await Assert.ThrowsAsync<InventoryPostingDisabledException>(()=>Store(s,enabled:false).CreateAsync(c,default));
    }
    [ErpPostgresFact]public async Task Desktop_source_start_and_cancel_have_durable_replay_and_no_stock_postings()
    {
        var s=await Seed();var c=Draft(s);var store=Store(s);await store.CreateAsync(c,default);
        foreach(var phase in new[]{"start","cancel"}){var p=await store.ReviewAsync(c.RequestId,phase,default);var command=new ProductionDesktopDecision(Guid.NewGuid(),p.Order.Fingerprint,"Reviewed production source decision");var a=await store.DecideAsync(c.RequestId,phase,command,default);Assert.Equal(a with{Replayed=true},await store.DecideAsync(c.RequestId,phase,command,default));await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.DecideAsync(c.RequestId,phase,command with{Reason="Different reviewed reason"},default));}
        await using var db=fixture.Context();Assert.Equal(ProductionOrderStatus.Cancelled,(await db.ProductionOrders.SingleAsync(o=>o.Id==c.RequestId)).Status);Assert.Equal(2,await db.BusinessDocuments.CountAsync(d=>d.SourceId==c.RequestId.ToString()));Assert.False(await db.DocumentPostings.AnyAsync(p=>db.BusinessDocuments.Any(d=>d.Id==p.DocumentId&&d.SourceId==c.RequestId.ToString())));
    }
    [ErpPostgresFact]public async Task Desktop_source_stale_review_cannot_start_order()
    {
        var s=await Seed();var c=Draft(s);var store=Store(s);await store.CreateAsync(c,default);var p=await store.ReviewAsync(c.RequestId,"start",default);
        await using(var db=fixture.Context()){var o=await db.ProductionOrders.SingleAsync(o=>o.Id==c.RequestId);o.Notes="Changed source explanation";await db.SaveChangesAsync();}
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.DecideAsync(c.RequestId,"start",new(Guid.NewGuid(),p.Order.Fingerprint,"Reviewed production source decision"),default));
    }
    [ErpPostgresFact]public async Task Desktop_start_replays_after_real_completion_without_repeating_state_transition()
    {
        var s=await Seed();var store=Store(s);var c=Draft(s);await store.CreateAsync(c,default);var start=await store.ReviewAsync(c.RequestId,"start",default);var command=new ProductionDesktopDecision(Guid.NewGuid(),start.Order.Fingerprint,"Reviewed production start decision");await store.DecideAsync(c.RequestId,"start",command,default);
        var reader=new ProductionPostingPreviewReader(Options(),s.Stock.Gate,new(true),TimeProvider.System);var plan=await reader.ReadAsync(s.Scope,c.RequestId,default);Assert.True(plan.CanPost);
        await new ProductionPostingStore(Options(),s.Stock.Gate,new(true),TimeProvider.System,reader).CompleteAsync(s.Scope,c.RequestId,new(Guid.NewGuid(),plan.Fingerprint,"Reviewed materials and finished output"),default);
        var replay=await store.DecideAsync(c.RequestId,"start",command,default);Assert.True(replay.Replayed);Assert.Equal("done",replay.Status);
    }
    [ErpPostgresFact]public async Task Desktop_source_enforces_actual_assignment_and_live_capabilities()
    {
        var s=await Seed();await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Store(s,s.Scope with{LocationIds=[]}).OrdersAsync(s.S.Location,null,null,1,default));
        var denied=s.Scope with{Permissions=new Dictionary<string,bool>{{"production.manage",false}}};await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Store(s,denied).CreateAsync(Draft(s),default));
        await using(var db=fixture.Context()){var user=await db.Users.SingleAsync(u=>u.Id==s.S.Actor);user.SetPermissions(new Dictionary<string,bool>{{"production.manage",false}});await db.SaveChangesAsync();}
        Assert.False((await Store(s).AccessAsync(default)).CanCreate);await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Store(s).CreateAsync(Draft(s),default));
    }
    [ErpPostgresFact]public async Task Desktop_cancel_does_not_orphan_linked_store_requests()
    {
        var s=await Seed();await using(var db=fixture.Context()){var r=new ProductionRequest{TenantId=s.S.Tenant,StoreLocationId=s.S.Location,ProductionLocationId=s.S.Location,Status=ProductionRequestStatus.Accepted,NeededBy=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))};var line=new ProductionRequestLine{TenantId=s.S.Tenant,RequestId=r.Id,ItemId=s.OutputId,RequestedQty=2,AcceptedQty=2};db.AddRange(r,line,new ProductionOrderRequestLine{TenantId=s.S.Tenant,ProductionOrderId=s.OrderId,RequestLineId=line.Id,Qty=2});await db.SaveChangesAsync();}
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s).ReviewAsync(s.OrderId,"cancel",default));
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(1.001)][InlineData(10000000)][InlineData(100000000)]public void Desktop_create_requires_exact_positive_output(decimal qty)=>Assert.Throws<ArgumentException>(()=>ProductionDesktopRules.Normalize(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),qty,null)));
    [Fact]public void Desktop_source_normalizes_explanation_and_rejects_unknown_phase()
    {var c=new ProductionDesktopCreate(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),1,"  Source notes  ");Assert.Equal("Source notes",ProductionDesktopRules.Normalize(c).Notes);Assert.Equal(9_999_999.99m,ProductionDesktopRules.Normalize(c with{Quantity=9_999_999.99m}).Quantity);Assert.Throws<ArgumentException>(()=>ProductionDesktopRules.Normalize(c with{Notes="bad\nnotes"}));Assert.Throws<ArgumentException>(()=>ProductionDesktopRules.Phase("complete"));}
}
