using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Application.Features.Stock;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data.Repositories;
public sealed class RequestShipmentStore(DbContextOptions<AppDbContext> options,IWorkAccessContext access,TransferPostingStore transfers,TimeProvider clock):IRequestShipment
{
    internal const string Source="requests.shipment.v1";
    private async Task<WorkAccessScope> Scope(AppDbContext db,CancellationToken ct)=>await ProductionPostingPreviewReader.Establish(db,await access.GetAsync(ct)??throw new WorkAccessDeniedException(),ct,TenantRoleCapabilities.ProductionManage);
    private static async Task<ProductionRequest> Request(AppDbContext db,WorkAccessScope s,Guid id,Guid location,CancellationToken ct)
    {
        var r=await db.ProductionRequests.Where(r=>r.TenantId==s.TenantId&&r.Id==id).Include(r=>r.Lines).ThenInclude(l=>l.Item).SingleOrDefaultAsync(ct)??throw new InventoryPostingNotFoundException();
        if(r.ProductionLocationId!=location||!s.CanLocation(location)||!await db.Locations.AnyAsync(l=>l.TenantId==s.TenantId&&l.Id==location&&l.IsActive,ct)||s.Role!="enterprise_admin"&&!await db.UserLocations.AnyAsync(l=>l.TenantId==s.TenantId&&l.UserId==s.UserId&&l.LocationId==location,ct))throw new WorkAccessDeniedException();return r;
    }
    private async Task<(RequestShipmentPreview Preview,TransferDispatchCommand? Command)> Plan(AppDbContext db,WorkAccessScope s,ProductionRequest r,Guid operation,CancellationToken ct)
    {
        var issues=new List<string>();var accepted=r.Lines.Where(l=>l.AcceptedQty is >0).ToList();
        if(r.Status is not ("accepted" or "partial")||r.ShipmentTransferId!=null)issues.Add("request_not_open");
        if(accepted.Count is <1 or >50)issues.Add("accepted_lines_required");
        if(accepted.Any(l=>l.Item?.IsActive!=true||!TransferPostingRules.Exact(l.AcceptedQty!.Value)||l.AcceptedQty>l.RequestedQty))issues.Add("invalid_accepted_quantity");
        var links=await new ProductionRequestRepository(db).GetLinksForRequestsAsync([r.Id],ct);
        var orderIds=links.Select(l=>l.ProductionOrderId).Distinct().ToArray();var orders=await db.ProductionOrders.AsNoTracking().Where(o=>o.TenantId==s.TenantId&&orderIds.Contains(o.Id)).Include(o=>o.Recipe).ToDictionaryAsync(o=>o.Id,ct);
        if(accepted.Any(l=>links.Where(k=>k.RequestLineId==l.Id&&orders.TryGetValue(k.ProductionOrderId,out var o)&&o.Status==ProductionOrderStatus.Done&&o.LocationId==r.ProductionLocationId&&o.Recipe?.OutputItemId==l.ItemId).Sum(k=>k.Qty)!=l.AcceptedQty)||links.Any(k=>k.ProductionOrder?.Status is not (ProductionOrderStatus.Done or ProductionOrderStatus.Cancelled)))issues.Add("production_not_ready");
        var itemIds=accepted.Select(l=>l.ItemId).Distinct().ToArray();var today=DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var candidates=await db.ProductStocks.Where(b=>b.TenantId==s.TenantId&&b.StoreId==r.ProductionLocationId&&itemIds.Contains(b.ProductId)&&b.Quantity>0).OrderBy(b=>b.ExpiryDate).ThenBy(b=>b.AddedAt).ThenBy(b=>b.Id).Take(1001).ToListAsync(ct);
        if(candidates.Count>1000)issues.Add("candidate_limit");
        var takes=new List<TransferDispatchItem>();
        foreach(var group in accepted.GroupBy(l=>l.ItemId))
        {
            var remaining=group.Sum(l=>l.AcceptedQty!.Value);
            foreach(var b in candidates.Where(b=>b.ProductId==group.Key&&StockStatus.IsUsable(b,today)))
            {if(remaining<=0)break;var take=Math.Min(remaining,b.Quantity);if(!TransferPostingRules.Exact(take)){issues.Add("quantity_not_representable");break;}takes.Add(new(b.Id,take));remaining-=take;}
            if(remaining>0)issues.Add("insufficient_usable_finished_stock");
        }
        if(takes.Count>100)issues.Add("shipment_batch_limit");
        if(await db.InventoryReservations.AnyAsync(x=>x.TenantId==s.TenantId&&itemIds.Contains(x.ItemId)&&(x.LocationId==r.ProductionLocationId||x.LocationId==r.StoreLocationId)&&x.Status=="active",ct))issues.Add("active_reservation");
        TransferDispatchCommand? command=null;TransferPostingPreview? transfer=null;
        if(issues.Count==0)
        {
            command=TransferPostingRules.Normalize(new(operation,r.ProductionLocationId,r.StoreLocationId,"production_to_store","Заявка на виробництво #"+r.Id.ToString("N")[..8].ToUpperInvariant(),takes));
            transfer=await transfers.PreviewDispatchInContextAsync(db,s,command,ct);command=command with{ExpectedFingerprint=transfer.Fingerprint};
            if(!transfer.Configured)issues.Add("transfer_scope_disabled");if(!transfer.Ready)issues.Add("transfer_scope_not_ready");
        }
        var fingerprint=ReceiptPostingRules.Hash(new{protocol=Source,r.Id,operation,actor=s.UserId,source=await RequestDesktopStore.Fingerprint(db,r,ct),transfer=transfer?.Fingerprint,today,issues});
        return(new(r.Id,operation,r.ProductionLocationId,r.StoreLocationId,fingerprint,issues.Count==0&&transfer?.CanPost==true,issues.Distinct().ToArray(),transfer),command);
    }
    public async Task<RequestShipmentPreview> PreviewAsync(Guid requestId,Guid operationId,Guid locationId,CancellationToken ct)
    {
        if(operationId==Guid.Empty)throw new ArgumentException("Shipment identity required.");await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='20s'",ct);var s=await Scope(db,ct);var r=await Request(db,s,requestId,locationId,ct);var plan=await Plan(db,s,r,operationId,ct);await tx.CommitAsync(ct);return plan.Preview;
    }
    public async Task<RequestShipmentResult> ShipAsync(Guid requestId,Guid locationId,RequestShipmentCommand c,CancellationToken ct)
    {
        if(requestId==Guid.Empty||c.OperationId==Guid.Empty)throw new ArgumentException("Shipment identity required.");TransferPostingRules.Fingerprint(c.ExpectedFingerprint);c=c with{Reason=WriteOffPostingRules.Reason("reverse",c.Reason)!};
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromMinutes(1));ct=deadline.Token;
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await Scope(db,ct);await Request(db,s,requestId,locationId,ct);
        var lockKey=s.TenantId.ToString()+':'+c.OperationId.ToString();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey},0))",ct);
        // Same stock -> reservations -> orders -> request order as production completion. The
        // transfer ID lock precedes stock locks, including retries racing terminal transfer actions.
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'; LOCK TABLE public.product_stock IN EXCLUSIVE MODE; LOCK TABLE public.inventory_reservations IN SHARE MODE; LOCK TABLE public.production_orders IN SHARE ROW EXCLUSIVE MODE; LOCK TABLE public.recipes,public.recipe_ingredients,public.items IN SHARE MODE; LOCK TABLE public.production_order_request_lines,public.production_request_lines,public.production_requests IN SHARE ROW EXCLUSIVE MODE",ct);
        db.ChangeTracker.Clear();var r=await Request(db,s,requestId,locationId,ct);var hash=ReceiptPostingRules.Hash(new{requestId,locationId,c,actor=s.UserId});var prior=await db.BusinessDocuments.SingleOrDefaultAsync(d=>d.TenantId==s.TenantId&&d.DocumentKey==c.OperationId,ct);
        if(prior!=null)
        {
            if(prior.SourceSystem!=Source||prior.Number!=requestId.ToString()||prior.ContentHash!=hash||prior.CreatedBy!=s.UserId||prior.Status!="approved")throw new InventoryPostingConflictException("Different shipment confirmation.");
            var metadata=await db.DocumentLines.Where(l=>l.TenantId==s.TenantId&&l.DocumentId==prior.Id&&l.LineNumber==1).Select(l=>l.Metadata).SingleAsync(ct);using var saved=JsonDocument.Parse(metadata!);var original=JsonSerializer.Deserialize<TransferDispatchCommand>(saved.RootElement.GetProperty("command").GetRawText())??throw new InventoryPostingConflictException("Shipment audit is incomplete.");
            var replay=await transfers.PostInContextAsync(db,s,original,c.OperationId,"dispatch",new(original.ExpectedFingerprint!),ct);await tx.CommitAsync(ct);return new(requestId,replay.TransferId,replay.DocumentId,replay.PostingId,replay.EventId,true);
        }
        var plan=await Plan(db,s,r,c.OperationId,ct);if(!plan.Preview.CanShip||plan.Preview.Fingerprint!=c.ExpectedFingerprint||plan.Command==null)throw new InventoryPostingConflictException("Request, production or reviewed FEFO changed.");
        var posted=await transfers.PostInContextAsync(db,s,plan.Command,c.OperationId,"dispatch",new(plan.Command.ExpectedFingerprint!),ct);
        var at=clock.GetUtcNow().UtcDateTime;var doc=new BusinessDocument{TenantId=s.TenantId,DocumentKey=c.OperationId,SourceSystem=Source,SourceId=c.OperationId.ToString(),Number=requestId.ToString(),DocumentType="production_request_shipment",CreatedBy=s.UserId,ContentHash=hash,CreatedAt=at,UpdatedAt=at,OccurredAt=at};db.Add(doc);await db.SaveChangesAsync(ct);
        var number=0;foreach(var l in r.Lines.Where(l=>l.AcceptedQty is >0).OrderBy(l=>l.Id))db.Add(new DocumentLine{TenantId=s.TenantId,DocumentId=doc.Id,LineNumber=++number,ItemId=l.ItemId,FromLocationId=r.ProductionLocationId,ToLocationId=r.StoreLocationId,Quantity=l.AcceptedQty!.Value,Unit=l.Item!.Unit,ItemNameSnapshot=l.Item.Name,Metadata=JsonSerializer.Serialize(new{requestId,lineId=l.Id,l.RequestedQty,l.AcceptedQty,l.Reason,command=number==1?plan.Command:null})});await db.SaveChangesAsync(ct);
        doc.Status="submitted";await db.SaveChangesAsync(ct);db.Add(new DocumentApproval{TenantId=s.TenantId,DocumentId=doc.Id,Stage=1,ApproverId=s.UserId,Decision="approved",ContentHash=hash,Reason=c.Reason,CreatedAt=at});doc.Status="approved";db.Add(new DocumentLink{TenantId=s.TenantId,FromDocumentId=doc.Id,ToDocumentId=posted.DocumentId,RelationType="fulfills_request",CreatedAt=at});r.Status="fulfilled";r.ShipmentTransferId=posted.TransferId;r.ShippedAt=at;r.UpdatedAt=at;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(requestId,posted.TransferId,posted.DocumentId,posted.PostingId,posted.EventId,false);
    }
}
