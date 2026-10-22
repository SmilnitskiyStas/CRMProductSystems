using System.Text.Json;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Receipts.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Receipts;

/// <summary>
/// TASK-755: automatic supplier-order drafts for items that are NOT produced in-house.
///
/// Rules:
///  - Per active non-production location: <see cref="IOrderCalcService"/> lines with QuantityToOrder &gt; 0,
///    minus self-produced items (OutputItemId of active recipes), computed EXCLUDING this job's own
///    untaken auto drafts from "in transit" (otherwise they would hide their own need on re-run).
///    Taken auto drafts and manual drafts DO count as in transit (no double ordering).
///  - Grouped by Item.DefaultSupplierId (null supplier = its own group, like the manual flow);
///    one draft StockReceipt per (store, supplier), Source auto_replenishment, CreatedBy null.
///  - Idempotent refresh: untaken auto drafts are rewritten in place (lines added/updated/removed;
///    cancelled when the need disappeared). Taken drafts and manual receipts are NEVER modified.
///  - `order.auto_draft` outbox event only when a NEW draft is created, once per store per run.
///    (receipt.created is a goods-RECEIVED event and is never raised here.)
/// </summary>
public sealed class AutoReplenishmentService : IAutoReplenishmentService
{
    public const string AutoDraftEventType = "order.auto_draft";
    public const string AutoNotes = "Автозаявка (щоденний розрахунок)";

    private readonly IReceiptRepository _receipts;
    private readonly IProductionDemandRepository _stores;
    private readonly IOrderCalcService _orderCalc;
    private readonly INotificationRepository _notifications;
    private readonly IReceiptService _receiptService;

    public AutoReplenishmentService(
        IReceiptRepository receipts,
        IProductionDemandRepository stores,
        IOrderCalcService orderCalc,
        INotificationRepository notifications,
        IReceiptService receiptService)
    {
        _receipts = receipts;
        _stores = stores;
        _orderCalc = orderCalc;
        _notifications = notifications;
        _receiptService = receiptService;
    }

    private sealed record Proposal(Guid ProductId, decimal Qty, decimal? Price);

    public async Task<(List<ReceiptDto>? Drafts, string? Error, int? StatusCode)> GenerateAsync(
        Guid tenantId, Guid? storeLocationId, CancellationToken ct = default)
    {
        var stores = await _stores.GetActiveStoresAsync(storeLocationId, ct);
        if (storeLocationId.HasValue && stores.Count == 0)
            return (null, "Магазин не знайдено.", 404);
        if (stores.Count == 0)
            return ([], null, null);

        var selfProduced = await _stores.GetSelfProducedItemIdsAsync(ct);

        var changed = new List<Guid>();
        var createdPerStore = new List<(Location Store, List<StockReceipt> Created)>();

        foreach (var store in stores)
        {
            var (calc, error) = await _orderCalc.CalculateAsync(store.Id, ct, excludeAutoUntakenDrafts: true);
            if (calc is null || error is not null) continue;

            var groups = calc.Lines
                .Where(l => l.QuantityToOrder > 0 && !selfProduced.Contains(l.ProductId))
                .GroupBy(l => l.SupplierId ?? Guid.Empty) // Guid.Empty = "without supplier" (Dictionary rejects null keys)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(l => new Proposal(l.ProductId, l.QuantityToOrder, l.PricePurchase)).ToList());

            var existing = await _receipts.GetUntakenAutoDraftsAsync(store.Id, ct);
            var bySupplier = new Dictionary<Guid, StockReceipt>();
            foreach (var d in existing) // newest first; a duplicate (should not happen) is cancelled
            {
                if (bySupplier.TryAdd(d.SupplierId ?? Guid.Empty, d)) continue;
                Cancel(d);
                changed.Add(d.Id);
            }

            var created = new List<StockReceipt>();
            foreach (var (groupKey, proposals) in groups)
            {
                if (bySupplier.Remove(groupKey, out var draft))
                {
                    if (await SyncAsync(draft, proposals, ct))
                        changed.Add(draft.Id);
                    continue;
                }

                var receipt = new StockReceipt
                {
                    TenantId = tenantId,
                    SupplierId = groupKey == Guid.Empty ? null : groupKey,
                    DestinationStoreId = store.Id,
                    ViaCentralStore = false,
                    Status = "draft",
                    Source = ReceiptSource.AutoReplenishment,
                    Notes = AutoNotes,
                    CreatedBy = null,
                };
                foreach (var p in proposals)
                {
                    receipt.Items.Add(new StockReceiptItem
                    {
                        ReceiptId = receipt.Id,
                        ProductId = p.ProductId,
                        QuantityOrdered = p.Qty,
                        PricePurchase = p.Price,
                    });
                }
                await _receipts.AddAsync(receipt, ct); // graph Add: parent + items all Added
                created.Add(receipt);
                changed.Add(receipt.Id);
            }

            // Need disappeared for these suppliers -> cancel the untaken auto draft.
            foreach (var stale in bySupplier.Values)
            {
                Cancel(stale);
                changed.Add(stale.Id);
            }

            if (created.Count > 0) createdPerStore.Add((store, created));
        }

        await _receipts.SaveChangesAsync(ct);

        foreach (var (store, created) in createdPerStore)
            await EnqueueAutoDraftNotificationAsync(store, created, ct);

        var result = new List<ReceiptDto>();
        foreach (var id in changed.Distinct())
        {
            var dto = await _receiptService.GetByIdAsync(id, ct);
            if (dto is not null) result.Add(dto);
        }
        return (result, null, null);
    }

    private void Cancel(StockReceipt draft)
    {
        draft.Status = "cancelled";
        _receipts.Update(draft);
    }

    /// <summary>Applies proposals to an untaken auto draft in place. True when anything changed.</summary>
    private async Task<bool> SyncAsync(StockReceipt draft, List<Proposal> proposals, CancellationToken ct)
    {
        var dirty = false;
        var byProduct = proposals.ToDictionary(p => p.ProductId);

        var stale = draft.Items.Where(i => !byProduct.ContainsKey(i.ProductId)).ToList();
        if (stale.Count > 0)
        {
            _receipts.RemoveItems(stale);
            dirty = true;
        }

        var have = draft.Items.Where(i => byProduct.ContainsKey(i.ProductId)).ToDictionary(i => i.ProductId);
        foreach (var p in proposals)
        {
            if (have.TryGetValue(p.ProductId, out var item))
            {
                if (item.QuantityOrdered != p.Qty || item.PricePurchase != p.Price)
                {
                    item.QuantityOrdered = p.Qty;
                    item.PricePurchase = p.Price;
                    dirty = true;
                }
            }
            else
            {
                await _receipts.AddItemAsync(new StockReceiptItem
                {
                    ReceiptId = draft.Id,
                    ProductId = p.ProductId,
                    QuantityOrdered = p.Qty,
                    PricePurchase = p.Price,
                }, ct);
                dirty = true;
            }
        }
        return dirty;
    }

    private async Task EnqueueAutoDraftNotificationAsync(
        Location store, List<StockReceipt> created, CancellationToken ct)
    {
        var lineCount = created.Sum(r => r.Items.Count);
        var payload = JsonSerializer.Serialize(new
        {
            storeId = store.Id,
            storeName = store.Name,
            draftCount = created.Count,
            lineCount,
            receiptIds = created.Select(r => r.Id).ToList(),
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId = created[0].TenantId,
            UserId = null,
            StoreId = store.Id,
            Title = $"Авточернетки замовлень постачальникам: {store.Name} ({created.Count} чернеток, {lineCount} поз.). " +
                    "Візьміть у роботу, перевірте кількості та підтвердіть.",
            Channel = "system",
            EventType = AutoDraftEventType,
            Payload = payload,
            Status = "pending",
        }, ct);
    }
}
