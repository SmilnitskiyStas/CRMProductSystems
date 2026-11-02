using ShelfGuard.Application.Features.Production.Dtos;
namespace ShelfGuard.Application.Features.Erp.Posting;

public record RequestDesktopAccess(bool CanCreate,bool CanReview,bool CanCancel,bool ProductionEnabled,bool CanShip=false,bool CanReceiveShipment=false,bool CanCancelShipment=false);
public record RequestDesktopQuery(Guid LocationId,string Side,string? Status,string? Search,int Page=1);
public record RequestDesktopSource(ProductionRequestDetailDto Request,string Fingerprint,bool CanSubmit,bool CanCancel,bool CanReview);
public record RequestDesktopItem(Guid Id,string Name,string Unit);
public record RequestDesktopDestination(Guid Id,string Name);
public record RequestDesktopCreate(Guid OperationId,Guid StoreLocationId,Guid ProductionLocationId,DateOnly NeededBy,string? Notes,List<ProductionRequestLineCreateDto> Lines);
public record RequestDesktopDecision(Guid OperationId,string ExpectedFingerprint,string Reason,List<ProductionRequestLineReviewDto>? Lines=null);
public record RequestDesktopResult(Guid RequestId,Guid DocumentId,string Phase,bool Replayed);
public interface IRequestDesktop
{
    Task<RequestDesktopAccess> AccessAsync(CancellationToken ct);
    Task<ProductionDesktopPage<ProductionRequestListItemDto>> ListAsync(RequestDesktopQuery query,CancellationToken ct);
    Task<RequestDesktopSource> GetAsync(Guid id,Guid locationId,string side,CancellationToken ct);
    Task<ProductionDesktopPage<RequestDesktopItem>> ItemsAsync(string? search,int page,CancellationToken ct);
    Task<IReadOnlyList<RequestDesktopDestination>> DestinationsAsync(CancellationToken ct);
    Task<RequestDesktopResult> CreateAsync(RequestDesktopCreate command,CancellationToken ct);
    Task<RequestDesktopResult> DecideAsync(Guid id,Guid locationId,string side,string phase,RequestDesktopDecision command,CancellationToken ct);
}
public static class RequestDesktopRules
{
    public const string Protocol="requests.desktop.v1";
    public static string Side(string side)=>side is "store" or "production"?side:throw new ArgumentException("Invalid request side.");
    public static string Phase(string phase)=>phase is "submit" or "cancel" or "review" or "reject"?phase:throw new ArgumentException("Invalid request action.");
    public static string? Notes(string? value,int max=2000){value=value?.Trim();if(value?.Length>max||value?.Any(char.IsControl)==true)throw new ArgumentException("Invalid notes.");return string.IsNullOrWhiteSpace(value)?null:value;}
    public static void Quantity(decimal value,bool zero=false){if(value<(zero?0:0.01m)||value>9_999_999.99m||decimal.Round(value,2)!=value)throw new ArgumentException("Exact request quantity required.");}
    public static RequestDesktopCreate Normalize(RequestDesktopCreate c)
    {
        if(c.OperationId==Guid.Empty||c.StoreLocationId==Guid.Empty||c.ProductionLocationId==Guid.Empty||c.StoreLocationId==c.ProductionLocationId||c.Lines is not {Count:>0 and <=50}||c.Lines.Any(l=>l.ItemId==Guid.Empty)||c.Lines.Select(l=>l.ItemId).Distinct().Count()!=c.Lines.Count)throw new ArgumentException("Invalid request source.");
        foreach(var l in c.Lines)Quantity(l.RequestedQty);
        return c with{Notes=Notes(c.Notes),Lines=c.Lines.OrderBy(l=>l.ItemId).ToList()};
    }
}
