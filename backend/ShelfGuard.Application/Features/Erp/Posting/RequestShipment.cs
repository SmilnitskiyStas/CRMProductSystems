namespace ShelfGuard.Application.Features.Erp.Posting;
public record RequestShipmentCommand(Guid OperationId,string ExpectedFingerprint,string Reason);
public record RequestShipmentPreview(Guid RequestId,Guid OperationId,Guid FromLocationId,Guid ToLocationId,string Fingerprint,bool CanShip,IReadOnlyList<string> Issues,TransferPostingPreview? Transfer);
public record RequestShipmentResult(Guid RequestId,Guid TransferId,Guid DocumentId,Guid PostingId,Guid EventId,bool Replayed);
public interface IRequestShipment
{
    Task<RequestShipmentPreview> PreviewAsync(Guid requestId,Guid operationId,Guid locationId,CancellationToken ct);
    Task<RequestShipmentResult> ShipAsync(Guid requestId,Guid locationId,RequestShipmentCommand command,CancellationToken ct);
}
