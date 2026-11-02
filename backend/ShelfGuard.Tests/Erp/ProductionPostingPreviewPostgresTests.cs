using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;
using ShelfGuard.Infrastructure.Data.Repositories;
using ShelfGuard.Tests.Infrastructure;
using Xunit;
namespace ShelfGuard.Tests.Erp;

[Collection("ERP schema acceptance")]
public sealed class ProductionPostingPreviewPostgresTests(ErpSchemaFixture fixture)
{
    internal sealed record Scenario(WriteOffPostingPostgresTests.Scenario Stock,Guid RecipeId,Guid OrderId,Guid OutputId)
    {
        internal InventoryFenceScenario S=>Stock.Inventory;
        internal WorkAccessScope Scope=>Stock.Scope;
    }
    internal static async Task<Scenario> Seed(ErpSchemaFixture f,bool fenced=true)
    {
        var s=await WriteOffPostingPostgresTests.Seed(f,true,fenced);
        await using var db=f.Context();(await db.Tenants.SingleAsync(t=>t.Id==s.Inventory.Tenant)).UpdateModules(["inventory","production"]);
        (await db.Items.SingleAsync(i=>i.Id==s.Inventory.Item)).PricePurchase=999;
        var output=new Item{TenantId=s.Inventory.Tenant,Name="Synthetic finished product",Unit="kg",ShelfLifeDays=5,PricePurchase=888};
        var recipe=new Recipe{TenantId=s.Inventory.Tenant,Name="Synthetic BOM",OutputItemId=output.Id,OutputQty=2,Unit="kg"};
        recipe.Ingredients.Add(new(){RecipeId=recipe.Id,ItemId=s.Inventory.Item,Qty=4.5m,Unit="l"});
        var order=new ProductionOrder{TenantId=s.Inventory.Tenant,LocationId=s.Inventory.Location,RecipeId=recipe.Id,PlannedQty=2,Status=ProductionOrderStatus.InProgress,StartedAt=DateTimeOffset.UtcNow};
        db.AddRange(output,recipe,order);await db.SaveChangesAsync();return new(s,recipe.Id,order.Id,output.Id);
    }
    private ProductionPostingPreviewReader Reader(Scenario s,bool configured=true,bool restricted=false)=>new(
        new DbContextOptionsBuilder<AppDbContext>(TestPostgres.Options(Environment.GetEnvironmentVariable("SHELFGUARD_ERP_SCHEMA_TEST_DB")!))
            .AddInterceptors(restricted?[new ReaderRole(fixture.ReaderRole)]:[]).IgnoreManyServiceProvidersWarning().Options,s.Stock.Gate,new(configured),new FixedClock());
    private sealed class FixedClock:TimeProvider { public override DateTimeOffset GetUtcNow()=>new(2026,10,27,12,0,0,TimeSpan.Zero); }
    private sealed class ReaderRole(string role):DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection connection,ConnectionEndEventData e,CancellationToken ct=default)
        {
            await using var command=connection.CreateCommand();command.CommandText="SET ROLE "+role;await command.ExecuteNonQueryAsync(ct);
        }
    }
    [ErpPostgresFact]
    public async Task Preview_is_tenant_scoped_read_only_and_preserves_unknown_cost_despite_catalog_price()
    {
        var s=await Seed(fixture);await using var check=fixture.Context();
        var before=await Fingerprint(check,s);var p=await Reader(s,restricted:true).ReadAsync(s.Scope,s.OrderId,default);
        Assert.True(p.Reviewable);Assert.True(p.Fenced);Assert.True(p.Configured);Assert.True(p.CanPost);Assert.True(p.WriterImplemented);Assert.True(p.Ready);
        Assert.Equal(new decimal[]{3.25m,1.25m},p.Materials.Select(t=>t.Quantity));Assert.Null(p.Output.MaterialCost);Assert.All(p.Materials,t=>Assert.Null(t.UnitCost));
        Assert.Equal(new DateOnly(2026,11,1),p.Output.ExpiryDate);Assert.Equal(before,await Fingerprint(check,s));
        var again=await Reader(s,restricted:true).ReadAsync(s.Scope,s.OrderId,default);Assert.Equal(p.Fingerprint,again.Fingerprint);
    }
    [ErpPostgresTheory][InlineData("foreign")][InlineData("missing")]
    public async Task Foreign_or_missing_order_is_not_found_without_source_data(string kind)
    {
        var s=await Seed(fixture);var other=await Seed(fixture);
        await Assert.ThrowsAsync<InventoryPostingNotFoundException>(()=>Reader(s,restricted:true).ReadAsync(s.Scope,kind=="foreign"?other.OrderId:Guid.NewGuid(),default));
    }
    [ErpPostgresTheory][InlineData("module")][InlineData("permission")][InlineData("assignment")][InlineData("location")][InlineData("user")]
    public async Task Preview_revalidates_live_rights_and_location_even_for_a_stale_scope(string kind)
    {
        var s=await Seed(fixture);await using(var db=fixture.Context())
        {
            if(kind=="module")(await db.Tenants.SingleAsync(t=>t.Id==s.S.Tenant)).UpdateModules(["production"]);
            if(kind=="permission")(await db.Users.SingleAsync(u=>u.Id==s.S.Actor)).SetPermissions(new(){{"production.complete",false}});
            if(kind=="assignment")await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM public.user_locations WHERE \"TenantId\"={s.S.Tenant}");
            if(kind=="location")(await db.Locations.SingleAsync(l=>l.Id==s.S.Location)).IsActive=false;
            if(kind=="user")(await db.Users.SingleAsync(u=>u.Id==s.S.Actor)).Deactivate();
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Reader(s,restricted:true).ReadAsync(s.Scope,s.OrderId,default));
    }
    [ErpPostgresFact]
    public async Task Capability_template_is_checked_live_instead_of_trusting_old_capability_claims()
    {
        var s=await Seed(fixture);var role=TenantRole.Create(s.S.Tenant,"Synthetic producer",["production.complete"],null);
        await using(var db=fixture.Context()){db.Add(role);var user=await db.Users.SingleAsync(u=>u.Id==s.S.Actor);user.SetRole("staff");user.SetTenantRole(role.Id);await db.SaveChangesAsync();}
        var scope=s.Scope with{Role="staff",Capabilities=new HashSet<string>{"production.complete"}};
        Assert.True((await Reader(s,restricted:true).ReadAsync(scope,s.OrderId,default)).Reviewable);
        await using(var db=fixture.Context()){(await db.Users.SingleAsync(u=>u.Id==s.S.Actor)).SetTenantRole(null);await db.SaveChangesAsync();}
        await Assert.ThrowsAsync<WorkAccessDeniedException>(()=>Reader(s,restricted:true).ReadAsync(scope,s.OrderId,default));
    }
    [ErpPostgresFact]
    public async Task Default_off_and_unfenced_review_never_turns_into_a_posting_permission()
    {
        var s=await Seed(fixture,false);var p=await Reader(s,false,true).ReadAsync(s.Scope,s.OrderId,default);
        Assert.False(p.Fenced);Assert.False(p.Configured);Assert.False(p.CanPost);Assert.True(p.WriterImplemented);
    }
    [ErpPostgresFact]
    public async Task Diverged_projection_blocks_review_and_does_not_create_a_repair_or_partial_posting()
    {
        var s=await Seed(fixture);await using(var db=fixture.Context()){var balance=await db.InventoryBalanceProjections.SingleAsync(b=>b.TenantId==s.S.Tenant&&b.BatchId==s.S.Batch);balance.Quantity+=1;await db.SaveChangesAsync();}
        var p=await Reader(s,restricted:true).ReadAsync(s.Scope,s.OrderId,default);
        Assert.False(p.Reviewable);Assert.Contains(p.Issues,i=>i.Code=="inventory_not_reconciled");
        await using var check=fixture.Context();Assert.False(await check.BusinessDocuments.AnyAsync(d=>d.TenantId==s.S.Tenant&&d.SourceSystem.StartsWith("production.")));
    }
    [ErpPostgresFact]
    public async Task Projection_cannot_reference_another_batchs_ledger_even_in_the_same_tenant()
    {
        var s=await Seed(fixture);
        await using(var db=fixture.Context())
        {
            var ledger=(await db.InventoryLedgerEntries.SingleAsync(e=>e.TenantId==s.S.Tenant&&e.BatchId==s.Stock.SecondBatch)).Id;
            var own=await db.InventoryBalanceProjections.SingleAsync(b=>b.TenantId==s.S.Tenant&&b.BatchId==s.S.Batch);
            own.LastLedgerEntryId=ledger;await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        }
        var p=await Reader(s,restricted:true).ReadAsync(s.Scope,s.OrderId,default);Assert.True(p.Reviewable);
        Assert.Equal("l",p.Source.Batches.Single(b=>b.Id==s.S.Batch).LedgerUnit);
    }
    [ErpPostgresFact]
    public async Task Changed_accepted_request_quantity_changes_fingerprint_and_blocks_the_old_production_plan()
    {
        var s=await Seed(fixture);var request=new ProductionRequest{TenantId=s.S.Tenant,StoreLocationId=s.S.Location,ProductionLocationId=s.S.Location,Status=ProductionRequestStatus.Accepted,NeededBy=new(2026,10,28)};
        var line=new ProductionRequestLine{TenantId=s.S.Tenant,RequestId=request.Id,ItemId=s.OutputId,RequestedQty=2,AcceptedQty=2};
        await using(var db=fixture.Context()){db.AddRange(request,line,new ProductionOrderRequestLine{TenantId=s.S.Tenant,ProductionOrderId=s.OrderId,RequestLineId=line.Id,Qty=2});await db.SaveChangesAsync();}
        var first=await Reader(s,restricted:true).ReadAsync(s.Scope,s.OrderId,default);Assert.True(first.Reviewable);Assert.Equal(2,first.Source.Requests.Single().AcceptedQuantity);
        await using(var db=fixture.Context()){(await db.ProductionRequestLines.SingleAsync(l=>l.Id==line.Id)).AcceptedQty=1;await db.SaveChangesAsync();}
        var second=await Reader(s,restricted:true).ReadAsync(s.Scope,s.OrderId,default);Assert.False(second.Reviewable);Assert.NotEqual(first.Fingerprint,second.Fingerprint);
    }
    [ErpPostgresFact]
    public async Task A_link_to_another_production_location_is_blocked_without_returning_its_request_fields()
    {
        var s=await Seed(fixture);var other=new Location{TenantId=s.S.Tenant,Name="Other production"};
        var request=new ProductionRequest{TenantId=s.S.Tenant,StoreLocationId=s.S.Location,ProductionLocationId=other.Id,Status=ProductionRequestStatus.Accepted,NeededBy=new(2026,10,28)};
        var line=new ProductionRequestLine{TenantId=s.S.Tenant,RequestId=request.Id,ItemId=s.OutputId,RequestedQty=2,AcceptedQty=2};
        await using(var db=fixture.Context()){db.AddRange(other,request,line,new ProductionOrderRequestLine{TenantId=s.S.Tenant,ProductionOrderId=s.OrderId,RequestLineId=line.Id,Qty=2});await db.SaveChangesAsync();}
        var p=await Reader(s,restricted:true).ReadAsync(s.Scope,s.OrderId,default);Assert.False(p.Reviewable);
        var link=p.Source.Requests.Single();Assert.False(link.InScope);Assert.Null(link.RequestId);Assert.Null(link.Status);Assert.Null(link.AcceptedQuantity);
    }
    private static Task<string> Fingerprint(AppDbContext db,Scenario s)=>db.Database.SqlQuery<string>($"SELECT md5(jsonb_build_object('stock',(SELECT jsonb_agg(to_jsonb(x) ORDER BY x.\"Id\") FROM public.product_stock x WHERE x.\"TenantId\"={s.S.Tenant}),'order',(SELECT to_jsonb(o) FROM public.production_orders o WHERE o.\"Id\"={s.OrderId}),'documents',(SELECT count(*) FROM public.business_documents d WHERE d.\"TenantId\"={s.S.Tenant}),'events',(SELECT count(*) FROM public.stock_events e WHERE e.\"TenantId\"={s.S.Tenant}))::text) AS \"Value\"").SingleAsync();
}
