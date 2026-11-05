using System.Text.Json;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Production;

public sealed class ProductionOrderSyncService : IProductionOrderSyncService
{
    public const string ReadyEventType = "production_request.ready";

    private const string AutoCancelNote = "Скасовано автоматично: потребу в заявках скасовано або зменшено.";

    private readonly IProductionRequestRepository _repo;
    private readonly INotificationRepository _notifications;

    public ProductionOrderSyncService(IProductionRequestRepository repo, INotificationRepository notifications)
    {
        _repo = repo;
        _notifications = notifications;
    }

    public Task SyncAsync(ProductionRequest request, Guid? userId, CancellationToken ct = default) =>
        _repo.ExecuteInTransactionAsync(async () =>
        {
            await SyncCoreAsync(request, userId, ct);
            return 0;
        }, ct);

    private async Task SyncCoreAsync(ProductionRequest request, Guid? userId, CancellationToken ct)
    {
        // A shipped request is history; draft/submitted ones have nothing accepted yet.
        if (request.Status == ProductionRequestStatus.Fulfilled) return;

        var lines = request.Lines.ToList();
        if (lines.Count == 0) return;

        // Serialise concurrent find-or-create for the same (production location, item) across requests.
        await _repo.LockOrderKeysAsync(lines.Select(l => (request.ProductionLocationId, l.ItemId)), ct);

        var allLinks = await _repo.GetLinksForLinesAsync(lines.Select(l => l.Id).ToList(), ct);
        var accepting = request.Status is ProductionRequestStatus.Accepted or ProductionRequestStatus.Partial;

        foreach (var line in lines)
        {
            var desired = accepting ? line.AcceptedQty ?? 0m : 0m;

            // Links to cancelled orders carry no demand — drop them.
            var stale = allLinks.Where(l => l.RequestLineId == line.Id
                                            && l.ProductionOrder?.Status == ProductionOrderStatus.Cancelled).ToList();
            if (stale.Count > 0) _repo.RemoveOrderLinks(stale);

            var lineLinks = allLinks
                .Where(l => l.RequestLineId == line.Id && l.ProductionOrder?.Status != ProductionOrderStatus.Cancelled)
                .ToList();
            var linked = lineLinks.Sum(l => l.Qty);

            if (desired < linked)
                ReduceLinks(lineLinks, linked - desired);
            else if (desired > linked)
                await IncreaseAsync(request, line, lineLinks, desired - linked, userId, ct);

            // Persist per line so the next line's find-or-create sees orders created here.
            await _repo.SaveChangesAsync(ct);
        }
    }

    /// <summary>Cuts qty from Planned-order links only (newest order first); other orders are untouched.</summary>
    private void ReduceLinks(List<ProductionOrderRequestLine> lineLinks, decimal reduce)
    {
        foreach (var link in lineLinks
                     .Where(l => l.ProductionOrder?.Status == ProductionOrderStatus.Planned)
                     .OrderByDescending(l => l.ProductionOrder!.CreatedAt)
                     .ToList())
        {
            if (reduce <= 0) break;
            var order = link.ProductionOrder!;
            var cut = Math.Min(reduce, link.Qty);
            link.Qty -= cut;
            order.PlannedQty = Math.Max(0m, order.PlannedQty - cut);
            order.UpdatedAt = DateTimeOffset.UtcNow;
            reduce -= cut;

            if (link.Qty <= 0)
            {
                _repo.RemoveOrderLinks([link]);
                lineLinks.Remove(link);
            }

            if (order.PlannedQty <= 0)
            {
                order.Status = ProductionOrderStatus.Cancelled;
                order.Notes = string.IsNullOrWhiteSpace(order.Notes) ? AutoCancelNote : $"{order.Notes} | {AutoCancelNote}";
            }
        }
    }

    private async Task IncreaseAsync(
        ProductionRequest request, ProductionRequestLine line, List<ProductionOrderRequestLine> lineLinks,
        decimal need, Guid? userId, CancellationToken ct)
    {
        var recipe = await _repo.GetActiveRecipeForOutputAsync(line.ItemId, ct);
        if (recipe is null) return; // no recipe: stays unassigned (hasRecipe = false), request is still accepted

        var order = await _repo.FindPlannedOrderAsync(
            request.ProductionLocationId, line.ItemId, request.NeededBy, ct);

        if (order is null)
        {
            order = new ProductionOrder
            {
                TenantId = request.TenantId,
                RecipeId = recipe.Id,
                LocationId = request.ProductionLocationId,
                PlannedQty = need,
                Status = ProductionOrderStatus.Planned,
                NeededBy = request.NeededBy,
                PlannedDate = null,
                CreatedBy = userId,
            };
            await _repo.AddOrderAsync(order, ct);
        }
        else
        {
            order.PlannedQty += need;
            order.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var existing = lineLinks.FirstOrDefault(l => l.ProductionOrderId == order.Id);
        if (existing is not null)
        {
            existing.Qty += need;
            return;
        }

        var link = new ProductionOrderRequestLine
        {
            TenantId = request.TenantId,
            ProductionOrderId = order.Id,
            RequestLineId = line.Id,
            Qty = need,
            ProductionOrder = order,
            RequestLine = line,
        };
        await _repo.AddOrderLinksAsync([link], ct);
        lineLinks.Add(link);
    }

    public async Task UnlinkOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var links = await _repo.GetLinksForOrderAsync(orderId, ct);
        if (links.Count > 0) _repo.RemoveOrderLinks(links);
    }

    public async Task NotifyOrderCompletedAsync(Guid orderId, CancellationToken ct = default)
    {
        var orderLinks = await _repo.GetLinksForOrderAsync(orderId, ct);
        var requestIds = orderLinks.Select(l => l.RequestLine?.RequestId ?? Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct().ToList();
        if (requestIds.Count == 0) return;

        var requests = await _repo.GetByIdsAsync(requestIds, ct);
        var links = await _repo.GetLinksForRequestsAsync(requestIds, ct);

        foreach (var request in requests)
        {
            var reqLinks = links.Where(l => l.RequestLine?.RequestId == request.Id).ToList();
            var stage = ProductionRequestStages.Derive(request.Status, request.Lines.ToList(), reqLinks);
            if (stage != ProductionRequestStage.Ready) continue;

            var storeName = request.StoreLocation?.Name ?? "Магазин";
            await _notifications.EnqueueAsync(new NotificationQueue
            {
                TenantId = request.TenantId,
                UserId = null,
                StoreId = request.StoreLocationId,
                Title = $"Виробництво завершено: {storeName} (до {request.NeededBy:dd.MM.yyyy})",
                Channel = "system",
                EventType = ReadyEventType,
                Payload = JsonSerializer.Serialize(new
                {
                    requestId = request.Id,
                    storeLocationId = request.StoreLocationId,
                    storeName,
                    productionLocationId = request.ProductionLocationId,
                    neededBy = request.NeededBy.ToString("yyyy-MM-dd"),
                    orderId,
                }),
                Status = "pending",
            }, ct);
        }
    }
}
