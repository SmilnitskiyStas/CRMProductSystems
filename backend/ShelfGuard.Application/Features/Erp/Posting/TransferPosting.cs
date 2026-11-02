using System.Globalization;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Application.Features.Erp.Posting;

public sealed record TransferDispatchItem(Guid BatchId, decimal Quantity);
public sealed record TransferDispatchCommand(Guid RequestId, Guid FromLocationId, Guid ToLocationId,
    string? TransferType, string? Notes, List<TransferDispatchItem> Items, string? ExpectedFingerprint = null);
public sealed record TransferActionCommand(string ExpectedFingerprint, string? Reason = null);
public sealed record TransferReviewedLine(Guid BatchId, Guid ItemId, string ItemName, string Unit, decimal Quantity,
    decimal? AvailableQuantity, string? BatchNumber, DateOnly ExpiryDate, decimal? UnitCost,
    string? QualityStatus = null, string? ExactUnitCost = null);
public sealed record TransferPostingPreview(Guid TransferId, string Phase, string Fingerprint, bool Configured, bool Ready,
    bool CanPost, IReadOnlyList<TransferReviewedLine> Lines, bool SupportsDesktopReview = true);
public sealed record TransferPostingResult(Guid TransferId, string Phase, Guid DocumentId, Guid PostingId, Guid EventId, bool Replayed);
public sealed record TransferPostingOptions(bool Enabled);
public interface ITransferPosting
{
    Task<TransferPostingPreview> PreviewDispatchAsync(TransferDispatchCommand command, CancellationToken ct = default);
    Task<TransferPostingResult> DispatchAsync(TransferDispatchCommand command, CancellationToken ct = default);
    Task<TransferPostingPreview> PreviewAsync(Guid transferId, string phase, CancellationToken ct = default);
    Task<TransferPostingResult> CompleteAsync(Guid transferId, string phase, TransferActionCommand command, CancellationToken ct = default);
}
public interface ITransferPostingStore
{
    Task<TransferPostingPreview> PreviewDispatchAsync(WorkAccessScope scope, TransferDispatchCommand command, CancellationToken ct);
    Task<TransferPostingResult> DispatchAsync(WorkAccessScope scope, TransferDispatchCommand command, CancellationToken ct);
    Task<TransferPostingPreview> PreviewAsync(WorkAccessScope scope, Guid transferId, string phase, CancellationToken ct);
    Task<TransferPostingResult> CompleteAsync(WorkAccessScope scope, Guid transferId, string phase, TransferActionCommand command, CancellationToken ct);
}
public sealed class TransferPostingService(IWorkAccessContext access, ITransferPostingStore store) : ITransferPosting
{
    private async Task<WorkAccessScope> Scope(CancellationToken ct) => await access.GetAsync(ct) ?? throw new WorkAccessDeniedException();
    public async Task<TransferPostingPreview> PreviewDispatchAsync(TransferDispatchCommand command, CancellationToken ct = default) =>
        await store.PreviewDispatchAsync(await Scope(ct), TransferPostingRules.Normalize(command), ct);
    public async Task<TransferPostingResult> DispatchAsync(TransferDispatchCommand command, CancellationToken ct = default)
    { TransferPostingRules.Fingerprint(command.ExpectedFingerprint); return await store.DispatchAsync(await Scope(ct), TransferPostingRules.Normalize(command), ct); }
    public async Task<TransferPostingPreview> PreviewAsync(Guid transferId, string phase, CancellationToken ct = default)
    { TransferPostingRules.Phase(phase); return await store.PreviewAsync(await Scope(ct), transferId, phase, ct); }
    public async Task<TransferPostingResult> CompleteAsync(Guid transferId, string phase, TransferActionCommand command, CancellationToken ct = default)
    { TransferPostingRules.Phase(phase); TransferPostingRules.Fingerprint(command.ExpectedFingerprint);
      return await store.CompleteAsync(await Scope(ct), transferId, phase, command with { Reason = TransferPostingRules.Reason(phase, command.Reason) }, ct); }
}
public static class TransferPostingRules
{
    public static string Source(string phase) => $"transfer.{phase}.v1";
    public static string Key(Guid id, string phase) => $"{Source(phase)}:{id:N}";
    public static string Number(decimal value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
    public static bool Exact(decimal value) => value >= 0 && value <= InventoryAdjustmentPlanner.MaximumQuantity && decimal.Round(value, 2) == value;
    public static void Fingerprint(string? value)
    { if (value is not { Length: 64 } || value.Any(c => !"0123456789abcdef".Contains(c))) throw new ArgumentException("Reviewed fingerprint required."); }
    public static void Phase(string phase) { if (phase is not ("receive" or "cancel")) throw new ArgumentException("Invalid transfer phase."); }
    public static string? Reason(string phase, string? value)
    {
        if (phase != "cancel") { if (!string.IsNullOrEmpty(value)) throw new ArgumentException("Reason is for cancellation only."); return null; }
        var reason = value?.Trim();
        if (reason is not { Length: > 0 and <= 500 } || reason.Any(char.IsControl)) throw new ArgumentException("Cancellation reason required.");
        return reason;
    }
    public static TransferDispatchCommand Normalize(TransferDispatchCommand command)
    {
        if (command.RequestId == Guid.Empty || command.FromLocationId == Guid.Empty || command.ToLocationId == Guid.Empty
            || command.FromLocationId == command.ToLocationId || command.Items is not { Count: > 0 and <= 100 }
            || command.Items.Any(x => x.BatchId == Guid.Empty || !Exact(x.Quantity) || x.Quantity == 0)
            || command.Items.Select(x => x.BatchId).Distinct().Count() != command.Items.Count
            || command.TransferType is not (null or "store_to_store" or "cs_to_store" or "store_to_production" or "production_to_store")
            || command.Notes?.Length > 2000) throw new ArgumentException("Invalid transfer command.");
        return command with { TransferType = command.TransferType ?? "store_to_store", Notes = command.Notes?.Trim(), Items = command.Items.OrderBy(x => x.BatchId).ToList() };
    }
}
