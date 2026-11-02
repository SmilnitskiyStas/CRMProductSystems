using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Application.Features.Erp.Posting;

public sealed record ReceiptReceivingContext(Guid ReceiptId, Guid LocationId, string Fingerprint, bool Configured, bool Fenced, bool CanReceive,
    bool SupportsReviewedEdits = true, int MaxReceiptLines = ReceiptPostingRules.MaximumLines);
public sealed record ReceiptDesktopSummary(Guid Id, Guid DestinationStoreId, string? SupplierName, string Status, DateOnly? ExpectedAt, int ItemCount);
public sealed record ReceiptPostingResult(Guid ReceiptId, Guid DocumentId, Guid PostingId, Guid EventId, bool Replayed);
public sealed record ReceiptPostingOptions(bool Enabled);
public interface IReceiptPosting
{
    Task<ProductionDesktopPage<ReceiptDesktopSummary>> ListAsync(Guid locationId, int page, string search, string status, CancellationToken ct = default);
    Task<ReceiptReceivingContext> PreviewAsync(Guid receiptId, CancellationToken ct = default);
    Task<ReceiptPostingResult> ReceiveAsync(Guid receiptId, string fingerprint, CancellationToken ct = default);
}
public interface IReceiptPostingStore
{
    Task<ProductionDesktopPage<ReceiptDesktopSummary>> ListAsync(WorkAccessScope scope, Guid locationId, int page, string search, string status, CancellationToken ct);
    Task<ReceiptReceivingContext> PreviewAsync(WorkAccessScope scope, Guid receiptId, CancellationToken ct);
    Task<ReceiptPostingResult> ReceiveAsync(WorkAccessScope scope, Guid receiptId, string fingerprint, CancellationToken ct);
}

public sealed class ReceiptPostingService(IWorkAccessContext access, IReceiptPostingStore store) : IReceiptPosting
{
    private async Task<WorkAccessScope> Scope(CancellationToken ct)
    {
        var scope = await access.GetAsync(ct) ?? throw new WorkAccessDeniedException();
        if (!ReceiptPostingRules.CanReceive(scope.Role)) throw new WorkAccessDeniedException();
        return scope;
    }
    public async Task<ProductionDesktopPage<ReceiptDesktopSummary>> ListAsync(Guid locationId, int page, string search, string status, CancellationToken ct = default) =>
        await store.ListAsync(await Scope(ct), locationId, page, search, status, ct);
    public async Task<ReceiptReceivingContext> PreviewAsync(Guid receiptId, CancellationToken ct = default) =>
        await store.PreviewAsync(await Scope(ct), receiptId, ct);
    public async Task<ReceiptPostingResult> ReceiveAsync(Guid receiptId, string fingerprint, CancellationToken ct = default)
    {
        if (receiptId == Guid.Empty || fingerprint is not { Length: 64 } || fingerprint.Any(c => !"0123456789abcdef".Contains(c)))
            throw new ArgumentException("A receipt and its reviewed fingerprint are required.");
        return await store.ReceiveAsync(await Scope(ct), receiptId, fingerprint, ct);
    }
}

public static class ReceiptPostingRules
{
    public const int MaximumLines = 2000;
    public const string Source = "receipt.receive.v1";
    public static string Key(Guid receiptId) => $"{Source}:{receiptId:N}";
    public static bool CanReceive(string role) => role is "enterprise_admin" or "network_manager" or "store_manager" or "storekeeper";
    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
    private static string? Number(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture);
    // Semantic snapshot of everything the receiver is about to accept, ordered independently of EF collection order.
    public static string Fingerprint(StockReceipt r) => Hash(new { protocol = Source, r.TenantId, r.Id, r.SupplierId,
        r.DestinationStoreId, r.ViaCentralStore, r.Status, r.ExpectedAt, r.Notes, r.Source, r.TakenByUserId,
        items = r.Items.OrderBy(x => x.Id).Select(x => new { x.Id, x.ProductId, ordered = Number(x.QuantityOrdered),
            received = Number(x.QuantityReceived), price = Number(x.PricePurchase), x.ExpiryDate, x.BatchNumber, x.DiscrepancyNotes,
            name = x.Product?.Name, unit = x.Product?.Unit, active = x.Product?.IsActive, barcode = x.Product?.Barcodes.FirstOrDefault() }).ToArray() });
    public static void Validate(StockReceipt r, Guid actor)
    {
        if (r.Status is not ("draft" or "ordered" or "in_transit")) throw new InventoryPostingConflictException("Receipt is no longer open.");
        if (r.ReceivingWorkJson == null && r.Status == "draft" && r.Source == ReceiptSource.AutoReplenishment && r.TakenByUserId is Guid holder && holder != actor)
            throw new InventoryPostingConflictException("Receipt is held by another operator.");
        if (r.Items.Count is < 1 or > MaximumLines) throw new ArgumentException($"Receipt must contain 1 to {MaximumLines} lines.");
        foreach (var line in r.Items)
        {
            var qty = line.QuantityReceived ?? line.QuantityOrdered;
            if (qty < 0 || qty > InventoryAdjustmentPlanner.MaximumQuantity || decimal.Round(qty, 2) != qty || line.ExpiryDate == null
                || line.BatchNumber?.Length > 100 || line.Product is not { IsActive: true } item || item.TenantId != r.TenantId
                || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Unit) || item.Unit.Length > 24
                || item.Barcodes.FirstOrDefault()?.Length > 128)
                throw new ArgumentException("Invalid receipt quantity, item or batch snapshot.");
            if (line.PricePurchase is decimal price && (price < 0 || price > 9_999_999_999.99m || decimal.Round(price, 2) != price
                || qty * price > 9_999_999_999.99m))
                throw new ArgumentException("Receipt value must fit legacy money columns.");
        }
        if (!r.Items.Any(x => (x.QuantityReceived ?? x.QuantityOrdered) > 0)) throw new ArgumentException("A receipt requires a positive received line.");
    }
}
