using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Erp.Posting;

public sealed record WriteOffPostingCommand(string ExpectedFingerprint, string? Reason = null);
public sealed record WriteOffDraftItem(Guid ItemId, decimal Quantity);
public sealed record WriteOffDraftCommand(Guid RequestId, Guid LocationId, string Reason, string? Notes, List<WriteOffDraftItem> Items);
public sealed record WriteOffDraftResult(Guid WriteOffId, Guid LocationId, bool Replayed);
public sealed record WriteOffAllocatedItem(Guid SourceItemId, decimal Quantity, decimal? RetailLoss);
public sealed record WriteOffReviewedBatch(Guid BatchId, Guid ItemId, string Name, string Unit, string? Barcode, string? BatchNumber,
    DateOnly ExpiryDate, Guid? ZoneId, decimal AvailableQuantity, decimal Quantity, decimal? UnitCost, IReadOnlyList<WriteOffAllocatedItem> Allocations,
    string? QualityStatus = null, string? ExactUnitCost = null);
public sealed record WriteOffReviewItem(Guid Id, Guid ProductId, Guid? ProductStockId, decimal Quantity, decimal? UnitPrice, decimal? LossAmount,
    decimal? UnitPricePurchase, decimal? LossAmountPurchase, bool IsReturnedToSupplier, string? ReimbursementType, decimal? ReimbursementValue, decimal? ReimbursementAmount);
public sealed record WriteOffReviewSource(Guid TenantId, Guid Id, Guid LocationId, string Status, string? Reason, string? Notes, string? PdfUrl,
    decimal? TotalLossAmount, decimal? TotalLossAmountPurchase, decimal? TotalReimbursementAmount, Guid? CreatedBy, DateTime CreatedAt,
    Guid? ApprovedBy, DateTime? ApprovedAt, bool HasUnknownRetailAmounts, bool HasUnknownPurchaseAmounts, IReadOnlyList<WriteOffReviewItem> Items);
public sealed record WriteOffPostingPreview(Guid WriteOffId, Guid LocationId, string Phase, string Fingerprint, bool Configured, bool Ready,
    bool CanPost, IReadOnlyList<WriteOffReviewedBatch> Batches, WriteOffReviewSource Source, bool SupportsDesktopReview = true);
public sealed record WriteOffPostingResult(Guid WriteOffId, string Phase, Guid DocumentId, Guid? PostingId, Guid? EventId, bool Replayed);
public sealed record WriteOffPostingOptions(bool Enabled);
public interface IWriteOffPosting
{
    Task<WriteOffDraftResult> CreateDraftAsync(WriteOffDraftCommand command, CancellationToken ct = default);
    Task<WriteOffPostingPreview> PreviewAsync(Guid id, string phase, CancellationToken ct = default);
    Task<WriteOffPostingResult> PostAsync(Guid id, string phase, WriteOffPostingCommand command, CancellationToken ct = default);
}
public interface IWriteOffPostingStore
{
    Task<WriteOffDraftResult> CreateDraftAsync(WorkAccessScope scope, WriteOffDraftCommand command, CancellationToken ct);
    Task<WriteOffPostingPreview> PreviewAsync(WorkAccessScope scope, Guid id, string phase, CancellationToken ct);
    Task<WriteOffPostingResult> PostAsync(WorkAccessScope scope, Guid id, string phase, WriteOffPostingCommand command, CancellationToken ct);
}
public sealed class WriteOffPostingService(IWorkAccessContext access, IWriteOffPostingStore store) : IWriteOffPosting
{
    public async Task<WriteOffDraftResult> CreateDraftAsync(WriteOffDraftCommand command, CancellationToken ct = default) =>
        await store.CreateDraftAsync(await Scope(ct), WriteOffPostingRules.NormalizeDraft(command), ct);
    private async Task<WorkAccessScope> Scope(CancellationToken ct) => await access.GetAsync(ct) ?? throw new WorkAccessDeniedException();
    public async Task<WriteOffPostingPreview> PreviewAsync(Guid id, string phase, CancellationToken ct = default)
    { WriteOffPostingRules.Phase(phase); return await store.PreviewAsync(await Scope(ct), id, phase, ct); }
    public async Task<WriteOffPostingResult> PostAsync(Guid id, string phase, WriteOffPostingCommand command, CancellationToken ct = default)
    {
        WriteOffPostingRules.Phase(phase); TransferPostingRules.Fingerprint(command.ExpectedFingerprint);
        return await store.PostAsync(await Scope(ct), id, phase, command with { Reason = WriteOffPostingRules.Reason(phase, command.Reason) }, ct);
    }
}
public static class WriteOffPostingRules
{
    public static WriteOffDraftCommand NormalizeDraft(WriteOffDraftCommand c)
    {
        if (c.RequestId == Guid.Empty || c.LocationId == Guid.Empty || c.Items is not { Count: >= 1 and <= 100 }
            || c.Items.Any(i => i.ItemId == Guid.Empty || !TransferPostingRules.Exact(i.Quantity) || i.Quantity == 0)
            || c.Items.Select(i => i.ItemId).Distinct().Count() != c.Items.Count
            || c.Reason is not ("expired" or "damaged" or "theft" or "production_loss" or "other")) throw new ArgumentException("Invalid write-off draft.");
        var notes = c.Notes?.Trim();
        if (notes?.Length > 100 || notes?.Any(char.IsControl) == true || c.Reason != "expired" && (notes?.Length ?? 0) < 15)
            throw new ArgumentException("Write-off explanation must contain 15 to 100 characters.");
        return c with { Notes = string.IsNullOrEmpty(notes) ? null : notes, Items = c.Items.OrderBy(i => i.ItemId).ToList() };
    }
    public static string Source(string phase) => $"write_off.{phase}.v1";
    public static string Key(Guid id, string phase) => $"{Source(phase)}:{id:N}";
    public static void Phase(string phase) { if (phase is not ("approve" or "reverse" or "reject")) throw new ArgumentException("Invalid write-off phase."); }
    public static string? Reason(string phase, string? reason)
    {
        if (phase == "approve") { if (!string.IsNullOrEmpty(reason)) throw new ArgumentException("Approval uses the reviewed source explanation."); return null; }
        var text = reason?.Trim();
        if (text is not { Length: >= 15 and <= 500 } || text.Any(char.IsControl)) throw new ArgumentException("Explicit review reason of 15 to 500 characters required.");
        return text;
    }
    public static void Validate(WriteOff w)
    {
        if (w.Status != "pending_approval") throw new InventoryPostingConflictException("Only a pending write-off can be posted.");
        if (w.Items.Count is < 1 or > 100 || w.Reason is not ("expired" or "damaged" or "theft" or "production_loss" or "other")
            || w.Notes?.Length > 100 || w.Reason != "expired" && (w.Notes?.Trim().Length ?? 0) < 15)
            throw new ArgumentException("Review the write-off reason and source lines.");
        if (w.Items.Any(x => x.IsReturnedToSupplier)) throw new InventoryPostingConflictException("Supplier returns require their own document flow.");
        if (w.Items.Any(x => !TransferPostingRules.Exact(x.Quantity) || x.Quantity == 0)) throw new ArgumentException("Exact positive stock quantities required.");
        if (w.Items.Any(x => new decimal?[] { x.UnitPrice, x.LossAmount, x.UnitPricePurchase, x.LossAmountPurchase, x.ReimbursementAmount }
            .Any(v => v.HasValue && (v < 0 || v > 9_999_999_999.99m || decimal.Round(v.Value, 2) != v)))) throw new ArgumentException("Invalid source money snapshot.");
        if (!Summary(w.TotalLossAmount,w.Items.Select(i=>i.LossAmount)) || !Summary(w.TotalLossAmountPurchase,w.Items.Select(i=>i.LossAmountPurchase))
            || !Summary(w.TotalReimbursementAmount,w.Items.Select(i=>i.ReimbursementAmount)))
            throw new InventoryPostingConflictException("Stored monetary summary differs from its source lines; correct the draft before posting.");
    }
    private static bool Summary(decimal? total,IEnumerable<decimal?> values)
    { var known=values.Where(v=>v.HasValue).Select(v=>v!.Value).ToArray();return known.Length==0?total is null or 0:total==known.Sum(); }
    /// <summary>First compute the total FEFO take per item, then assign explicit references and remaining source lines.</summary>
    public static IReadOnlyDictionary<Guid, List<WriteOffAllocatedItem>> Allocate(WriteOff w, IReadOnlyList<ProductStock> stocks)
    {
        Validate(w);
        var result = new Dictionary<Guid, List<WriteOffAllocatedItem>>();
        foreach (var group in w.Items.GroupBy(x => x.ProductId).OrderBy(x => x.Key))
        {
            var batches = stocks.Where(s => s.ProductId == group.Key && s.TenantId == w.TenantId && s.StoreId == w.StoreId && s.Quantity > 0)
                .OrderBy(s => s.ExpiryDate).ThenBy(s => s.AddedAt).ThenBy(s => s.Id).ToArray();
            var remaining = group.Sum(x => x.Quantity); var takes = new Dictionary<Guid, decimal>();
            foreach (var batch in batches) { var take = Math.Min(batch.Quantity, remaining); if (take > 0) takes.Add(batch.Id, take); remaining -= take; if (remaining == 0) break; }
            if (remaining != 0) throw new InventoryPostingConflictException("Insufficient stock; no partial write-off is allowed.");
            var assignments = new Dictionary<Guid, List<(Guid Batch, decimal Qty)>>();
            foreach (var line in group.Where(x => x.ProductStockId.HasValue).OrderBy(x => x.Id))
            {
                var id = line.ProductStockId!.Value;
                if (takes.GetValueOrDefault(id) < line.Quantity) throw new InventoryPostingConflictException("Explicit batch conflicts with the full FEFO allocation.");
                takes[id] -= line.Quantity; assignments[line.Id] = [(id, line.Quantity)];
            }
            foreach (var line in group.Where(x => !x.ProductStockId.HasValue).OrderBy(x => x.Id))
            {
                remaining = line.Quantity; var assigned = new List<(Guid, decimal)>();
                foreach (var batch in batches)
                {
                    var take = Math.Min(takes.GetValueOrDefault(batch.Id), remaining);
                    if (take > 0) { assigned.Add((batch.Id, take)); takes[batch.Id] -= take; remaining -= take; }
                    if (remaining == 0) break;
                }
                if (remaining != 0) throw new InventoryPostingConflictException("Incomplete FEFO allocation.");
                assignments[line.Id] = assigned;
            }
            foreach (var line in group.OrderBy(x => x.Id))
            {
                decimal cumulative = 0, previous = 0;
                foreach (var (batch, qty) in assignments[line.Id])
                {
                    cumulative += qty;
                    var target = line.LossAmount.HasValue ? decimal.Round(line.LossAmount.Value * cumulative / line.Quantity, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
                    var allocated = target.HasValue ? target - previous : null;
                    previous = target ?? 0;
                    if (!result.TryGetValue(batch, out var values)) result[batch] = values = [];
                    values.Add(new(line.Id, qty, allocated));
                }
            }
        }
        if (result.Count > 100) throw new InventoryPostingConflictException("Write-off exceeds the bounded batch posting limit.");
        return result;
    }
}
