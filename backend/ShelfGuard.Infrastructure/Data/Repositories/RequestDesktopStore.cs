using System.Data;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Features.Production;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Application.Features.Transfers;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>Desktop source commands join the existing request synchronizer in one transaction.
/// They never issue inventory or invoke legacy shipment writers.</summary>
public sealed class RequestDesktopStore(DbContextOptions<AppDbContext> options,IWorkAccessContext access,ITransferService transfers,TimeProvider clock):IRequestDesktop
{
    private async Task<WorkAccessScope> Scope(AppDbContext db,string capability,bool production,CancellationToken ct)=>await ProductionPostingPreviewReader.Establish(db,await access.GetAsync(ct)??throw new WorkAccessDeniedException(),ct,capability,production);
    private static void Query(string? search,int page){if(page<1||page>100000||search?.Length>200)throw new ArgumentException("Invalid request query.");}
    private static async Task Location(AppDbContext db,WorkAccessScope s,Guid id,CancellationToken ct)
    {
        if(!s.CanLocation(id)||!await db.Locations.AnyAsync(l=>l.TenantId==s.TenantId&&l.Id==id&&l.IsActive,ct)||s.Role!="enterprise_admin"&&!await db.UserLocations.AnyAsync(l=>l.TenantId==s.TenantId&&l.UserId==s.UserId&&l.LocationId==id,ct))throw new WorkAccessDeniedException();
    }
    private ProductionRequestService Service(AppDbContext db)
    {var repo=new ProductionRequestRepository(db);var notifications=new NotificationRepository(db);return new(repo,new ProductionRepository(db,access),notifications,transfers,new ProductionOrderSyncService(repo,notifications));}
    private static IQueryable<ProductionRequest> Sources(AppDbContext db,Guid tenant)=>db.ProductionRequests.Where(r=>r.TenantId==tenant).Include(r=>r.Lines).ThenInclude(l=>l.Item).Include(r=>r.StoreLocation).Include(r=>r.ProductionLocation).AsSplitQuery();
    internal static async Task<string> Fingerprint(AppDbContext db,ProductionRequest r,CancellationToken ct)
    {
        var lineIds=r.Lines.Select(l=>l.Id).ToArray();var links=await db.ProductionOrderRequestLines.AsNoTracking().Where(l=>l.TenantId==r.TenantId&&lineIds.Contains(l.RequestLineId)).Include(l=>l.ProductionOrder).OrderBy(l=>l.Id).ToListAsync(ct);
        var items=r.Lines.Select(l=>l.ItemId).ToArray();var recipes=await db.Recipes.AsNoTracking().Where(x=>x.TenantId==r.TenantId&&items.Contains(x.OutputItemId)&&x.IsActive).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.OutputItemId,x.UpdatedAt,x.OutputQty,x.Unit}).ToListAsync(ct);
        // Include other requests' planned capacity: acceptance may aggregate into an existing order.
        var candidates=await db.ProductionOrders.AsNoTracking().Where(o=>o.TenantId==r.TenantId&&o.LocationId==r.ProductionLocationId&&o.NeededBy==r.NeededBy&&o.Status==ProductionOrderStatus.Planned&&items.Contains(o.Recipe!.OutputItemId)).OrderBy(o=>o.Id).Select(o=>new{o.Id,o.PlannedQty,o.UpdatedAt}).ToListAsync(ct);
        return ReceiptPostingRules.Hash(new{r.Id,r.StoreLocationId,r.ProductionLocationId,r.NeededBy,r.Status,r.Source,r.Notes,r.ReviewNote,r.UpdatedAt,lines=r.Lines.OrderBy(l=>l.Id).Select(l=>new{l.Id,l.ItemId,l.RequestedQty,l.AcceptedQty,l.Reason,active=l.Item?.IsActive}),links=links.Select(l=>new{l.Id,l.RequestLineId,l.ProductionOrderId,l.Qty,status=l.ProductionOrder?.Status,updated=l.ProductionOrder?.UpdatedAt}),recipes,candidates});
    }
    private static async Task<bool> MutablePlan(AppDbContext db,ProductionRequest r,CancellationToken ct)=>!await db.ProductionOrderRequestLines.AnyAsync(l=>l.TenantId==r.TenantId&&l.RequestLine!.RequestId==r.Id&&l.ProductionOrder!.Status!=ProductionOrderStatus.Planned&&l.ProductionOrder.Status!=ProductionOrderStatus.Cancelled,ct);
    private static async Task Bound(AppDbContext db,WorkAccessScope s,ProductionRequest r,Guid location,string side,CancellationToken ct)
    {if((side=="store"?r.StoreLocationId:r.ProductionLocationId)!=location)throw new WorkAccessDeniedException();await Location(db,s,location,ct);}
    public async Task<RequestDesktopAccess> AccessAsync(CancellationToken ct)
    {
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await Scope(db,TenantRoleCapabilities.ProductionView,false,ct);var tenant=await db.Tenants.SingleAsync(t=>t.Id==s.TenantId,ct);var production=tenant.HasModule("production");await tx.CommitAsync(ct);return new(s.Has(TenantRoleCapabilities.ProductionManage),production&&s.Has(TenantRoleCapabilities.ProductionManage),s.Has(TenantRoleCapabilities.ProductionCancel),production,production&&s.Has(TenantRoleCapabilities.ProductionManage)&&ReceiptPostingRules.CanReceive(s.Role)&&s.Permissions.GetValueOrDefault("transfers",true),ReceiptPostingRules.CanReceive(s.Role)&&s.Permissions.GetValueOrDefault("transfers",true),production&&s.Has(TenantRoleCapabilities.ProductionCancel)&&InventoryAdjustmentPlanner.CanAdjust(s.Role)&&s.Permissions.GetValueOrDefault("transfers",true));
    }
    public async Task<ProductionDesktopPage<ProductionRequestListItemDto>> ListAsync(RequestDesktopQuery q,CancellationToken ct)
    {
        Query(q.Search,q.Page);RequestDesktopRules.Side(q.Side);if(!string.IsNullOrEmpty(q.Status)&&!ProductionRequestStatus.All.Contains(q.Status))throw new ArgumentException("Invalid status.");
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'",ct);var s=await Scope(db,TenantRoleCapabilities.ProductionView,q.Side=="production",ct);await Location(db,s,q.LocationId,ct);
        var query=Sources(db,s.TenantId).AsNoTracking().Where(r=>q.Side=="store"?r.StoreLocationId==q.LocationId:r.ProductionLocationId==q.LocationId);if(!string.IsNullOrEmpty(q.Status))query=query.Where(r=>r.Status==q.Status);if(!string.IsNullOrWhiteSpace(q.Search))query=query.Where(r=>EF.Functions.ILike(r.StoreLocation!.Name,"%"+q.Search.Trim()+"%")||EF.Functions.ILike(r.ProductionLocation!.Name,"%"+q.Search.Trim()+"%"));
        var count=await query.CountAsync(ct);var rows=await query.OrderBy(r=>r.NeededBy).ThenByDescending(r=>r.CreatedAt).ThenBy(r=>r.Id).Skip((q.Page-1)*50).Take(50).ToListAsync(ct);var ids=rows.Select(r=>r.Id).ToArray();var links=await new ProductionRequestRepository(db).GetLinksForRequestsAsync(ids,ct);
        var result=rows.Select(r=>new ProductionRequestListItemDto(r.Id,r.StoreLocationId,r.StoreLocation?.Name??"",r.ProductionLocationId,r.ProductionLocation?.Name??"",r.NeededBy,r.Status,r.Source,r.Lines.Count,r.Lines.Sum(l=>l.RequestedQty),r.Lines.Any(l=>l.AcceptedQty!=null)?r.Lines.Sum(l=>l.AcceptedQty??0):null,r.CreatedAt,r.SubmittedAt,ProductionRequestStages.Derive(r.Status,r.Lines.ToList(),links.Where(l=>l.RequestLine?.RequestId==r.Id).ToList()))).ToList();await tx.CommitAsync(ct);return new(result,count,q.Page,50,(count+49)/50);
    }
    public async Task<RequestDesktopSource> GetAsync(Guid id,Guid locationId,string side,CancellationToken ct)
    {
        RequestDesktopRules.Side(side);await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'",ct);var s=await Scope(db,TenantRoleCapabilities.ProductionView,side=="production",ct);var r=await Sources(db,s.TenantId).AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id,ct)??throw new InventoryPostingNotFoundException();await Bound(db,s,r,locationId,side,ct);
        var detail=await Service(db).GetByIdAsync(id,ct)??throw new InventoryPostingNotFoundException();var fingerprint=await Fingerprint(db,r,ct);var mutable=await MutablePlan(db,r,ct);await tx.CommitAsync(ct);return new(detail,fingerprint,side=="store"&&s.Has(TenantRoleCapabilities.ProductionManage)&&r.Status=="draft",side=="store"&&s.Has(TenantRoleCapabilities.ProductionCancel)&&(r.Status is "draft" or "submitted"),side=="production"&&s.Has(TenantRoleCapabilities.ProductionManage)&&mutable&&(r.Status is "submitted" or "accepted" or "partial"));
    }
    public async Task<ProductionDesktopPage<RequestDesktopItem>> ItemsAsync(string? search,int page,CancellationToken ct)
    {
        Query(search,page);await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);var s=await Scope(db,TenantRoleCapabilities.ProductionView,false,ct);var q=db.Items.Where(i=>i.TenantId==s.TenantId&&i.IsActive&&db.Recipes.Any(r=>r.TenantId==s.TenantId&&r.IsActive&&r.OutputItemId==i.Id));if(!string.IsNullOrWhiteSpace(search))q=q.Where(i=>EF.Functions.ILike(i.Name,"%"+search.Trim()+"%"));var count=await q.CountAsync(ct);var rows=await q.OrderBy(i=>i.Name).ThenBy(i=>i.Id).Skip((page-1)*50).Take(50).Select(i=>new RequestDesktopItem(i.Id,i.Name,i.Unit)).ToListAsync(ct);await tx.CommitAsync(ct);return new(rows,count,page,50,(count+49)/50);
    }
    public async Task<IReadOnlyList<RequestDesktopDestination>> DestinationsAsync(CancellationToken ct)
    {
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await Scope(db,TenantRoleCapabilities.ProductionView,false,ct);var rows=await db.Locations.Where(l=>l.TenantId==s.TenantId&&l.IsActive&&l.LocationType=="production").OrderBy(l=>l.Name).ThenBy(l=>l.Id).Take(501).Select(l=>new RequestDesktopDestination(l.Id,l.Name)).ToListAsync(ct);if(rows.Count>500)throw new InventoryPostingConflictException("Too many production destinations.");await tx.CommitAsync(ct);return rows;
    }
    private static async Task Locks(AppDbContext db,CancellationToken ct)=>await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'; LOCK TABLE public.production_orders IN SHARE ROW EXCLUSIVE MODE; LOCK TABLE public.recipes,public.recipe_ingredients,public.items IN SHARE MODE; LOCK TABLE public.production_order_request_lines,public.production_request_lines,public.production_requests IN SHARE ROW EXCLUSIVE MODE",ct);
    private static string System(string phase)=>"requests.desktop."+phase+".v1";
    private static async Task<BusinessDocument?> Prior(AppDbContext db,WorkAccessScope s,Guid operation,string phase,string hash,CancellationToken ct)
    {
        var p=await db.BusinessDocuments.SingleOrDefaultAsync(d=>d.TenantId==s.TenantId&&d.DocumentKey==operation,ct);if(p!=null&&(p.SourceSystem!=System(phase)||p.ContentHash!=hash||p.CreatedBy!=s.UserId||p.Status!="approved"))throw new InventoryPostingConflictException("Different request command intent.");return p;
    }
    private async Task<BusinessDocument> Audit(AppDbContext db,WorkAccessScope s,Guid id,Guid operation,string phase,string hash,string reason,CancellationToken ct)
    {
        var at=clock.GetUtcNow().UtcDateTime;var doc=new BusinessDocument{TenantId=s.TenantId,DocumentKey=operation,SourceSystem=System(phase),SourceId=operation.ToString(),Number=id.ToString(),DocumentType="production_request_decision",CreatedBy=s.UserId,ContentHash=hash,CreatedAt=at,UpdatedAt=at,OccurredAt=at};db.Add(doc);await db.SaveChangesAsync(ct);doc.Status="submitted";await db.SaveChangesAsync(ct);db.Add(new DocumentApproval{TenantId=s.TenantId,DocumentId=doc.Id,Stage=1,ApproverId=s.UserId,Decision="approved",ContentHash=hash,Reason=reason,CreatedAt=at});doc.Status="approved";await db.SaveChangesAsync(ct);return doc;
    }
    public async Task<RequestDesktopResult> CreateAsync(RequestDesktopCreate c,CancellationToken ct)
    {
        c=RequestDesktopRules.Normalize(c);await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await Scope(db,TenantRoleCapabilities.ProductionManage,false,ct);await Location(db,s,c.StoreLocationId,ct);await Locks(db,ct);var hash=ReceiptPostingRules.Hash(new{c,actor=s.UserId});var prior=await Prior(db,s,c.OperationId,"create",hash,ct);if(prior!=null){await tx.CommitAsync(ct);return new(c.OperationId,prior.Id,"create",true);}
        var today=DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);if(c.NeededBy<today)throw new ArgumentException("Past request date.");
        if(!await db.Locations.AnyAsync(l=>l.TenantId==s.TenantId&&l.Id==c.StoreLocationId&&l.LocationType!="production",ct)||!await db.Locations.AnyAsync(l=>l.TenantId==s.TenantId&&l.Id==c.ProductionLocationId&&l.IsActive&&l.LocationType=="production",ct))throw new ArgumentException("Invalid source or destination.");
        var itemIds=c.Lines.Select(l=>l.ItemId).ToArray();if(await db.Items.CountAsync(i=>i.TenantId==s.TenantId&&itemIds.Contains(i.Id)&&i.IsActive&&db.Recipes.Any(r=>r.TenantId==s.TenantId&&r.IsActive&&r.OutputItemId==i.Id),ct)!=itemIds.Length)throw new ArgumentException("Active recipe output required.");
        if(await db.ProductionRequests.AnyAsync(r=>r.TenantId==s.TenantId&&r.Id==c.OperationId,ct))throw new InventoryPostingConflictException("Source identity already exists.");
        db.Add(new ProductionRequest{Id=c.OperationId,TenantId=s.TenantId,StoreLocationId=c.StoreLocationId,ProductionLocationId=c.ProductionLocationId,NeededBy=c.NeededBy,Notes=c.Notes,CreatedBy=s.UserId,CreatedAt=clock.GetUtcNow().UtcDateTime,UpdatedAt=clock.GetUtcNow().UtcDateTime});db.AddRange(c.Lines.Select(l=>new ProductionRequestLine{TenantId=s.TenantId,RequestId=c.OperationId,ItemId=l.ItemId,RequestedQty=l.RequestedQty}));await db.SaveChangesAsync(ct);var doc=await Audit(db,s,c.OperationId,c.OperationId,"create",hash,"Створення чернетки заявки магазину",ct);await tx.CommitAsync(ct);return new(c.OperationId,doc.Id,"create",false);
    }
    public async Task<RequestDesktopResult> DecideAsync(Guid id,Guid locationId,string side,string phase,RequestDesktopDecision c,CancellationToken ct)
    {
        RequestDesktopRules.Side(side);RequestDesktopRules.Phase(phase);if(c.OperationId==Guid.Empty||id==Guid.Empty)throw new ArgumentException("Command identity required.");TransferPostingRules.Fingerprint(c.ExpectedFingerprint);c=c with{Reason=WriteOffPostingRules.Reason("reverse",c.Reason)!,Lines=c.Lines?.OrderBy(l=>l.LineId).Select(l=>l with{Reason=RequestDesktopRules.Notes(l.Reason,500)}).ToList()};
        if(side!=(phase is "review" or "reject"?"production":"store")||phase!="review"&&c.Lines!=null)throw new ArgumentException("Invalid command side.");
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await Scope(db,phase=="cancel"?TenantRoleCapabilities.ProductionCancel:TenantRoleCapabilities.ProductionManage,side=="production",ct);await Location(db,s,locationId,ct);await Locks(db,ct);var r=await Sources(db,s.TenantId).SingleOrDefaultAsync(r=>r.Id==id,ct)??throw new InventoryPostingNotFoundException();await Bound(db,s,r,locationId,side,ct);var hash=ReceiptPostingRules.Hash(new{id,locationId,side,phase,c,actor=s.UserId});var prior=await Prior(db,s,c.OperationId,phase,hash,ct);if(prior!=null){await tx.CommitAsync(ct);return new(id,prior.Id,phase,true);}
        if(await Fingerprint(db,r,ct)!=c.ExpectedFingerprint)throw new InventoryPostingConflictException("Request or production plan changed.");
        if(phase=="review")
        {
            if(!await MutablePlan(db,r,ct))throw new InventoryPostingConflictException("Linked production already started.");
            if(c.Lines==null||c.Lines.Count!=r.Lines.Count||c.Lines.Select(l=>l.LineId).Distinct().Count()!=c.Lines.Count||c.Lines.Any(l=>!r.Lines.Any(x=>x.Id==l.LineId)))throw new ArgumentException("Every line must be reviewed once.");
            foreach(var l in c.Lines){RequestDesktopRules.Quantity(l.AcceptedQty,true);var source=r.Lines.Single(x=>x.Id==l.LineId);if(l.AcceptedQty>source.RequestedQty||l.AcceptedQty<source.RequestedQty&&string.IsNullOrWhiteSpace(l.Reason))throw new ArgumentException("Reduction reason required.");if(l.AcceptedQty>0&&(!source.Item!.IsActive||!await db.Recipes.AnyAsync(x=>x.TenantId==s.TenantId&&x.OutputItemId==source.ItemId&&x.IsActive,ct)))throw new InventoryPostingConflictException("Active output recipe required.");}
            var repo=new ProductionRequestRepository(db);var links=await repo.GetLinksForRequestsAsync([id],ct);
            foreach(var line in r.Lines)
            {
                var accepted=c.Lines.Single(l=>l.LineId==line.Id).AcceptedQty;
                var linked=links.Where(l=>l.RequestLineId==line.Id&&l.ProductionOrder?.Status!=ProductionOrderStatus.Cancelled).Sum(l=>l.Qty);
                if(accepted<=linked)continue;
                var target=await repo.FindPlannedOrderAsync(r.ProductionLocationId,line.ItemId,r.NeededBy,ct);
                if((target?.PlannedQty??0)+accepted-linked>9_999_999.99m)throw new InventoryPostingConflictException("Aggregated production exceeds output bounds.");
            }
        }
        var svc=Service(db);var outcome=phase switch{"submit"=>await svc.SubmitAsync(id,ct),"cancel"=>await svc.CancelAsync(id,ct),"reject"=>await svc.RejectAsync(id,c.Reason,s.UserId,ct),_=>await svc.ReviewAsync(id,new(c.Lines!,c.Reason),s.UserId,ct)};
        if(outcome.Error!=null)throw new InventoryPostingConflictException(outcome.Error);
        // The shared planner aggregates requests. Reject unsupported totals before committing any source changes.
        if(await db.ProductionOrders.AnyAsync(o=>o.TenantId==s.TenantId&&o.LocationId==r.ProductionLocationId&&o.Status==ProductionOrderStatus.Planned&&(o.PlannedQty>9_999_999.99m||decimal.Round(o.PlannedQty,2)!=o.PlannedQty),ct))throw new InventoryPostingConflictException("Planned output exceeds Desktop quantity bounds.");
        var doc=await Audit(db,s,id,c.OperationId,phase,hash,c.Reason,ct);await tx.CommitAsync(ct);return new(id,doc.Id,phase,false);
    }
}
