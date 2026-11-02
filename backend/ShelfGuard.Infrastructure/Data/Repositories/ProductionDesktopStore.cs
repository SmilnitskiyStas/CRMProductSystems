using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data.Repositories;
public sealed class ProductionDesktopStore(DbContextOptions<AppDbContext> options,IWorkAccessContext access,IInventoryPostingGate gate,ProductionPostingOptions enabled,TimeProvider clock):IProductionDesktop
{
    private async Task<WorkAccessScope> Scope(CancellationToken ct)=>await access.GetAsync(ct)??throw new WorkAccessDeniedException();
    private static async Task Location(AppDbContext db,WorkAccessScope s,Guid id,CancellationToken ct)
    {
        if(!s.CanLocation(id)||!await db.Locations.AnyAsync(l=>l.TenantId==s.TenantId&&l.Id==id&&l.IsActive,ct)
            ||s.Role!="enterprise_admin"&&!await db.UserLocations.AnyAsync(l=>l.TenantId==s.TenantId&&l.UserId==s.UserId&&l.LocationId==id,ct))throw new WorkAccessDeniedException();
    }
    private async Task Ready(AppDbContext db,WorkAccessScope s,Guid id,CancellationToken ct)
    {if(!enabled.Enabled||!gate.Allows(s.TenantId,id))throw new InventoryPostingDisabledException();if(!await ProductionPostingPreviewReader.Ready(db,s.TenantId,id,ct))throw new InventoryPostingConflictException("Production scope not ready.");}
    private static async Task HeaderLock(AppDbContext db,CancellationToken ct)=>await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'; LOCK TABLE public.production_orders IN SHARE ROW EXCLUSIVE MODE",ct);
    private static void Query(string? search,int page){if(page<1||page>100000||search?.Length>200)throw new ArgumentException("Invalid production query.");}
    private static ProductionDesktopOrder Row(ProductionOrder o)=>new(o.Id,o.LocationId,o.RecipeId,o.Recipe?.Name??"",o.Location?.Name??"",o.PlannedQty,o.Status.ToApiString(),o.Notes,o.NeededBy,o.PlannedDate,o.CreatedAt,o.StartedAt,o.CompletedAt,o.RequestLines.Count,ProductionDesktopRules.Fingerprint(o,o.RequestLines));
    private static IQueryable<ProductionOrder> Orders(AppDbContext db,WorkAccessScope s)=>db.ProductionOrders.Where(o=>o.TenantId==s.TenantId).Include(o=>o.Recipe).Include(o=>o.Location).Include(o=>o.RequestLines).AsSplitQuery();
    public async Task<ProductionDesktopAccess> AccessAsync(CancellationToken ct)
    {
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await ProductionPostingPreviewReader.Establish(db,await Scope(ct),ct,TenantRoleCapabilities.ProductionView);
        var result=new ProductionDesktopAccess(ProductionDesktopRules.Protocol,s.Has(TenantRoleCapabilities.ProductionManage),s.Has(TenantRoleCapabilities.ProductionComplete),s.Has(TenantRoleCapabilities.ProductionCancel),s.Has(TenantRoleCapabilities.ProductionComplete));await tx.CommitAsync(ct);return result;
    }
    public async Task<ProductionDesktopOrder> OrderAsync(Guid id,Guid locationId,CancellationToken ct)
    {
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY",ct);var s=await ProductionPostingPreviewReader.Establish(db,await Scope(ct),ct,TenantRoleCapabilities.ProductionView);await Location(db,s,locationId,ct);var o=await Orders(db,s).AsNoTracking().SingleOrDefaultAsync(o=>o.Id==id&&o.LocationId==locationId,ct)??throw new InventoryPostingNotFoundException();await tx.CommitAsync(ct);return Row(o);
    }
    public async Task<ProductionDesktopPage<ProductionDesktopOrder>> OrdersAsync(Guid locationId,string? status,string? search,int page,CancellationToken ct)
    {
        Query(search,page);ProductionOrderStatus parsed=default;if(!string.IsNullOrEmpty(status)&&!ProductionOrderStatusExtensions.TryParseApi(status,out parsed))throw new ArgumentException("Invalid status.");
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY",ct);
        var s=await ProductionPostingPreviewReader.Establish(db,await Scope(ct),ct,TenantRoleCapabilities.ProductionView);await Location(db,s,locationId,ct);
        var q=Orders(db,s).AsNoTracking().Where(o=>o.LocationId==locationId);if(!string.IsNullOrEmpty(status))q=q.Where(o=>o.Status==parsed);if(!string.IsNullOrWhiteSpace(search))q=q.Where(o=>EF.Functions.ILike(o.Recipe!.Name,"%"+search.Trim()+"%"));
        var count=await q.CountAsync(ct);var rows=await q.OrderByDescending(o=>o.CreatedAt).ThenBy(o=>o.Id).Skip((page-1)*50).Take(50).ToListAsync(ct);await tx.CommitAsync(ct);return new(rows.Select(Row).ToArray(),count,page,50,(count+49)/50);
    }
    public async Task<ProductionDesktopPage<ProductionDesktopRecipe>> RecipesAsync(string? search,int page,CancellationToken ct)
    {
        Query(search,page);await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY",ct);var s=await ProductionPostingPreviewReader.Establish(db,await Scope(ct),ct,TenantRoleCapabilities.ProductionView);
        var q=db.Recipes.Where(r=>r.TenantId==s.TenantId&&r.IsActive);if(!string.IsNullOrWhiteSpace(search))q=q.Where(r=>EF.Functions.ILike(r.Name,"%"+search.Trim()+"%"));var count=await q.CountAsync(ct);
        var rows=await q.OrderBy(r=>r.Name).ThenBy(r=>r.Id).Skip((page-1)*50).Take(50).Select(r=>new ProductionDesktopRecipe(r.Id,r.Name,r.OutputItem!.Name,r.OutputQty,r.Unit,r.Ingredients.Count)).ToListAsync(ct);await tx.CommitAsync(ct);return new(rows,count,page,50,(count+49)/50);
    }
    public async Task<ProductionDesktopResult> CreateAsync(ProductionDesktopCreate command,CancellationToken ct)
    {
        command=ProductionDesktopRules.Normalize(command);await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var s=await ProductionPostingPreviewReader.Establish(db,await Scope(ct),ct,TenantRoleCapabilities.ProductionManage);await Location(db,s,command.LocationId,ct);await Ready(db,s,command.LocationId,ct);
        if(!await db.Locations.AnyAsync(l=>l.Id==command.LocationId&&l.TenantId==s.TenantId&&l.LocationType=="production",ct))throw new ArgumentException("Production location required.");
        var key=$"production.desktop.create:{s.TenantId:N}:{command.RequestId:N}";await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))",ct);
        // Serialize active-recipe validation with Desktop catalog archive/edit before inserting the header.
        await HeaderLock(db,ct);
        var prior=await Orders(db,s).SingleOrDefaultAsync(o=>o.Id==command.RequestId,ct);
        if(prior!=null){if(prior.CreatedBy!=s.UserId||prior.LocationId!=command.LocationId||prior.RecipeId!=command.RecipeId||prior.PlannedQty!=command.Quantity||prior.Notes!=command.Notes||prior.NeededBy!=command.NeededBy||prior.PlannedDate!=command.PlannedDate||prior.RequestLines.Count!=0)throw new InventoryPostingConflictException("Different production source intent.");await tx.CommitAsync(ct);return new(prior.Id,prior.LocationId,"create",prior.Status.ToApiString(),null,true);}
        if(!await db.Recipes.AnyAsync(r=>r.TenantId==s.TenantId&&r.Id==command.RecipeId&&r.IsActive,ct))throw new InventoryPostingNotFoundException();
        var order=new ProductionOrder{Id=command.RequestId,TenantId=s.TenantId,LocationId=command.LocationId,RecipeId=command.RecipeId,PlannedQty=command.Quantity,Notes=command.Notes,NeededBy=command.NeededBy,PlannedDate=command.PlannedDate,CreatedBy=s.UserId,CreatedAt=clock.GetUtcNow(),UpdatedAt=clock.GetUtcNow()};db.Add(order);
        try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}catch(DbUpdateException e)when(e.InnerException is PostgresException{SqlState:PostgresErrorCodes.UniqueViolation}){throw new InventoryPostingConflictException("Production request identity already exists.");}
        return new(order.Id,order.LocationId,"create","planned",null,false);
    }
    private static void Allowed(ProductionOrder o,string phase)
    {
        if(phase=="start"&&(o.Status!=ProductionOrderStatus.Planned||o.Recipe?.IsActive!=true)||phase=="cancel"&&(o.Status is not (ProductionOrderStatus.Planned or ProductionOrderStatus.InProgress)||o.RequestLines.Count!=0))throw new InventoryPostingConflictException("Source decision unavailable; linked cancellations require the request planning flow.");
    }
    public async Task<ProductionDesktopReview> ReviewAsync(Guid id,string phase,CancellationToken ct)
    {
        ProductionDesktopRules.Phase(phase);await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY",ct);
        var s=await ProductionPostingPreviewReader.Establish(db,await Scope(ct),ct,phase=="start"?TenantRoleCapabilities.ProductionComplete:TenantRoleCapabilities.ProductionCancel);var o=await Orders(db,s).AsNoTracking().SingleOrDefaultAsync(o=>o.Id==id,ct)??throw new InventoryPostingNotFoundException();await Location(db,s,o.LocationId,ct);Allowed(o,phase);
        var can=enabled.Enabled&&gate.Allows(s.TenantId,o.LocationId)&&await ProductionPostingPreviewReader.Ready(db,s.TenantId,o.LocationId,ct);await tx.CommitAsync(ct);return new(Row(o),phase,can);
    }
    public async Task<ProductionDesktopResult> DecideAsync(Guid id,string phase,ProductionDesktopDecision command,CancellationToken ct)
    {
        ProductionDesktopRules.Phase(phase);if(command.RequestId==Guid.Empty)throw new ArgumentException("Request required.");TransferPostingRules.Fingerprint(command.ExpectedFingerprint);command=command with{Reason=WriteOffPostingRules.Reason("reverse",command.Reason)!};
        await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await ProductionPostingPreviewReader.Establish(db,await Scope(ct),ct,phase=="start"?TenantRoleCapabilities.ProductionComplete:TenantRoleCapabilities.ProductionCancel);
        var location=await db.ProductionOrders.Where(o=>o.TenantId==s.TenantId&&o.Id==id).Select(o=>(Guid?)o.LocationId).SingleOrDefaultAsync(ct)??throw new InventoryPostingNotFoundException();await Location(db,s,location,ct);await Ready(db,s,location,ct);await HeaderLock(db,ct);
        var o=await Orders(db,s).SingleAsync(o=>o.Id==id,ct);var source="production.desktop."+phase+".v1";var hash=ReceiptPostingRules.Hash(new{id,phase,command,actor=s.UserId});
        var prior=await db.BusinessDocuments.SingleOrDefaultAsync(d=>d.TenantId==s.TenantId&&d.SourceSystem==source&&d.SourceId==id.ToString(),ct);
        if(prior!=null){if(prior.DocumentKey!=command.RequestId||prior.ContentHash!=hash||prior.CreatedBy!=s.UserId||prior.Status!=(phase=="start"?"approved":"cancelled"))throw new InventoryPostingConflictException("Different source decision.");await tx.CommitAsync(ct);return new(id,location,phase,o.Status.ToApiString(),prior.Id,true);}
        Allowed(o,phase);if(ProductionDesktopRules.Fingerprint(o,o.RequestLines)!=command.ExpectedFingerprint||await db.ProductionOrderConsumptions.AnyAsync(c=>c.ProductionOrderId==id,ct))throw new InventoryPostingConflictException("Source changed or has inventory facts.");
        var at=clock.GetUtcNow();var doc=new BusinessDocument{TenantId=s.TenantId,DocumentKey=command.RequestId,DocumentType="production_order_decision",SourceSystem=source,SourceId=id.ToString(),CreatedBy=s.UserId,ContentHash=hash,CreatedAt=at.UtcDateTime,UpdatedAt=at.UtcDateTime,OccurredAt=at.UtcDateTime};db.Add(doc);
        try{await db.SaveChangesAsync(ct);doc.Status="submitted";await db.SaveChangesAsync(ct);db.Add(new DocumentApproval{TenantId=s.TenantId,DocumentId=doc.Id,Stage=1,ApproverId=s.UserId,Decision=phase=="start"?"approved":"rejected",ContentHash=hash,Reason=command.Reason,CreatedAt=at.UtcDateTime});doc.Status=phase=="start"?"approved":"cancelled";o.Status=phase=="start"?ProductionOrderStatus.InProgress:ProductionOrderStatus.Cancelled;if(phase=="start")o.StartedAt=at;o.UpdatedAt=at;await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        catch(DbUpdateException e)when(e.InnerException is PostgresException{SqlState:PostgresErrorCodes.UniqueViolation}){throw new InventoryPostingConflictException("Decision identity already used.");}
        return new(id,location,phase,o.Status.ToApiString(),doc.Id,false);
    }
}
