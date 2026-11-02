using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Entities.Erp;
namespace ShelfGuard.Infrastructure.Data.Repositories;
public sealed class CatalogDesktopStore(DbContextOptions<AppDbContext> options,IWorkAccessContext access,TimeProvider clock):ICatalogDesktop
{
    private async Task<WorkAccessScope> Scope(AppDbContext db,CancellationToken ct)
    {var s=await ProductionPostingPreviewReader.Establish(db,await access.GetAsync(ct)??throw new WorkAccessDeniedException(),ct,"catalog",false,false);if(!s.CanStock||!s.Permissions.GetValueOrDefault("catalog",true))throw new WorkAccessDeniedException();return s;}
    private static bool ManageItems(WorkAccessScope s)=>InventoryAdjustmentPlanner.CanAdjust(s.Role)&&s.Permissions.GetValueOrDefault("catalog.manage",true);
    private static async Task<bool> Production(AppDbContext db,WorkAccessScope s,CancellationToken ct)=>(await db.Tenants.SingleAsync(t=>t.Id==s.TenantId,ct)).HasModule("production");
    private static void Query(string? search,string state,int page){if(search?.Length>200||state is not ("all" or "active" or "inactive")||page<1||page>100000)throw new ArgumentException("Invalid catalog query.");}
    private static CatalogItemData Data(Item i)=>new(i.Name,i.Unit,i.ItemType,i.ShelfLifeDays,i.IsActive,i.Barcodes.Order(StringComparer.Ordinal).ToList());
    internal static CatalogItemView View(Item i)=>new(i.Id,Data(i),ReceiptPostingRules.Hash(new{i.Id,i.TenantId,i.Name,i.Unit,i.ItemType,i.ShelfLifeDays,i.IsActive,i.Barcodes,i.UpdatedAt,i.PricePurchase,i.PriceRetail,i.VatRate}),i.PricePurchase,i.PriceRetail,i.VatRate);
    private static CatalogRecipeData Data(Recipe r)=>new(r.Name,r.OutputItemId,r.OutputQty,r.Notes,r.IsActive,r.Ingredients.OrderBy(l=>l.ItemId).Select(l=>new CatalogIngredient(l.ItemId,l.Qty)).ToList());
    private static IQueryable<Recipe> Recipes(AppDbContext db,Guid tenant)=>db.Recipes.Where(r=>r.TenantId==tenant).Include(r=>r.OutputItem).Include(r=>r.Ingredients).ThenInclude(l=>l.Item).AsSplitQuery();
    private static async Task<CatalogRecipeView> View(AppDbContext db,Recipe r,CancellationToken ct)
    {
        var has=await db.ProductionOrders.AnyAsync(o=>o.TenantId==r.TenantId&&o.RecipeId==r.Id,ct);var active=await db.ProductionOrders.AnyAsync(o=>o.TenantId==r.TenantId&&o.RecipeId==r.Id&&(o.Status==ProductionOrderStatus.Planned||o.Status==ProductionOrderStatus.InProgress),ct);
        var fp=ReceiptPostingRules.Hash(new{r.Id,r.TenantId,r.Name,r.OutputItemId,r.OutputQty,r.Unit,r.Notes,r.IsActive,r.UpdatedAt,has,active,output=r.OutputItem==null?null:new{r.OutputItem.Name,r.OutputItem.Unit,r.OutputItem.IsActive,r.OutputItem.ShelfLifeDays,r.OutputItem.UpdatedAt},lines=r.Ingredients.OrderBy(l=>l.Id).Select(l=>new{l.Id,l.ItemId,l.Qty,l.Unit,name=l.Item?.Name,itemUnit=l.Item?.Unit,active=l.Item?.IsActive,updated=l.Item?.UpdatedAt})});
        return new(r.Id,Data(r),r.OutputItem?.Name??"Товар недоступний",r.Unit,fp,has,active,r.Ingredients.OrderBy(l=>l.ItemId).Select(l=>new CatalogRecipeLine(l.ItemId,l.Item?.Name??"Товар недоступний",l.Unit,l.Qty,l.Item?.IsActive==true)).ToList());
    }
    public async Task<CatalogAccess> AccessAsync(CancellationToken ct)
    {await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await Scope(db,ct);var production=await Production(db,s,ct);await tx.CommitAsync(ct);return new(ManageItems(s),production&&s.Has(TenantRoleCapabilities.ProductionView),production&&s.Has(TenantRoleCapabilities.ProductionView)&&s.Has(TenantRoleCapabilities.ProductionManage));}
    public async Task<ProductionDesktopPage<CatalogItemView>> ItemsAsync(string? search,string state,int page,CancellationToken ct)
    {
        Query(search,state,page);await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'",ct);var s=await Scope(db,ct);var q=db.Items.AsNoTracking().Where(i=>i.TenantId==s.TenantId);if(state!="all")q=q.Where(i=>i.IsActive==(state=="active"));if(!string.IsNullOrWhiteSpace(search)){var pattern="%"+search.Trim()+"%";q=db.Items.FromSqlInterpolated($"SELECT * FROM public.items WHERE \"TenantId\"={s.TenantId} AND (\"Name\" ILIKE {pattern} OR \"Barcodes\" ? {search.Trim()})").AsNoTracking();if(state!="all")q=q.Where(i=>i.IsActive==(state=="active"));}var count=await q.CountAsync(ct);var rows=await q.OrderBy(i=>i.Name).ThenBy(i=>i.Id).Skip((page-1)*50).Take(50).ToListAsync(ct);await tx.CommitAsync(ct);return new(rows.Select(View).ToList(),count,page,50,(count+49)/50);
    }
    public async Task<IReadOnlyList<CatalogItemView>> SelectionAsync(Guid[] ids,CancellationToken ct)
    {if(ids.Length is <1 or >100||ids.Any(i=>i==Guid.Empty)||ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Invalid selection.");await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);var s=await Scope(db,ct);var rows=await db.Items.AsNoTracking().Where(i=>i.TenantId==s.TenantId&&ids.Contains(i.Id)).OrderBy(i=>i.Id).ToListAsync(ct);if(rows.Count!=ids.Length)throw new InventoryPostingNotFoundException();await tx.CommitAsync(ct);return rows.Select(View).ToList();}
    public async Task<CatalogItemView> ItemAsync(Guid id,CancellationToken ct)
    {await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await Scope(db,ct);var i=await db.Items.AsNoTracking().SingleOrDefaultAsync(i=>i.TenantId==s.TenantId&&i.Id==id,ct)??throw new InventoryPostingNotFoundException();await tx.CommitAsync(ct);return View(i);}
    private static async Task RecipeAccess(AppDbContext db,WorkAccessScope s,bool write,CancellationToken ct)
    {if(!await Production(db,s,ct)||!s.Has(TenantRoleCapabilities.ProductionView)||write&&!s.Has(TenantRoleCapabilities.ProductionManage))throw new WorkAccessDeniedException();}
    public async Task<ProductionDesktopPage<CatalogRecipeView>> RecipesAsync(string? search,string state,int page,CancellationToken ct)
    {
        Query(search,state,page);await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='15s'",ct);var s=await Scope(db,ct);await RecipeAccess(db,s,false,ct);var q=Recipes(db,s.TenantId).AsNoTracking();if(state!="all")q=q.Where(r=>r.IsActive==(state=="active"));if(!string.IsNullOrWhiteSpace(search))q=q.Where(r=>EF.Functions.ILike(r.Name,"%"+search.Trim()+"%")||EF.Functions.ILike(r.OutputItem!.Name,"%"+search.Trim()+"%"));var count=await q.CountAsync(ct);var rows=await q.OrderBy(r=>r.Name).ThenBy(r=>r.Id).Skip((page-1)*50).Take(50).ToListAsync(ct);var result=new List<CatalogRecipeView>();foreach(var r in rows)result.Add(await View(db,r,ct));await tx.CommitAsync(ct);return new(result,count,page,50,(count+49)/50);
    }
    public async Task<CatalogRecipeView> RecipeAsync(Guid id,CancellationToken ct)
    {await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);var s=await Scope(db,ct);await RecipeAccess(db,s,false,ct);var r=await Recipes(db,s.TenantId).AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id,ct)??throw new InventoryPostingNotFoundException();var result=await View(db,r,ct);await tx.CommitAsync(ct);return result;}
    private static CatalogConflict Conflict(string code,string message)=>new(code,message);
    private async Task<Guid> SaveItem(AppDbContext db,WorkAccessScope s,CatalogSave c,CancellationToken ct)
    {
        var d=c.Item!;var item=c.Id is Guid id?await db.Items.SingleOrDefaultAsync(i=>i.TenantId==s.TenantId&&i.Id==id,ct)??throw new InventoryPostingNotFoundException():new Item{Id=c.OperationId,TenantId=s.TenantId};
        if(c.Id!=null){if(View(item).Fingerprint!=c.ExpectedFingerprint)throw Conflict("catalog_changed","Товар змінився. Оновіть перегляд.");if(item.Unit!=d.Unit||item.ItemType!=d.ItemType)throw Conflict("catalog_item_identity_locked","Одиниця й тип наявного товару зафіксовані. Створіть окрему картку для іншого товару.");}
        if(d.Barcodes.Count>0&&await db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM public.items WHERE \"TenantId\"={s.TenantId} AND \"Id\"<>{item.Id} AND \"Barcodes\" ?| {d.Barcodes.ToArray()}) AS \"Value\"").SingleAsync(ct))throw Conflict("catalog_barcode_conflict","Штрихкод уже належить іншому товару підприємства.");
        if(c.Id!=null&&item.IsActive&&!d.IsActive&&(await db.DocumentLines.AnyAsync(l=>l.TenantId==s.TenantId&&l.ItemId==item.Id&&db.BusinessDocuments.Any(d=>d.TenantId==s.TenantId&&d.Id==l.DocumentId&&!d.SourceSystem.StartsWith("catalog.")),ct)||await db.ProductStocks.AnyAsync(b=>b.TenantId==s.TenantId&&b.ProductId==item.Id,ct)||await db.Recipes.AnyAsync(r=>r.TenantId==s.TenantId&&r.IsActive&&(r.OutputItemId==item.Id||r.Ingredients.Any(l=>l.ItemId==item.Id)),ct)||await db.ProductionRequestLines.AnyAsync(l=>l.TenantId==s.TenantId&&l.ItemId==item.Id,ct)))throw Conflict("catalog_item_in_use","Товар має облікову історію або використовується в рецептурі чи заявці. Архівацію заблоковано.");
        item.Name=d.Name;item.Unit=d.Unit;item.ItemType=d.ItemType;item.ShelfLifeDays=d.ShelfLifeDays;item.IsActive=d.IsActive;item.Barcodes=d.Barcodes;if(c.Id==null)db.Add(item);await db.SaveChangesAsync(ct);return item.Id;
    }
    private static bool Same(CatalogRecipeData a,CatalogRecipeData b)=>a.Name==b.Name&&a.OutputItemId==b.OutputItemId&&a.OutputQuantity==b.OutputQuantity&&a.Notes==b.Notes&&a.Ingredients.Count==b.Ingredients.Count&&a.Ingredients.All(l=>b.Ingredients.Any(x=>x.ItemId==l.ItemId&&x.Quantity==l.Quantity));
    private async Task<Guid> SaveRecipe(AppDbContext db,WorkAccessScope s,CatalogSave c,CancellationToken ct)
    {
        var d=c.Recipe!;Recipe? current=null;
        var sourceId=c.Id??c.BasedOnId;if(sourceId is Guid id){current=await Recipes(db,s.TenantId).SingleOrDefaultAsync(r=>r.Id==id,ct)??throw new InventoryPostingNotFoundException();if((await View(db,current,ct)).Fingerprint!=c.ExpectedFingerprint)throw Conflict("catalog_changed","Рецептура або її товари змінилися. Оновіть перегляд.");}
        var itemIds=d.Ingredients.Select(l=>l.ItemId).Append(d.OutputItemId).ToArray();var items=await db.Items.Where(i=>i.TenantId==s.TenantId&&itemIds.Contains(i.Id)).ToDictionaryAsync(i=>i.Id,ct);if(items.Count!=itemIds.Length||items.Values.Any(i=>i.ItemType=="service"||d.IsActive&&!i.IsActive))throw Conflict("catalog_recipe_items","Оберіть активні складські товари для випуску та всіх складових.");if(c.ItemFingerprints!=null&&(c.ItemFingerprints.Count!=items.Count||items.Values.Any(i=>!c.ItemFingerprints.TryGetValue(i.Id,out var fp)||fp!=View(i).Fingerprint)))throw Conflict("catalog_changed","Товари рецептури змінилися після перегляду.");var output=items[d.OutputItemId];if(d.IsActive&&output.ShelfLifeDays is not >0)throw Conflict("catalog_recipe_shelf_life","Вкажіть термін придатності готової продукції перед збереженням рецептури.");
        var hasOrders=c.Id!=null&&await db.ProductionOrders.AnyAsync(o=>o.TenantId==s.TenantId&&o.RecipeId==c.Id,ct);if(hasOrders&&!Same(Data(current!),d))throw Conflict("catalog_recipe_sealed","Рецептура вже має замовлення. Для зміни складу чи виходу створіть нову редакцію-копію.");
        if(c.Id!=null&&!d.IsActive&&await db.ProductionOrders.AnyAsync(o=>o.TenantId==s.TenantId&&o.RecipeId==c.Id&&(o.Status==ProductionOrderStatus.Planned||o.Status==ProductionOrderStatus.InProgress),ct))throw Conflict("catalog_recipe_in_use","Не можна архівувати рецепт із незавершеними виробничими замовленнями.");
        var r=c.Id!=null?current!:new Recipe{Id=c.OperationId,TenantId=s.TenantId,CreatedAt=clock.GetUtcNow()};
        if(!hasOrders){r.Name=d.Name;r.OutputItemId=d.OutputItemId;r.OutputQty=d.OutputQuantity;r.Unit=output.Unit;r.Notes=d.Notes;if(c.Id!=null){db.RecipeIngredients.RemoveRange(r.Ingredients);await db.SaveChangesAsync(ct);r.Ingredients.Clear();}foreach(var line in d.Ingredients){var ingredient=new RecipeIngredient{RecipeId=r.Id,ItemId=line.ItemId,Qty=line.Quantity,Unit=items[line.ItemId].Unit};db.Add(ingredient);} }
        r.IsActive=d.IsActive;r.UpdatedAt=clock.GetUtcNow();if(c.Id==null)db.Add(r);await db.SaveChangesAsync(ct);return r.Id;
    }
    public async Task<CatalogResult> SaveAsync(CatalogSave command,CancellationToken ct)
    {
        var c=CatalogRules.Normalize(command);await using var db=new AppDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(ct);var s=await Scope(db,ct);if(c.Kind=="recipe")await RecipeAccess(db,s,true,ct);else if(!ManageItems(s))throw new WorkAccessDeniedException();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'; LOCK TABLE public.production_orders IN SHARE ROW EXCLUSIVE MODE; LOCK TABLE public.recipes,public.recipe_ingredients,public.items IN SHARE ROW EXCLUSIVE MODE",ct);
            var hash=ReceiptPostingRules.Hash(new{protocol=CatalogRules.Protocol,c,actor=s.UserId});var prior=await db.BusinessDocuments.SingleOrDefaultAsync(d=>d.TenantId==s.TenantId&&d.DocumentKey==c.OperationId,ct);
            if(prior!=null){if(prior.SourceSystem!="catalog."+c.Kind+".v1"||prior.ContentHash!=hash||prior.CreatedBy!=s.UserId||prior.Status!="approved"||!Guid.TryParse(prior.Number,out var entity))throw Conflict("catalog_command_conflict","Цей запит уже використано для іншого рішення.");await tx.CommitAsync(ct);return new(entity,prior.Id,c.Kind,true);}
            var id=c.Kind=="item"?await SaveItem(db,s,c,ct):await SaveRecipe(db,s,c,ct);var at=clock.GetUtcNow().UtcDateTime;var doc=new BusinessDocument{TenantId=s.TenantId,DocumentKey=c.OperationId,SourceSystem="catalog."+c.Kind+".v1",SourceId=c.OperationId.ToString(),Number=id.ToString(),DocumentType="catalog_change",CreatedBy=s.UserId,ContentHash=hash,CreatedAt=at,UpdatedAt=at,OccurredAt=at};db.Add(doc);await db.SaveChangesAsync(ct);
            var itemId=c.Kind=="item"?id:c.Recipe!.OutputItemId;var item=await db.Items.SingleAsync(i=>i.TenantId==s.TenantId&&i.Id==itemId,ct);db.Add(new DocumentLine{TenantId=s.TenantId,DocumentId=doc.Id,LineNumber=1,ItemId=itemId,Quantity=1,Unit=item.Unit,ItemNameSnapshot=item.Name,Metadata=JsonSerializer.Serialize(new{entityId=id,c,unit=item.Unit})});await db.SaveChangesAsync(ct);doc.Status="submitted";await db.SaveChangesAsync(ct);db.Add(new DocumentApproval{TenantId=s.TenantId,DocumentId=doc.Id,Stage=1,ApproverId=s.UserId,Decision="approved",ContentHash=hash,Reason=c.Reason,CreatedAt=at});doc.Status="approved";await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return new(id,doc.Id,c.Kind,false);
        }
        catch(Exception e)when(e is PostgresException||e.InnerException is PostgresException){var p=e as PostgresException??(PostgresException)e.InnerException!;if(p.SqlState is "23505" or "55P03" or "40001" or "40P01")throw Conflict("catalog_concurrent","Довідник змінюється іншим користувачем. Перевірте збережену операцію.");throw;}
    }
}
