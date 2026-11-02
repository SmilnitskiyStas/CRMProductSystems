namespace ShelfGuard.Application.Features.Erp.Posting;
public record CatalogAccess(bool CanManageItems,bool CanViewRecipes,bool CanManageRecipes);
public record CatalogItemData(string Name,string Unit,string ItemType,int? ShelfLifeDays,bool IsActive,List<string> Barcodes);
public record CatalogIngredient(Guid ItemId,decimal Quantity);
public record CatalogRecipeData(string Name,Guid OutputItemId,decimal OutputQuantity,string? Notes,bool IsActive,List<CatalogIngredient> Ingredients);
public record CatalogItemView(Guid Id,CatalogItemData Data,string Fingerprint,decimal? CatalogPurchasePrice,decimal? CatalogRetailPrice,decimal VatRate);
public record CatalogRecipeLine(Guid ItemId,string Name,string Unit,decimal Quantity,bool IsActive);
public record CatalogRecipeView(Guid Id,CatalogRecipeData Data,string OutputName,string Unit,string Fingerprint,bool HasOrders,bool HasActiveOrders,IReadOnlyList<CatalogRecipeLine> Lines);
public record CatalogSave(Guid OperationId,string Kind,Guid? Id,Guid? BasedOnId,string? ExpectedFingerprint,string Reason,CatalogItemData? Item,CatalogRecipeData? Recipe,Dictionary<Guid,string>? ItemFingerprints=null);
public record CatalogResult(Guid EntityId,Guid DocumentId,string Kind,bool Replayed);
public interface ICatalogDesktop
{
    Task<CatalogAccess> AccessAsync(CancellationToken ct);
    Task<ProductionDesktopPage<CatalogItemView>> ItemsAsync(string? search,string state,int page,CancellationToken ct);
    Task<IReadOnlyList<CatalogItemView>> SelectionAsync(Guid[] ids,CancellationToken ct);
    Task<CatalogItemView> ItemAsync(Guid id,CancellationToken ct);
    Task<ProductionDesktopPage<CatalogRecipeView>> RecipesAsync(string? search,string state,int page,CancellationToken ct);
    Task<CatalogRecipeView> RecipeAsync(Guid id,CancellationToken ct);
    Task<CatalogResult> SaveAsync(CatalogSave command,CancellationToken ct);
}
public sealed class CatalogConflict(string code,string message):Exception(message){public string Code{get;}=code;}
public static class CatalogRules
{
    public const string Protocol="catalog.desktop.v1";
    public static string Text(string? value,int max,bool optional=false){var s=value?.Trim()??"";if(!optional&&s.Length==0||s.Length>max||s.Any(char.IsControl))throw new ArgumentException("Invalid catalog text.");return s;}
    public static void Quantity(decimal value){if(value<=0||value>9_999_999.999m||decimal.Round(value,3)!=value)throw new ArgumentException("Exact positive recipe quantity required.");}
    public static CatalogSave Normalize(CatalogSave c)
    {
        if(c.OperationId==Guid.Empty||c.Kind is not ("item" or "recipe")||c.Id==Guid.Empty||c.BasedOnId==Guid.Empty||c.Id!=null&&c.BasedOnId!=null||c.Kind=="item"&&c.BasedOnId!=null)throw new ArgumentException("Invalid catalog identity.");
        if(c.Id!=null||c.BasedOnId!=null)TransferPostingRules.Fingerprint(c.ExpectedFingerprint);else if(c.ExpectedFingerprint!=null)throw new ArgumentException("Unexpected fingerprint.");
        if(c.ItemFingerprints!=null){if(c.Kind!="recipe"||c.ItemFingerprints.Count>100)throw new ArgumentException("Invalid references.");foreach(var entry in c.ItemFingerprints){if(entry.Key==Guid.Empty)throw new ArgumentException("Invalid item identity.");TransferPostingRules.Fingerprint(entry.Value);}c=c with{ItemFingerprints=c.ItemFingerprints.OrderBy(e=>e.Key).ToDictionary(e=>e.Key,e=>e.Value)};}
        c=c with{Reason=WriteOffPostingRules.Reason("reverse",c.Reason)!};
        if(c.Kind=="item")
        {
            var d=c.Item??throw new ArgumentException("Item data required.");if(c.Recipe!=null||d.Barcodes==null||d.Barcodes.Count>20||d.ShelfLifeDays is <1 or >3650||d.ItemType is not ("product" or "raw_material" or "consumable" or "packaging" or "spare_part" or "kit" or "service"))throw new ArgumentException("Invalid item fields.");
            var barcodes=d.Barcodes.Select(b=>Text(b,128)).Order(StringComparer.Ordinal).ToList();if(barcodes.Distinct(StringComparer.Ordinal).Count()!=barcodes.Count)throw new ArgumentException("Duplicate barcodes.");return c with{Item=d with{Name=Text(d.Name,255),Unit=Text(d.Unit,20),Barcodes=barcodes}};
        }
        var r=c.Recipe??throw new ArgumentException("Recipe data required.");if(c.Item!=null||r.OutputItemId==Guid.Empty||r.Ingredients is not {Count:>0 and <=99}||r.Ingredients.Any(l=>l.ItemId==Guid.Empty||l.ItemId==r.OutputItemId)||r.Ingredients.Select(l=>l.ItemId).Distinct().Count()!=r.Ingredients.Count)throw new ArgumentException("Invalid recipe ingredients.");Quantity(r.OutputQuantity);foreach(var l in r.Ingredients)Quantity(l.Quantity);return c with{Recipe=r with{OutputQuantity=System.Decimal.Parse(r.OutputQuantity.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture),System.Globalization.CultureInfo.InvariantCulture),Name=Text(r.Name,255),Notes=Text(r.Notes,2000,true) is string notes&&notes.Length>0?notes:null,Ingredients=r.Ingredients.Select(l=>l with{Quantity=System.Decimal.Parse(l.Quantity.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture),System.Globalization.CultureInfo.InvariantCulture)}).OrderBy(l=>l.ItemId).ToList()}};
    }
}
