using System.Data;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ProductionPostingPreviewReader(DbContextOptions<AppDbContext> options,IInventoryPostingGate gate,
    ProductionPostingOptions configured,TimeProvider clock) : IProductionPostingPreviewReader
{
    public async Task<ProductionPostingPreview> ReadAsync(WorkAccessScope scope,Guid orderId,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));ct=deadline.Token;
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'",ct);
        await Establish(db,scope,ct);
        var result=await ReadInContextAsync(db,scope,orderId,ct);
        await tx.CommitAsync(ct);return result;
    }
    internal async Task<ProductionPostingPreview> ReadInContextAsync(AppDbContext db,WorkAccessScope scope,Guid orderId,CancellationToken ct)
    {
        var order=await db.ProductionOrders.AsNoTracking().SingleOrDefaultAsync(o=>o.TenantId==scope.TenantId&&o.Id==orderId,ct)??throw new InventoryPostingNotFoundException();
        if(!scope.CanLocation(order.LocationId)||!await db.Locations.AsNoTracking().AnyAsync(l=>l.TenantId==scope.TenantId&&l.Id==order.LocationId&&l.IsActive,ct)
            ||scope.Role!=AppRoles.EnterpriseAdmin&&!await db.UserLocations.AsNoTracking().AnyAsync(l=>l.TenantId==scope.TenantId&&l.UserId==scope.UserId&&l.LocationId==order.LocationId,ct))throw new WorkAccessDeniedException();
        var recipe=await db.Recipes.AsNoTracking().SingleOrDefaultAsync(r=>r.TenantId==scope.TenantId&&r.Id==order.RecipeId,ct)??throw new InventoryPostingConflictException("Production recipe requires reconciliation.");
        var ingredients=await db.RecipeIngredients.AsNoTracking().Where(i=>i.RecipeId==recipe.Id).OrderBy(i=>i.Id).Take(100).ToListAsync(ct);
        if(ingredients.Count>99)throw new InventoryPostingConflictException("Production review supports at most 99 ingredient lines.");
        var itemIds=ingredients.Select(i=>i.ItemId).Append(recipe.OutputItemId).Distinct().ToArray();
        var items=await db.Items.AsNoTracking().Where(i=>i.TenantId==scope.TenantId&&itemIds.Contains(i.Id)).ToDictionaryAsync(i=>i.Id,ct);
        var stock=await db.ProductStocks.AsNoTracking().Where(b=>b.TenantId==scope.TenantId&&b.StoreId==order.LocationId&&itemIds.Contains(b.ProductId)&&b.ProductId!=recipe.OutputItemId&&b.Quantity>0)
            .OrderBy(b=>b.Id).Take(1001).Select(b=>new{Stock=b,Version=EF.Property<uint>(b,"xmin")}).ToListAsync(ct);
        if(stock.Count>1000)throw new InventoryPostingConflictException("Production review candidate limit exceeded.");
        var batchIds=stock.Select(b=>b.Stock.Id).ToArray();
        var balances=await db.InventoryBalanceProjections.AsNoTracking().Where(b=>b.TenantId==scope.TenantId&&batchIds.Contains(b.BatchId)).ToDictionaryAsync(b=>b.BatchId,ct);
        var ledgerSums=await db.InventoryLedgerEntries.AsNoTracking().Where(e=>e.TenantId==scope.TenantId&&batchIds.Contains(e.BatchId))
            .GroupBy(e=>e.BatchId).Select(g=>new{BatchId=g.Key,Quantity=g.Sum(e=>e.QuantityDelta)}).ToDictionaryAsync(e=>e.BatchId,e=>e.Quantity,ct);
        var lastIds=balances.Values.Where(b=>b.LastLedgerEntryId.HasValue).Select(b=>b.LastLedgerEntryId!.Value).ToArray();
        var last=await db.InventoryLedgerEntries.AsNoTracking().Where(e=>e.TenantId==scope.TenantId&&lastIds.Contains(e.Id)).ToDictionaryAsync(e=>e.Id,ct);
        var lineIds=last.Values.Select(e=>e.DocumentLineId).ToArray();
        var units=await db.DocumentLines.AsNoTracking().Where(l=>l.TenantId==scope.TenantId&&lineIds.Contains(l.Id)).ToDictionaryAsync(l=>l.Id,l=>l.Unit,ct);
        var reserved=await db.InventoryReservations.AsNoTracking().Where(r=>r.TenantId==scope.TenantId&&r.LocationId==order.LocationId&&itemIds.Contains(r.ItemId)&&r.Status=="active")
            .Select(r=>r.ItemId).Distinct().ToArrayAsync(ct);
        var links=await db.ProductionOrderRequestLines.AsNoTracking().Where(l=>l.TenantId==scope.TenantId&&l.ProductionOrderId==order.Id).OrderBy(l=>l.Id).Take(1001).ToListAsync(ct);
        if(links.Count>1000)throw new InventoryPostingConflictException("Production request allocation limit exceeded.");
        var requestLineIds=links.Select(l=>l.RequestLineId).ToArray();
        var requestLines=await db.ProductionRequestLines.AsNoTracking().Where(l=>l.TenantId==scope.TenantId&&requestLineIds.Contains(l.Id)).ToDictionaryAsync(l=>l.Id,ct);
        var requestIds=requestLines.Values.Select(l=>l.RequestId).Distinct().ToArray();
        var requests=await db.ProductionRequests.AsNoTracking().Where(r=>r.TenantId==scope.TenantId&&requestIds.Contains(r.Id)).ToDictionaryAsync(r=>r.Id,ct);
        var allocated=await db.ProductionOrderRequestLines.AsNoTracking().Where(l=>l.TenantId==scope.TenantId&&requestLineIds.Contains(l.RequestLineId))
            .Join(db.ProductionOrders.Where(o=>o.TenantId==scope.TenantId&&o.Status!=ProductionOrderStatus.Cancelled),l=>l.ProductionOrderId,o=>o.Id,(l,o)=>l)
            .GroupBy(l=>l.RequestLineId).Select(g=>new{Id=g.Key,Quantity=g.Sum(l=>l.Qty)}).ToDictionaryAsync(g=>g.Id,g=>g.Quantity,ct);
        var reviewedRequests=links.Select(l=>{
            var line=requestLines.GetValueOrDefault(l.RequestLineId);var request=line==null?null:requests.GetValueOrDefault(line.RequestId);
            var inScope=line!=null&&request!=null&&line.ItemId==recipe.OutputItemId&&request.ProductionLocationId==order.LocationId;
            return new ProductionRequestReview(l.Id,l.RequestLineId,l.Qty,inScope?request!.Id:null,inScope?request!.Status:null,inScope,
                inScope?line!.RequestedQty:null,inScope?line!.AcceptedQty:null,inScope?allocated.GetValueOrDefault(l.RequestLineId):null,inScope?request!.UpdatedAt:null);
        }).ToArray();
        var batches=stock.Select(row=>{
            var b=row.Stock;var balance=balances.GetValueOrDefault(b.Id);var entry=balance?.LastLedgerEntryId is Guid id?last.GetValueOrDefault(id):null;
            if(entry!=null&&(entry.BatchId!=b.Id||entry.ItemId!=b.ProductId||entry.LocationId!=b.StoreId||entry.Currency!="UAH"))entry=null;
            var reconciled=balance!=null&&entry!=null&&balance.ItemId==b.ProductId&&balance.LocationId==b.StoreId&&balance.Quantity==b.Quantity&&balance.ReservedQuantity==0
                &&entry.QuantityAfter==b.Quantity&&entry.Currency=="UAH"
                &&entry.ZoneId==b.ZoneId&&entry.BatchNumberSnapshot==b.BatchNumber&&entry.ExpiryDateSnapshot==b.ExpiryDate
                &&ledgerSums.TryGetValue(b.Id,out var quantity)&&quantity==b.Quantity;
            return new ProductionBatchReview(b.Id,b.ProductId,b.StoreId,b.ZoneId,b.Quantity,b.ExpiryDate,b.AddedAt,b.Status,b.BatchNumber,row.Version,
                reconciled,balance?.LastLedgerEntryId,entry?.UnitCost,entry==null?null:units.GetValueOrDefault(entry.DocumentLineId));
        }).ToArray();
        var source=new ProductionReviewSource(new(order.Id,order.TenantId,order.LocationId,order.RecipeId,order.PlannedQty,order.Status.ToApiString(),order.UpdatedAt,order.StartedAt),
            new(recipe.Id,recipe.Name,recipe.OutputItemId,recipe.OutputQty,recipe.Unit,recipe.IsActive,recipe.UpdatedAt),
            Item(items.GetValueOrDefault(recipe.OutputItemId)),ingredients.Select(i=>new ProductionIngredientReview(i.Id,i.ItemId,i.Qty,i.Unit,Item(items.GetValueOrDefault(i.ItemId)))).ToArray(),
            batches,reserved,reviewedRequests,DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        var registry=await db.Database.SqlQuery<bool>($"SELECT to_regclass('public.inventory_posting_scopes') IS NOT NULL AS \"Value\"").SingleAsync(ct);
        var fenced=registry&&await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM public.inventory_posting_scopes WHERE \"TenantId\"={scope.TenantId} AND \"LocationId\"={order.LocationId}) AS \"Value\"").SingleAsync(ct);
        var result=ProductionPostingPlanner.Plan(source,configured.Enabled&&gate.Allows(scope.TenantId,order.LocationId),fenced);
        return result with{WriterImplemented=true,Ready=await Ready(db,scope.TenantId,order.LocationId,ct)};
    }
    private static ProductionItemReview? Item(Item? i)=>i==null?null:new(i.Id,i.Name,i.Unit,i.Barcodes.FirstOrDefault(),i.IsActive,i.ShelfLifeDays);
    internal static async Task<bool> Ready(AppDbContext db,Guid tenant,Guid location,CancellationToken ct)=>
        await db.Database.SqlQuery<bool>($"SELECT to_regprocedure('public.erp_w4_integrated_ready(uuid,uuid)') IS NOT NULL AS \"Value\"").SingleAsync(ct)
        &&await db.Database.SqlQuery<bool>($"SELECT public.erp_w4_integrated_ready({tenant},{location}) AS \"Value\"").SingleAsync(ct);
    internal static async Task<WorkAccessScope> Establish(AppDbContext db,WorkAccessScope scope,CancellationToken ct,string capability=TenantRoleCapabilities.ProductionComplete,bool requireProduction=true,bool enforceCapability=true)
    {
        if(scope.Role==AppRoles.Consumer||AppRoles.ProviderTeamRoles.Contains(scope.Role)||enforceCapability&&!scope.Has(capability))throw new WorkAccessDeniedException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{scope.TenantId.ToString()},true),set_config('app.user_id',{scope.UserId.ToString()},true),set_config('app.role','',true),set_config('app.consumer_account_id','',true)",ct);
        var user=await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.TenantId==scope.TenantId&&u.Id==scope.UserId&&u.IsActive&&u.Role==scope.Role,ct);
        var tenant=await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==scope.TenantId&&t.IsActive,ct);
        if(user==null||tenant==null||(requireProduction&&!tenant.HasModule("production"))||!tenant.HasModule("inventory"))throw new WorkAccessDeniedException();
        var permissions=user.Permissions==null?new Dictionary<string,bool>():new Dictionary<string,bool>(user.Permissions);
        var grants=await db.UserPermissionGrants.AsNoTracking().Where(g=>g.TenantId==scope.TenantId&&g.UserId==scope.UserId&&g.RevokedAt==null&&g.ExpiresAt>DateTime.UtcNow).Select(g=>g.PermissionKey).ToListAsync(ct);
        foreach(var grant in grants)permissions[grant]=true;
        var capabilities=new HashSet<string>();
        if(user.TenantRoleId is Guid roleId)
        {
            var role=await db.TenantRoles.AsNoTracking().SingleOrDefaultAsync(r=>r.TenantId==scope.TenantId&&r.Id==roleId&&r.IsActive,ct);
            if(role!=null)capabilities.UnionWith(role.Capabilities);
        }
        var live=scope with{Capabilities=capabilities,Permissions=permissions};
        if(enforceCapability&&!live.Has(capability))throw new WorkAccessDeniedException();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.role',{scope.Role},true)",ct);
        return live;
    }
}
