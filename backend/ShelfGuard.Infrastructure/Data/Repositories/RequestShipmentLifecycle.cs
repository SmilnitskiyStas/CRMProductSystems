using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
namespace ShelfGuard.Infrastructure.Data.Repositories;
internal static class RequestShipmentLifecycle
{
    internal static async Task ApplyAsync(AppDbContext db,WorkAccessScope scope,StockTransfer transfer,string phase,CancellationToken ct)
    {
        var linked=await db.ProductionRequests.AnyAsync(r=>r.TenantId==scope.TenantId&&r.ShipmentTransferId==transfer.Id,ct);if(!linked)return;
        if(phase=="cancel")await ProductionPostingPreviewReader.Establish(db,scope,ct,TenantRoleCapabilities.ProductionCancel);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.production_request_lines,public.production_requests IN SHARE ROW EXCLUSIVE MODE",ct);
        var requests=await db.ProductionRequests.Where(r=>r.TenantId==scope.TenantId&&r.ShipmentTransferId==transfer.Id).Include(r=>r.Lines).ToListAsync(ct);
        if(requests.Count!=1)throw new InventoryPostingConflictException("Ambiguous request shipment.");var r=requests[0];
        if(r.Status!="fulfilled"||r.ProductionLocationId!=transfer.FromStoreId||r.StoreLocationId!=transfer.ToStoreId)throw new InventoryPostingConflictException("Request shipment linkage changed.");
        if(phase=="cancel")
        {r.Status=r.Lines.All(l=>l.AcceptedQty==l.RequestedQty)?"accepted":"partial";r.ShipmentTransferId=null;r.ShippedAt=null;}
        r.UpdatedAt=DateTime.UtcNow;
    }
}
