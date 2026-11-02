using ShelfGuard.Application.Services;
namespace ShelfGuard.Application.Features.Erp.Posting;

public sealed record PosReturnCommand(Guid ShiftId, Guid ClientRequestId, string ExpectedFingerprint, string Reason, bool TechnicalReturn = false);
public sealed record PosReturnBatch(Guid OriginalEntryId, Guid BatchId, Guid ItemId, decimal Quantity, decimal CurrentQuantity, decimal? UnitCost);
public sealed record PosReturnPreview(Guid SaleId, Guid ShiftId, string Fingerprint, decimal RefundAmount, decimal TaxAmount, bool CanPost, IReadOnlyList<PosReturnBatch> Batches);
public sealed record PosReturnResult(Guid SaleId, Guid DocumentId, Guid PostingId, decimal RefundAmount, string FiscalStatus, bool Replayed);
public sealed record PosReturnFiscalResult(Guid DocumentId, string Status, int Attempts, string? FiscalNumber);
public sealed record PosReturnOptions(bool Enabled);
public interface IPosReturnPosting
{
    Task<PosReturnPreview> PreviewAsync(Guid saleId, Guid shiftId, CancellationToken ct = default);
    Task<PosReturnResult> PostAsync(Guid saleId, PosReturnCommand command, CancellationToken ct = default);
    Task<PosReturnFiscalResult> FiscalizeAsync(Guid saleId, CancellationToken ct = default);
}
public interface IPosReturnStore
{
    Task<PosReturnPreview> PreviewAsync(WorkAccessScope scope, Guid saleId, Guid shiftId, CancellationToken ct);
    Task<PosReturnResult> PostAsync(WorkAccessScope scope, Guid saleId, PosReturnCommand command, CancellationToken ct);
    Task<PosReturnFiscalResult> FiscalizeAsync(WorkAccessScope scope, Guid saleId, CancellationToken ct);
}
public sealed class PosReturnPostingService(IWorkAccessContext access, IPosReturnStore store) : IPosReturnPosting
{
    private async Task<WorkAccessScope> Scope(CancellationToken ct) => await access.GetAsync(ct) ?? throw new WorkAccessDeniedException();
    public async Task<PosReturnPreview> PreviewAsync(Guid saleId, Guid shiftId, CancellationToken ct = default) => await store.PreviewAsync(await Scope(ct), saleId, shiftId, ct);
    public async Task<PosReturnResult> PostAsync(Guid saleId, PosReturnCommand command, CancellationToken ct = default)
    {
        TransferPostingRules.Fingerprint(command.ExpectedFingerprint);
        if (command.ShiftId == Guid.Empty || command.ClientRequestId == Guid.Empty) throw new ArgumentException("Shift and durable client identity required.");
        var reason = WriteOffPostingRules.Reason("reverse", command.Reason)!;
        return await store.PostAsync(await Scope(ct), saleId, command with { Reason = reason }, ct);
    }
    public async Task<PosReturnFiscalResult> FiscalizeAsync(Guid saleId, CancellationToken ct = default) => await store.FiscalizeAsync(await Scope(ct), saleId, ct);
}
