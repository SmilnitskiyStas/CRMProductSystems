using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Application.Features.Transfers;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
namespace ShelfGuard.Tests.Erp;
[Collection("ERP schema acceptance")]
public sealed class RequestDesktopTests(ErpSchemaFixture fixture)
{
    private sealed class Access(WorkAccessScope scope):IWorkAccessContext{public Task<WorkAccessScope?> GetAsync(CancellationToken ct=default)=>Task.FromResult<WorkAccessScope?>(scope);}
    private DbContextOptions<AppDbContext> Options()=>new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!)).IgnoreManyServiceProvidersWarning().Options;
    private RequestDesktopStore Store(WorkAccessScope scope)=>new(Options(),new Access(scope),Substitute.For<ITransferService>(),TimeProvider.System);
    private async Task<(ProductionPostingPreviewPostgresTests.Scenario S,Guid Store,WorkAccessScope Scope)> Seed()
    {
        var s=await ProductionPostingPreviewPostgresTests.Seed(fixture);await using var db=fixture.Context();var prod=await db.Locations.SingleAsync(l=>l.Id==s.S.Location);prod.Type="production";prod.LocationType="production";var store=new Location{TenantId=s.S.Tenant,Name="Desktop requesting store"};db.Add(store);db.Add(UserLocation.Create(s.S.Tenant,s.S.Actor,store.Id,null));await db.SaveChangesAsync();return(s,store.Id,s.Scope with{LocationIds=[s.S.Location,store.Id]});
    }
    private static RequestDesktopCreate Draft(ProductionPostingPreviewPostgresTests.Scenario s,Guid store,decimal qty=2)=>new(Guid.NewGuid(),store,s.S.Location,DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),"Store request notes",[new(s.OutputId,qty)]);
    private async Task<RequestDesktopResult> Decision(RequestDesktopStore store,Guid id,Guid location,string side,string phase,decimal? accepted=null)
    {var s=await store.GetAsync(id,location,side,default);return await store.DecideAsync(id,location,side,phase,new(Guid.NewGuid(),s.Fingerprint,"Reviewed store production demand",phase=="review"?s.Request.Lines.Select(l=>new ProductionRequestLineReviewDto(l.Id,accepted??l.RequestedQty,accepted<l.RequestedQty?"Reviewed capacity reduction":null)).ToList():null),default);}
    [ErpPostgresFact]public async Task Concurrent_draft_retries_have_one_source_audit_and_no_inventory_posting()
    {
        var s=await Seed();var c=Draft(s.S,s.Store);var rs=await Task.WhenAll(Store(s.Scope).CreateAsync(c,default),Store(s.Scope).CreateAsync(c,default));Assert.Single(rs,r=>r.Replayed);Assert.Equal(rs[0].DocumentId,rs[1].DocumentId);
        await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Store(s.Scope).CreateAsync(c with{Notes="Different request intent"},default));await using var db=fixture.Context();Assert.Single(await db.ProductionRequests.Where(r=>r.Id==c.OperationId).ToListAsync());Assert.False(await db.DocumentPostings.AnyAsync(p=>p.DocumentId==rs[0].DocumentId));Assert.Single((await Store(s.Scope).ListAsync(new(s.Store,"store",null,null),default)).Items);
    }
    [ErpPostgresFact]public async Task Partial_acceptance_plans_once_replay_after_later_edit_does_not_restore_old_demand()
    {
        var s=await Seed();var store=Store(s.Scope);var c=Draft(s.S,s.Store,3);await store.CreateAsync(c,default);await Decision(store,c.OperationId,s.Store,"store","submit");var src=await store.GetAsync(c.OperationId,s.S.S.Location,"production",default);var line=Assert.Single(src.Request.Lines);var cmd=new RequestDesktopDecision(Guid.NewGuid(),src.Fingerprint,"Reviewed reduced store demand",[new(line.Id,2,"Available production capacity")]);var a=await store.DecideAsync(c.OperationId,s.S.S.Location,"production","review",cmd,default);await Decision(store,c.OperationId,s.S.S.Location,"production","review",1);
        Assert.True((await store.DecideAsync(c.OperationId,s.S.S.Location,"production","review",cmd,default)).Replayed);var now=await store.GetAsync(c.OperationId,s.S.S.Location,"production",default);Assert.Equal("partial",now.Request.Status);var link=Assert.Single(Assert.Single(now.Request.Lines).LinkedOrders!);Assert.Equal(1,link.Qty);await using var db=fixture.Context();Assert.Equal(1,(await db.ProductionOrders.SingleAsync(o=>o.Id==link.OrderId)).PlannedQty);Assert.False(await db.DocumentPostings.AnyAsync(p=>p.DocumentId==a.DocumentId));
    }
    [ErpPostgresFact]public async Task Two_requests_for_same_product_and_due_date_aggregate_and_reduction_preserves_other_demand()
    {
        var s=await Seed();var store=Store(s.Scope);var ids=new List<Guid>();foreach(var qty in new[]{2,3}){var c=Draft(s.S,s.Store,qty);ids.Add(c.OperationId);await store.CreateAsync(c,default);await Decision(store,c.OperationId,s.Store,"store","submit");await Decision(store,c.OperationId,s.S.S.Location,"production","review");}
        await Decision(store,ids[0],s.S.S.Location,"production","review",0);await using var db=fixture.Context();var orders=await db.ProductionOrders.Where(o=>o.TenantId==s.S.S.Tenant&&o.Status==ProductionOrderStatus.Planned).ToListAsync();Assert.Equal(3,Assert.Single(orders).PlannedQty);Assert.Single(await db.ProductionOrderRequestLines.Where(l=>l.ProductionOrderId==orders[0].Id).ToListAsync());
    }
    [ErpPostgresFact]public async Task Concurrent_reviews_with_different_command_ids_cannot_double_plan()
    {
        var s=await Seed();var c=Draft(s.S,s.Store);var store=Store(s.Scope);await store.CreateAsync(c,default);await Decision(store,c.OperationId,s.Store,"store","submit");var src=await store.GetAsync(c.OperationId,s.S.S.Location,"production",default);var cmd=new RequestDesktopDecision(Guid.NewGuid(),src.Fingerprint,"Reviewed store production demand",src.Request.Lines.Select(l=>new ProductionRequestLineReviewDto(l.Id,2,null)).ToList());
        var errors=await Task.WhenAll(Record.ExceptionAsync(()=>Store(s.Scope).DecideAsync(c.OperationId,s.S.S.Location,"production","review",cmd,default)),Record.ExceptionAsync(()=>Store(s.Scope).DecideAsync(c.OperationId,s.S.S.Location,"production","review",cmd with{OperationId=Guid.NewGuid()},default)));Assert.Single(errors,e=>e==null);Assert.IsType<InventoryPostingConflictException>(Assert.Single(errors,e=>e!=null));
    }
    [ErpPostgresFact]public async Task Actual_store_assignment_and_live_permission_are_required_even_for_replay()
    {
        var s=await Seed();var store=Store(s.Scope);var c=Draft(s.S,s.Store);await store.CreateAsync(c,default);await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Store(s.S.Scope).GetAsync(c.OperationId,s.Store,"store",default));await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>store.GetAsync(c.OperationId,s.Store,"production",default));
        await using(var db=fixture.Context()){(await db.Users.SingleAsync(u=>u.Id==s.S.S.Actor)).SetPermissions(new(){{"production.manage",false}});await db.SaveChangesAsync();}Assert.False((await store.AccessAsync(default)).CanCreate);await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>store.CreateAsync(c,default));
    }
    [ErpPostgresFact]public async Task Store_can_submit_without_production_module_but_cannot_review()
    {
        var s=await Seed();await using(var db=fixture.Context()){(await db.Tenants.SingleAsync(t=>t.Id==s.S.S.Tenant)).UpdateModules(["inventory"]);await db.SaveChangesAsync();}var store=Store(s.Scope);var c=Draft(s.S,s.Store);await store.CreateAsync(c,default);await Decision(store,c.OperationId,s.Store,"store","submit");Assert.False((await store.AccessAsync(default)).CanReview);await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>store.GetAsync(c.OperationId,s.S.S.Location,"production",default));
    }
    [ErpPostgresFact]public async Task Started_linked_production_blocks_replanning_and_completes_with_request_allocations()
    {
        var s=await Seed();var store=Store(s.Scope);var c=Draft(s.S,s.Store);await store.CreateAsync(c,default);await Decision(store,c.OperationId,s.Store,"store","submit");await Decision(store,c.OperationId,s.S.S.Location,"production","review");var src=await store.GetAsync(c.OperationId,s.S.S.Location,"production",default);var order=Assert.Single(Assert.Single(src.Request.Lines).LinkedOrders!).OrderId;
        var prod=new ProductionDesktopStore(Options(),new Access(s.Scope),s.S.Stock.Gate,new(true),TimeProvider.System);var before=await prod.ReviewAsync(order,"start",default);await prod.DecideAsync(order,"start",new(Guid.NewGuid(),before.Order.Fingerprint,"Reviewed linked production start"),default);Assert.False((await store.GetAsync(c.OperationId,s.S.S.Location,"production",default)).CanReview);await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Decision(store,c.OperationId,s.S.S.Location,"production","review",1));
        var reader=new ProductionPostingPreviewReader(Options(),s.S.Stock.Gate,new(true),TimeProvider.System);var p=await reader.ReadAsync(s.Scope,order,default);Assert.True(p.CanPost);await new ProductionPostingStore(Options(),s.S.Stock.Gate,new(true),TimeProvider.System,reader).CompleteAsync(s.Scope,order,new(Guid.NewGuid(),p.Fingerprint,"Reviewed finished request production"),default);Assert.Equal("ready",(await store.GetAsync(c.OperationId,s.Store,"store",default)).Request.ProductionStage);
    }
    [ErpPostgresFact]public async Task Stale_source_missing_reduction_reason_and_missing_recipe_leave_no_plan()
    {
        var s=await Seed();var store=Store(s.Scope);var c=Draft(s.S,s.Store);await store.CreateAsync(c,default);await Decision(store,c.OperationId,s.Store,"store","submit");var src=await store.GetAsync(c.OperationId,s.S.S.Location,"production",default);var line=Assert.Single(src.Request.Lines);var cmd=new RequestDesktopDecision(Guid.NewGuid(),src.Fingerprint,"Reviewed store production demand",[new(line.Id,1,null)]);await Assert.ThrowsAsync<ArgumentException>(()=>store.DecideAsync(c.OperationId,s.S.S.Location,"production","review",cmd,default));await using(var db=fixture.Context()){(await db.Recipes.SingleAsync(r=>r.Id==s.S.RecipeId)).IsActive=false;await db.SaveChangesAsync();}await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.DecideAsync(c.OperationId,s.S.S.Location,"production","review",cmd with{Lines=[new(line.Id,2,null)]},default));Assert.Equal("submitted",(await store.GetAsync(c.OperationId,s.Store,"store",default)).Request.Status);
    }
    [ErpPostgresFact]public async Task Reject_and_cancel_create_audits_without_inventory_and_replay_exactly()
    {
        var s=await Seed();var store=Store(s.Scope);foreach(var phase in new[]{"cancel","reject"}){var c=Draft(s.S,s.Store);await store.CreateAsync(c,default);await Decision(store,c.OperationId,s.Store,"store","submit");var side=phase=="cancel"?"store":"production";var loc=side=="store"?s.Store:s.S.S.Location;var src=await store.GetAsync(c.OperationId,loc,side,default);var cmd=new RequestDesktopDecision(Guid.NewGuid(),src.Fingerprint,"Reviewed final request decision");var result=await store.DecideAsync(c.OperationId,loc,side,phase,cmd,default);Assert.True((await store.DecideAsync(c.OperationId,loc,side,phase,cmd,default)).Replayed);await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>store.DecideAsync(c.OperationId,loc,side,phase,cmd with{Reason="Different final request decision"},default));Assert.Equal(phase=="cancel"?"cancelled":"rejected",(await store.GetAsync(c.OperationId,loc,side,default)).Request.Status);await using var db=fixture.Context();Assert.False(await db.DocumentPostings.AnyAsync(p=>p.DocumentId==result.DocumentId));}
    }
    [ErpPostgresFact]public async Task Aggregated_output_overflow_rolls_back_review_and_audit()
    {
        var s=await Seed();var store=Store(s.Scope);foreach(var qty in new[]{9_999_999.99m,0.01m}){var c=Draft(s.S,s.Store,qty);await store.CreateAsync(c,default);await Decision(store,c.OperationId,s.Store,"store","submit");if(qty>1)await Decision(store,c.OperationId,s.S.S.Location,"production","review");else{await Assert.ThrowsAsync<InventoryPostingConflictException>(()=>Decision(store,c.OperationId,s.S.S.Location,"production","review"));Assert.Equal("submitted",(await store.GetAsync(c.OperationId,s.Store,"store",default)).Request.Status);}}
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(1.001)][InlineData(10000000)]public void Request_quantity_requires_exact_bounded_positive_values(decimal qty)=>Assert.Throws<ArgumentException>(()=>RequestDesktopRules.Quantity(qty));
    [Fact]public void Request_commands_reject_duplicates_and_unknown_side(){Assert.Throws<ArgumentException>(()=>RequestDesktopRules.Side("both"));var id=Guid.NewGuid();Assert.Throws<ArgumentException>(()=>RequestDesktopRules.Normalize(new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new(2026,10,29),null,[new(id,1),new(id,2)])));RequestDesktopRules.Quantity(0,true);}
}
