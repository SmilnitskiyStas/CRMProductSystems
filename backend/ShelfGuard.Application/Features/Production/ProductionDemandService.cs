using System.Text.Json;
using ShelfGuard.Application.Features.Orders;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Production;

/// <summary>
/// Stage 3: automatic demand analysis for self-produced items.
///
/// Rules (see ProductionDemandCalculator for the formula):
///  - "Self-produced" = OutputItemId of an active recipe (no Item column).
///  - Target production location = the tenant's active Location of Type "production"; with several,
///    the one used most recently (latest production order), then by name. None -> 422.
///  - Per store, ONE auto draft (source auto_*) per production location: refreshed in place on every
///    run (lines added/updated/removed; auto drafts are fully generator-managed until submitted, so
///    manual edits of an auto draft are overwritten; manual drafts are never touched). If the need
///    disappears the auto draft is cancelled. Unchanged drafts are not rewritten.
///  - Open requests that already count against the need: submitted/accepted/partial (drafts do not,
///    otherwise the generator would subtract its own output).
///  - Source = auto_promo when any line is driven by an event/promo coefficient &gt; 1, else auto_buffer.
///  - A notification (production_request.auto_draft) is enqueued only when a NEW draft is created.
/// </summary>
public sealed class ProductionDemandService : IProductionDemandService
{
    public const string AutoDraftEventType = "production_request.auto_draft";

    private static readonly string[] CountedStatuses =
    [
        ProductionRequestStatus.Submitted, ProductionRequestStatus.Accepted, ProductionRequestStatus.Partial,
    ];

    private readonly IProductionDemandRepository _demand;
    private readonly IProductionRequestRepository _requests;
    private readonly IOrderCalcService _orderCalc;
    private readonly INotificationRepository _notifications;

    public ProductionDemandService(
        IProductionDemandRepository demand,
        IProductionRequestRepository requests,
        IOrderCalcService orderCalc,
        INotificationRepository notifications)
    {
        _demand = demand;
        _requests = requests;
        _orderCalc = orderCalc;
        _notifications = notifications;
    }

    private sealed record Proposal(Guid ItemId, decimal Qty, string Reason, bool PromoDriven);

    public async Task<(List<ProductionRequestDetailDto>? Drafts, string? Error, int? StatusCode)> GenerateAsync(
        Guid tenantId, Guid? userId, Guid? storeLocationId, CancellationToken ct = default)
    {
        var prodLocations = await _demand.GetActiveProductionLocationsAsync(ct);
        if (prodLocations.Count == 0)
            return (null, "Немає активної виробничої локації — створіть локацію типу «виробництво».", 422);
        var prod = prodLocations[0];

        var stores = await _demand.GetActiveStoresAsync(storeLocationId, ct);
        if (storeLocationId.HasValue && stores.Count == 0)
            return (null, "Магазин не знайдено.", 404);

        var selfProduced = await _demand.GetSelfProducedItemIdsAsync(ct);
        if (selfProduced.Count == 0) return ([], null, null);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var neededBy = today.AddDays(ProductionDemandCalculator.DefaultLeadDays);

        // Quantities already requested from production (not yet shipped) per (store, item).
        var open = await _requests.ListAsync(null, null, CountedStatuses, null, null, ct);
        var openQty = open
            .SelectMany(r => r.Lines.Select(l => (r.StoreLocationId, l.ItemId, Qty: l.AcceptedQty ?? l.RequestedQty)))
            .GroupBy(x => (x.StoreLocationId, x.ItemId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

        var drafts = await _requests.ListAsync(null, prod.Id, [ProductionRequestStatus.Draft], null, null, ct);

        var changed = new List<Guid>();
        var created = new List<ProductionRequest>();

        foreach (var store in stores)
        {
            var proposals = await BuildProposalsAsync(store.Id, selfProduced, openQty, today, ct);

            var existing = drafts
                .Where(d => d.StoreLocationId == store.Id && d.Source != ProductionRequestSource.Manual)
                .OrderByDescending(d => d.CreatedAt)
                .FirstOrDefault();

            if (existing is null)
            {
                if (proposals.Count == 0) continue;
                var request = new ProductionRequest
                {
                    TenantId = tenantId,
                    StoreLocationId = store.Id,
                    ProductionLocationId = prod.Id,
                    NeededBy = neededBy,
                    Source = SourceFor(proposals),
                    Status = ProductionRequestStatus.Draft,
                    Notes = "Створено автоматично за аналізом потреби. Перевірте кількості й надішліть на виробництво.",
                    CreatedBy = userId,
                };
                await _requests.AddAsync(request, ct);
                await _requests.AddLinesAsync(proposals.Select(p => NewLine(tenantId, request.Id, p)), ct);
                created.Add(request);
                changed.Add(request.Id);
                continue;
            }

            if (proposals.Count == 0)
            {
                existing.Status = ProductionRequestStatus.Cancelled;
                existing.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            if (await SyncDraftAsync(existing, proposals, tenantId, neededBy, today, ct))
                changed.Add(existing.Id);
        }

        await _requests.SaveChangesAsync(ct);

        foreach (var request in created)
            await EnqueueAutoDraftNotificationAsync(request, stores, ct);

        var result = new List<ProductionRequestDetailDto>();
        foreach (var id in changed)
        {
            var r = await _requests.GetByIdAsync(id, ct);
            if (r is not null) result.Add(ToDetail(r));
        }

        return (result, null, null);
    }

    private async Task<List<Proposal>> BuildProposalsAsync(
        Guid storeId, HashSet<Guid> selfProduced,
        Dictionary<(Guid, Guid), decimal> openQty, DateOnly today, CancellationToken ct)
    {
        var (calc, error) = await _orderCalc.CalculateAsync(storeId, ct);
        if (calc is null || error is not null) return [];

        var lines = calc.Lines.Where(l => selfProduced.Contains(l.ProductId)).ToList();
        if (lines.Count == 0) return [];

        var ids = lines.Select(l => l.ProductId).ToList();
        var onHand = await _demand.GetNonExpiredStockAsync(storeId, ids, today, ct);
        var transfers = await _demand.GetTransferInTransitAsync(storeId, ids, ct);

        var result = new List<Proposal>();
        foreach (var l in lines)
        {
            onHand.TryGetValue(l.ProductId, out var stock);
            transfers.TryGetValue(l.ProductId, out var transferQty);
            openQty.TryGetValue((storeId, l.ProductId), out var openRequests);

            var r = ProductionDemandCalculator.Compute(new ProductionDemandCalculator.Input(
                l.BufferTotal, l.SafetyBuffer, stock, l.InTransit + transferQty, openRequests,
                l.EventCoefficient, l.WeatherCoefficient, l.PromoCoefficient));

            if (r.Need > 0) result.Add(new Proposal(l.ProductId, r.Need, r.Reason, r.PromoDriven));
        }

        return result;
    }

    /// <summary>Applies proposals to an auto draft in place. Returns true when anything changed.</summary>
    private async Task<bool> SyncDraftAsync(
        ProductionRequest draft, List<Proposal> proposals, Guid tenantId, DateOnly neededBy, DateOnly today,
        CancellationToken ct)
    {
        var dirty = false;
        var byItem = proposals.ToDictionary(p => p.ItemId);

        var stale = draft.Lines.Where(l => !byItem.ContainsKey(l.ItemId)).ToList();
        if (stale.Count > 0)
        {
            _requests.RemoveLines(stale);
            dirty = true;
        }

        var have = draft.Lines.Where(l => byItem.ContainsKey(l.ItemId)).ToDictionary(l => l.ItemId);
        var added = new List<ProductionRequestLine>();
        foreach (var p in proposals)
        {
            if (have.TryGetValue(p.ItemId, out var line))
            {
                if (line.RequestedQty != p.Qty || line.AutoReason != p.Reason)
                {
                    line.RequestedQty = p.Qty;
                    line.AutoReason = p.Reason;
                    dirty = true;
                }
            }
            else
            {
                added.Add(NewLine(tenantId, draft.Id, p));
            }
        }

        if (added.Count > 0)
        {
            await _requests.AddLinesAsync(added, ct);
            dirty = true;
        }

        var source = SourceFor(proposals);
        if (draft.Source != source) { draft.Source = source; dirty = true; }
        if (draft.NeededBy < today) { draft.NeededBy = neededBy; dirty = true; }
        if (dirty) draft.UpdatedAt = DateTime.UtcNow;
        return dirty;
    }

    private static string SourceFor(List<Proposal> proposals) =>
        proposals.Any(p => p.PromoDriven) ? ProductionRequestSource.AutoPromo : ProductionRequestSource.AutoBuffer;

    private static ProductionRequestLine NewLine(Guid tenantId, Guid requestId, Proposal p) => new()
    {
        TenantId = tenantId,
        RequestId = requestId,
        ItemId = p.ItemId,
        RequestedQty = p.Qty,
        AutoReason = p.Reason,
    };

    private async Task EnqueueAutoDraftNotificationAsync(
        ProductionRequest request, List<Location> stores, CancellationToken ct)
    {
        var storeName = stores.FirstOrDefault(s => s.Id == request.StoreLocationId)?.Name ?? "Магазин";
        var lineCount = request.Lines.Count;
        var payload = JsonSerializer.Serialize(new
        {
            requestId = request.Id,
            storeLocationId = request.StoreLocationId,
            storeName,
            productionLocationId = request.ProductionLocationId,
            neededBy = request.NeededBy.ToString("yyyy-MM-dd"),
            lineCount,
            source = request.Source,
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId = request.TenantId,
            UserId = null,
            StoreId = request.StoreLocationId,
            Title = $"Авточернетка заявки на виробництво: {storeName} ({lineCount} поз., до {request.NeededBy:dd.MM.yyyy}). " +
                    "Перегляньте та надішліть.",
            Channel = "system",
            EventType = AutoDraftEventType,
            Payload = payload,
            Status = "pending",
        }, ct);
    }

    private static ProductionRequestDetailDto ToDetail(ProductionRequest r) => new(
        r.Id,
        r.StoreLocationId,
        r.StoreLocation?.Name ?? string.Empty,
        r.ProductionLocationId,
        r.ProductionLocation?.Name ?? string.Empty,
        r.NeededBy,
        r.Status,
        r.Source,
        r.Notes,
        r.ReviewNote,
        r.CreatedAt,
        r.SubmittedAt,
        r.ReviewedAt,
        r.ShippedAt,
        r.ShipmentTransferId,
        r.ShipmentTransfer?.Status,
        r.Lines.Select(l => new ProductionRequestLineDto(
            l.Id, l.ItemId, l.Item?.Name ?? string.Empty, l.Item?.Unit ?? string.Empty,
            l.RequestedQty, l.AcceptedQty, l.Reason, l.AutoReason)).ToList());
}
