using System.Text.Json;
using ShelfGuard.Application.Features.Erp.Posting;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Receipts;

public sealed record ReceiptCategoryGroup(string Key, string Name, Guid[] LineIds, Guid? OwnerId = null,
    DateTime? TakenAt = null, string? CompletedFingerprint = null, DateTime? CompletedAt = null,
    Guid? LastRequestId = null, string? LastRequestHash = null, long Revision = 0);
public sealed record ReceiptWorkState(int Version, ReceiptCategoryGroup[] Groups);
public sealed record ReceiptCategoryGroupDto(string Key, string Name, Guid[] LineIds, Guid? OwnerId,
    string? OwnerName, string Fingerprint, bool Ready, DateTime? CompletedAt);
public sealed record ReceiptWorkDto(Guid ReceiptId, Guid LocationId, bool Active, bool Open, ReceiptCategoryGroupDto[] Groups);
public sealed record ReceiptCategorySave(Guid RequestId, string ExpectedFingerprint, List<Dtos.UpdateItemPayload> Items, bool Complete);
public interface IReceiptCategoryWork
{
    Task<ReceiptWorkDto> GetAsync(Guid receiptId, CancellationToken ct);
    Task<ReceiptWorkDto> ChangeAsync(Guid receiptId, string action, string? key, ReceiptCategorySave? save, CancellationToken ct, Guid[]? lineIds = null);
}

public static class ReceiptCategoryWorkRules
{
    public static ReceiptWorkState? Read(StockReceipt r) => r.ReceivingWorkJson == null ? null
        : JsonSerializer.Deserialize<ReceiptWorkState>(r.ReceivingWorkJson) ?? throw new InventoryPostingConflictException("Invalid category work state.");
    public static void Write(StockReceipt r, ReceiptWorkState state) => r.ReceivingWorkJson = JsonSerializer.Serialize(state);
    public static bool Open(StockReceipt r) => r.Status is "draft" or "ordered" or "in_transit";
    public static ReceiptWorkState Create(StockReceipt r)
    {
        if (r.Items.Count is < 1 or > ReceiptPostingRules.MaximumLines) throw new ArgumentException("Накладна повинна містити від 1 до 2000 рядків.");
        return new(1, r.Items.GroupBy(x => x.Product?.Category is { } c && !string.IsNullOrWhiteSpace(c.Name) ? c.Id.ToString() : "other")
            .Select(g => new ReceiptCategoryGroup(g.Key, g.Key == "other" ? "Інше" : g.First().Product!.Category!.Name,
                g.Select(x => x.Id).OrderBy(x => x).ToArray())).OrderBy(g => g.Key == "other").ThenBy(g => g.Name).ToArray());
    }
    // A different category's quantities never invalidate this category's review.
    public static string Fingerprint(StockReceipt r, ReceiptCategoryGroup g)
    {
        var ids = g.LineIds.ToHashSet();
        var subset = new StockReceipt { Id = r.Id, TenantId = r.TenantId, DestinationStoreId = r.DestinationStoreId,
            SupplierId = r.SupplierId, Status = "ordered", ExpectedAt = r.ExpectedAt, Notes = r.Notes,
            ViaCentralStore = r.ViaCentralStore, Source = r.Source };
        foreach (var line in r.Items.Where(x => ids.Contains(x.Id))) subset.Items.Add(line);
        return ReceiptPostingRules.Hash(new { protocol = "receipt.category.v1", g.Key, g.Name, g.LineIds, g.Revision, source = ReceiptPostingRules.Fingerprint(subset) });
    }
    public static bool Partition(StockReceipt r, ReceiptWorkState s) => s.Version == 1 && s.Groups.Length > 0
        && s.Groups.All(g => g.LineIds.Length > 0) && s.Groups.Select(g => g.Key).Distinct().Count() == s.Groups.Length
        && s.Groups.SelectMany(g => g.LineIds).Count() == r.Items.Count
        && s.Groups.SelectMany(g => g.LineIds).ToHashSet().SetEquals(r.Items.Select(x => x.Id));
    public static bool Ready(StockReceipt r, ReceiptWorkState s) => Partition(r, s)
        && s.Groups.All(g => g.OwnerId != null && g.CompletedFingerprint == Fingerprint(r, g));
    public static void EnsureReady(StockReceipt r)
    {
        if (Read(r) is { } s && !Ready(r, s)) throw new InventoryPostingConflictException("Спочатку всі відповідальні мають завершити свої категорії.");
    }
    public static ReceiptWorkDto Describe(StockReceipt r, IReadOnlyDictionary<Guid, string>? names = null)
    {
        var state = Read(r);
        if (state != null && !Partition(r, state)) throw new InventoryPostingConflictException("Склад накладної змінився після розподілу категорій.");
        return new(r.Id, r.DestinationStoreId, state != null, Open(r), state?.Groups.Select(g => {
            var hash = Fingerprint(r, g);
            return new ReceiptCategoryGroupDto(g.Key, g.Name, g.LineIds, g.OwnerId,
                g.OwnerId is Guid owner && names != null ? names.GetValueOrDefault(owner) : null,
                hash, g.OwnerId != null && hash == g.CompletedFingerprint, g.CompletedAt);
        }).ToArray() ?? []);
    }
}
