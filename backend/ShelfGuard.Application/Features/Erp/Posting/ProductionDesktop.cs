using ShelfGuard.Domain.Entities;
namespace ShelfGuard.Application.Features.Erp.Posting;
public sealed record ProductionDesktopAccess(string Protocol,bool CanCreate,bool CanStart,bool CanCancel,bool CanComplete);
public sealed record ProductionDesktopOrder(Guid Id,Guid LocationId,Guid RecipeId,string RecipeName,string LocationName,decimal Quantity,string Status,
    string? Notes,DateOnly? NeededBy,DateOnly? PlannedDate,DateTimeOffset CreatedAt,DateTimeOffset? StartedAt,DateTimeOffset? CompletedAt,int RequestCount,string Fingerprint);
public sealed record ProductionDesktopRecipe(Guid Id,string Name,string OutputName,decimal OutputQuantity,string Unit,int IngredientCount);
public sealed record ProductionDesktopPage<T>(IReadOnlyList<T> Items,int TotalCount,int Page,int PageSize,int TotalPages);
public sealed record ProductionDesktopCreate(Guid RequestId,Guid LocationId,Guid RecipeId,decimal Quantity,string? Notes,DateOnly? NeededBy=null,DateOnly? PlannedDate=null);
public sealed record ProductionDesktopDecision(Guid RequestId,string ExpectedFingerprint,string Reason);
public sealed record ProductionDesktopResult(Guid OrderId,Guid LocationId,string Phase,string Status,Guid? DocumentId,bool Replayed);
public sealed record ProductionDesktopReview(ProductionDesktopOrder Order,string Phase,bool CanPost);
public interface IProductionDesktop
{
    Task<ProductionDesktopOrder> OrderAsync(Guid id,Guid locationId,CancellationToken ct);
    Task<ProductionDesktopAccess> AccessAsync(CancellationToken ct);
    Task<ProductionDesktopPage<ProductionDesktopOrder>> OrdersAsync(Guid locationId,string? status,string? search,int page,CancellationToken ct);
    Task<ProductionDesktopPage<ProductionDesktopRecipe>> RecipesAsync(string? search,int page,CancellationToken ct);
    Task<ProductionDesktopResult> CreateAsync(ProductionDesktopCreate command,CancellationToken ct);
    Task<ProductionDesktopReview> ReviewAsync(Guid id,string phase,CancellationToken ct);
    Task<ProductionDesktopResult> DecideAsync(Guid id,string phase,ProductionDesktopDecision command,CancellationToken ct);
}
public static class ProductionDesktopRules
{
    public const string Protocol="production.desktop.v1";
    public static ProductionDesktopCreate Normalize(ProductionDesktopCreate c)
    {
        if(c.RequestId==Guid.Empty||c.LocationId==Guid.Empty||c.RecipeId==Guid.Empty||!TransferPostingRules.Exact(c.Quantity)||c.Quantity<=0||c.Quantity>9_999_999.99m
            ||c.Notes?.Length>2000||c.Notes?.Any(char.IsControl)==true)throw new ArgumentException("Invalid production source.");
        return c with{Notes=string.IsNullOrWhiteSpace(c.Notes)?null:c.Notes.Trim()};
    }
    public static void Phase(string phase){if(phase is not ("start" or "cancel"))throw new ArgumentException("Invalid production decision.");}
    public static string Fingerprint(ProductionOrder o,IEnumerable<ProductionOrderRequestLine> links)=>ReceiptPostingRules.Hash(new{
        protocol=Protocol,o.Id,o.TenantId,o.LocationId,o.RecipeId,o.PlannedQty,o.Status,o.Notes,o.NeededBy,o.PlannedDate,o.CreatedBy,o.CreatedAt,o.UpdatedAt,o.StartedAt,o.CompletedAt,
        links=links.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RequestLineId,x.Qty}).ToArray()});
}
