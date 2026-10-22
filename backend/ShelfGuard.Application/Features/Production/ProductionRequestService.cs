using System.Text.Json;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Application.Features.Transfers;
using ShelfGuard.Application.Features.Transfers.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Production;

/// <summary>
/// Store -> production request workflow. Visibility/tenant isolation is enforced by RLS
/// (tenant_isolation + store_scope on either the store or the production location).
/// </summary>
public sealed class ProductionRequestService : IProductionRequestService
{
    public const string SubmittedEventType = "production_request.submitted";

    private static readonly string[] OpenStatuses =
    [
        ProductionRequestStatus.Submitted, ProductionRequestStatus.Accepted, ProductionRequestStatus.Partial,
    ];

    private readonly IProductionRequestRepository _repo;
    private readonly IProductionRepository _production;
    private readonly INotificationRepository _notifications;
    private readonly ITransferService _transfers;
    private readonly IProductionOrderSyncService _sync;

    public ProductionRequestService(
        IProductionRequestRepository repo,
        IProductionRepository production,
        INotificationRepository notifications,
        ITransferService transfers,
        IProductionOrderSyncService sync)
    {
        _sync = sync;
        _repo = repo;
        _production = production;
        _notifications = notifications;
        _transfers = transfers;
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<List<ProductionRequestListItemDto>> ListAsync(
        Guid? storeLocationId, Guid? productionLocationId, string? status,
        DateOnly? neededFrom, DateOnly? neededTo, CancellationToken ct = default)
    {
        var statuses = ParseStatuses(status);
        var list = await _repo.ListAsync(storeLocationId, productionLocationId, statuses, neededFrom, neededTo, ct);
        var ctx = await LoadContextAsync(list, ct);
        return list.Select(r => ToListItem(r, ctx)).ToList();
    }

    public async Task<ProductionRequestDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _repo.GetByIdAsync(id, ct);
        return r is null ? null : await ToDetailAsync(r, ct);
    }

    // ── Store side ────────────────────────────────────────────────────────────

    public async Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> CreateAsync(
        ProductionRequestCreateDto dto, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var lineError = await ValidateLinesAsync(dto.Lines, ct);
        if (lineError is not null) return (null, lineError.Value.Error, lineError.Value.Code);

        if (dto.NeededBy < DateOnly.FromDateTime(DateTime.UtcNow.Date))
            return (null, "Дата потреби не може бути в минулому.", 400);

        if (dto.StoreLocationId == dto.ProductionLocationId)
            return (null, "Магазин і виробнича локація мають бути різними.", 400);

        var store = await _repo.GetLocationAsync(dto.StoreLocationId, ct);
        if (store is null || !store.IsActive) return (null, "Магазин не знайдено.", 404);
        if (store.Type == "production")
            return (null, "Заявку на виробництво створює магазин, а не виробнича локація.", 422);

        var prod = await _repo.GetLocationAsync(dto.ProductionLocationId, ct);
        if (prod is null || !prod.IsActive) return (null, "Виробничу локацію не знайдено.", 404);
        if (prod.Type != "production")
            return (null, "Заявку можна адресувати лише виробничій локації.", 422);

        var request = new ProductionRequest
        {
            TenantId = tenantId,
            StoreLocationId = dto.StoreLocationId,
            ProductionLocationId = dto.ProductionLocationId,
            NeededBy = dto.NeededBy,
            Notes = dto.Notes,
            Source = ProductionRequestSource.Manual,
            Status = ProductionRequestStatus.Draft,
            CreatedBy = userId,
        };

        await _repo.AddAsync(request, ct);
        await _repo.AddLinesAsync(MergeLines(dto.Lines).Select(l => new ProductionRequestLine
        {
            TenantId = tenantId,
            RequestId = request.Id,
            ItemId = l.ItemId,
            RequestedQty = l.RequestedQty,
        }), ct);
        await _repo.SaveChangesAsync(ct);

        if (dto.Submit)
            return await SubmitAsync(request.Id, ct);

        return (await LoadDetailAsync(request.Id, ct), null, null);
    }

    public async Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> UpdateAsync(
        Guid id, ProductionRequestUpdateDto dto, CancellationToken ct = default)
    {
        var request = await _repo.GetByIdAsync(id, ct);
        if (request is null) return (null, "Заявку не знайдено.", 404);

        if (request.Status != ProductionRequestStatus.Draft)
            return (null, "Редагувати можна лише чернетку заявки.", 409);

        if (dto.NeededBy.HasValue)
        {
            if (dto.NeededBy.Value < DateOnly.FromDateTime(DateTime.UtcNow.Date))
                return (null, "Дата потреби не може бути в минулому.", 400);
            request.NeededBy = dto.NeededBy.Value;
        }

        if (dto.Notes is not null) request.Notes = dto.Notes;

        if (dto.Lines is not null)
        {
            var lineError = await ValidateLinesAsync(dto.Lines, ct);
            if (lineError is not null) return (null, lineError.Value.Error, lineError.Value.Code);

            _repo.RemoveLines(request.Lines.ToList());
            await _repo.AddLinesAsync(MergeLines(dto.Lines).Select(l => new ProductionRequestLine
            {
                TenantId = request.TenantId,
                RequestId = request.Id,
                ItemId = l.ItemId,
                RequestedQty = l.RequestedQty,
            }), ct);
        }

        request.UpdatedAt = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);
        return (await LoadDetailAsync(id, ct), null, null);
    }

    public async Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> SubmitAsync(
        Guid id, CancellationToken ct = default)
    {
        var request = await _repo.GetByIdAsync(id, ct);
        if (request is null) return (null, "Заявку не знайдено.", 404);

        if (request.Status != ProductionRequestStatus.Draft)
            return (null, "Надіслати можна лише чернетку заявки.", 409);

        if (request.Lines.Count == 0)
            return (null, "Заявка не містить позицій.", 400);

        if (request.NeededBy < DateOnly.FromDateTime(DateTime.UtcNow.Date))
            return (null, "Дата потреби вже минула — оновіть її перед відправленням.", 400);

        request.Status = ProductionRequestStatus.Submitted;
        request.SubmittedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        await _repo.SaveChangesAsync(ct);

        await EnqueueSubmittedNotificationAsync(request, ct);

        return (await ToDetailAsync(request, ct), null, null);
    }

    public async Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> CancelAsync(
        Guid id, CancellationToken ct = default)
    {
        var request = await _repo.GetByIdAsync(id, ct);
        if (request is null) return (null, "Заявку не знайдено.", 404);

        if (request.Status is not (ProductionRequestStatus.Draft or ProductionRequestStatus.Submitted))
            return (null, "Скасувати можна лише чернетку або надіслану заявку, яку ще не прийнято виробництвом.", 409);

        await _repo.ExecuteInTransactionAsync(async () =>
        {
            request.Status = ProductionRequestStatus.Cancelled;
            request.UpdatedAt = DateTime.UtcNow;
            await _repo.SaveChangesAsync(ct);
            await _sync.SyncAsync(request, null, ct); // defensive: drops any lingering demand
            return 0;
        }, ct);
        return (await ToDetailAsync(request, ct), null, null);
    }

    // ── Production side ───────────────────────────────────────────────────────

    public async Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> ReviewAsync(
        Guid id, ProductionRequestReviewDto dto, Guid userId, CancellationToken ct = default)
    {
        var request = await _repo.GetByIdAsync(id, ct);
        if (request is null) return (null, "Заявку не знайдено.", 404);

        // A reviewed (accepted/partial) request can be re-reviewed: production changes quantities while the
        // linked orders are still Planned. Shipped / rejected / cancelled requests are final.
        if (request.Status is not (ProductionRequestStatus.Submitted or ProductionRequestStatus.Accepted
                                   or ProductionRequestStatus.Partial))
            return (null, "Розглянути можна лише надіслану або вже прийняту (ще не відвантажену) заявку.", 409);

        var byId = request.Lines.ToDictionary(l => l.Id);
        var reviews = new Dictionary<Guid, ProductionRequestLineReviewDto>();
        foreach (var rv in dto.Lines ?? [])
        {
            if (!byId.ContainsKey(rv.LineId))
                return (null, $"Позицію {rv.LineId} не знайдено в заявці.", 400);
            if (!reviews.TryAdd(rv.LineId, rv))
                return (null, $"Позицію {rv.LineId} вказано більше одного разу.", 400);
        }

        foreach (var line in request.Lines)
        {
            reviews.TryGetValue(line.Id, out var rv);
            var accepted = rv?.AcceptedQty ?? line.RequestedQty;
            if (accepted < 0 || accepted > line.RequestedQty)
                return (null, "Прийнята кількість має бути від 0 до запитаної.", 400);
            if (accepted < line.RequestedQty && string.IsNullOrWhiteSpace(rv?.Reason))
                return (null, $"Вкажіть причину зменшення кількості для «{line.Item?.Name ?? line.ItemId.ToString()}».", 400);
        }

        // One transaction: status/quantities + the order auto-planning commit (or roll back) together.
        await _repo.ExecuteInTransactionAsync(async () =>
        {
            foreach (var line in request.Lines)
            {
                reviews.TryGetValue(line.Id, out var rv);
                line.AcceptedQty = rv?.AcceptedQty ?? line.RequestedQty;
                line.Reason = line.AcceptedQty < line.RequestedQty ? rv!.Reason!.Trim() : null;
            }

            request.Status = ResolveReviewStatus(request.Lines);
            request.ReviewNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            request.ReviewedBy = userId;
            request.ReviewedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;
            await _repo.SaveChangesAsync(ct);
            await _sync.SyncAsync(request, userId, ct);
            return 0;
        }, ct);
        return (await ToDetailAsync(request, ct), null, null);
    }

    public async Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> RejectAsync(
        Guid id, string reason, Guid userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (null, "Вкажіть причину відхилення.", 400);

        var request = await _repo.GetByIdAsync(id, ct);
        if (request is null) return (null, "Заявку не знайдено.", 404);

        if (request.Status != ProductionRequestStatus.Submitted)
            return (null, "Відхилити можна лише надіслану заявку.", 409);

        await _repo.ExecuteInTransactionAsync(async () =>
        {
            foreach (var line in request.Lines)
            {
                line.AcceptedQty = 0;
                line.Reason = reason.Trim();
            }

            request.Status = ProductionRequestStatus.Rejected;
            request.ReviewNote = reason.Trim();
            request.ReviewedBy = userId;
            request.ReviewedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;
            await _repo.SaveChangesAsync(ct);
            await _sync.SyncAsync(request, userId, ct);
            return 0;
        }, ct);
        return (await ToDetailAsync(request, ct), null, null);
    }

    public async Task<List<ProductionRequestSummaryItemDto>> GetSummaryAsync(
        Guid? productionLocationId, string? status, CancellationToken ct = default)
    {
        IReadOnlyCollection<string> statuses = ParseStatuses(status) ?? (IReadOnlyCollection<string>)OpenStatuses;
        var requests = await _repo.ListAsync(null, productionLocationId, statuses, null, null, ct);
        var lines = requests.SelectMany(r => r.Lines.Select(l => (Request: r, Line: l))).ToList();
        if (lines.Count == 0) return [];

        var inOrders = await _repo.GetLineIdsInActiveOrdersAsync(lines.Select(x => x.Line.Id).ToList(), ct);
        var itemIds = lines.Select(x => x.Line.ItemId).Distinct().ToList();
        var withRecipe = await _repo.GetItemIdsWithActiveRecipeAsync(itemIds, ct);

        return lines
            .GroupBy(x => x.Line.ItemId)
            .Select(g => new ProductionRequestSummaryItemDto(
                g.Key,
                g.First().Line.Item?.Name ?? string.Empty,
                g.First().Line.Item?.Unit ?? string.Empty,
                g.Select(x => x.Request.Id).Distinct().Count(),
                g.Sum(x => x.Line.RequestedQty),
                g.Where(x => !inOrders.Contains(x.Line.Id)).Sum(x => x.Line.AcceptedQty ?? x.Line.RequestedQty),
                withRecipe.Contains(g.Key),
                g.Min(x => x.Request.NeededBy)))
            .OrderBy(s => s.EarliestNeededBy)
            .ThenBy(s => s.ItemName)
            .ToList();
    }

    public async Task<(List<ProductionOrderCreatedDto>? Orders, string? Error, int? StatusCode)> CreateOrdersFromRequestsAsync(
        ProductionOrdersFromRequestsDto dto, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var ids = (dto.RequestIds ?? []).Distinct().ToList();
        if (ids.Count == 0) return (null, "Оберіть хоча б одну заявку.", 400);

        var requests = await _repo.GetByIdsAsync(ids, ct);
        if (requests.Count != ids.Count) return (null, "Деякі заявки не знайдено.", 404);

        if (requests.Any(r => r.Status is not (ProductionRequestStatus.Accepted or ProductionRequestStatus.Partial)))
            return (null, "Виробниче замовлення можна створити лише із прийнятих (повністю чи частково) заявок.", 409);

        var locationIds = requests.Select(r => r.ProductionLocationId).Distinct().ToList();
        if (locationIds.Count != 1)
            return (null, "Усі обрані заявки мають бути адресовані одній виробничій локації.", 400);

        var allLines = requests
            .SelectMany(r => r.Lines.Select(l => (Request: r, Line: l)))
            .Where(x => x.Line.AcceptedQty is > 0)
            .ToList();

        var already = await _repo.GetLineIdsInActiveOrdersAsync(allLines.Select(x => x.Line.Id).ToList(), ct);
        var lines = allLines.Where(x => !already.Contains(x.Line.Id)).ToList();
        if (lines.Count == 0)
            return (null, "Для обраних заявок немає позицій, що потребують виробництва (усе вже у замовленнях).", 409);

        // Resolve a recipe per item up-front; create nothing if any item is not made in-house.
        var groups = lines.GroupBy(x => x.Line.ItemId).ToList();
        var recipes = new Dictionary<Guid, Recipe>();
        var missing = new List<string>();
        foreach (var g in groups)
        {
            var recipe = await _repo.GetActiveRecipeForOutputAsync(g.Key, ct);
            if (recipe is null) missing.Add(g.First().Line.Item?.Name ?? g.Key.ToString());
            else recipes[g.Key] = recipe;
        }

        if (missing.Count > 0)
            return (null, $"Немає активного рецепта для: {string.Join(", ", missing)}.", 422);

        var result = new List<ProductionOrderCreatedDto>();
        foreach (var g in groups)
        {
            var recipe = recipes[g.Key];
            var planned = g.Sum(x => x.Line.AcceptedQty!.Value);
            var order = new ProductionOrder
            {
                TenantId = tenantId,
                RecipeId = recipe.Id,
                LocationId = locationIds[0],
                PlannedQty = planned,
                Status = ProductionOrderStatus.Planned,
                NeededBy = g.Min(x => x.Request.NeededBy),
                PlannedDate = dto.PlannedDate,
                Notes = dto.Notes,
                CreatedBy = userId,
            };
            await _production.AddOrderAsync(order, ct);
            await _repo.AddOrderLinksAsync(g.Select(x => new ProductionOrderRequestLine
            {
                TenantId = tenantId,
                ProductionOrderId = order.Id,
                RequestLineId = x.Line.Id,
                Qty = x.Line.AcceptedQty!.Value,
            }), ct);

            result.Add(new ProductionOrderCreatedDto(
                order.Id, g.Key, g.First().Line.Item?.Name ?? string.Empty,
                recipe.Name, planned, g.Count()));
        }

        await _repo.SaveChangesAsync(ct);
        return (result, null, null);
    }

    public async Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> ShipAsync(
        Guid id, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var request = await _repo.GetByIdAsync(id, ct);
        if (request is null) return (null, "Заявку не знайдено.", 404);

        if (request.Status is not (ProductionRequestStatus.Accepted or ProductionRequestStatus.Partial))
            return (null, "Відвантажити можна лише прийняту (повністю чи частково) заявку.", 409);

        // Orders exist for this request -> they must all be finished first. No linked orders at all
        // (shipping from existing stock) keeps the old behaviour.
        var links = await _repo.GetLinksForRequestsAsync([request.Id], ct);
        if (!ProductionRequestStages.AllOrdersDone(links))
            return (null, "Виробництво ще не завершене.", 409);

        var shipLines = request.Lines.Where(l => l.AcceptedQty is > 0).ToList();
        if (shipLines.Count == 0)
            return (null, "У заявці немає прийнятих кількостей для відвантаження.", 409);

        // FEFO plan from the production location; expired batches are never shipped.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = new List<CreateTransferItemRequest>();
        foreach (var line in shipLines)
        {
            var batches = (await _production.GetFefoOrderedAsync(line.ItemId, request.ProductionLocationId, ct))
                .Where(b => b.ExpiryDate >= today)
                .OrderBy(b => b.ExpiryDate)
                .ToList();

            var remaining = line.AcceptedQty!.Value;
            foreach (var batch in batches)
            {
                if (remaining <= 0) break;
                var take = Math.Min(remaining, batch.Quantity);
                items.Add(new CreateTransferItemRequest(batch.Id, take));
                remaining -= take;
            }

            if (remaining > 0)
                return (null,
                    $"Недостатньо залишків на виробництві для «{line.Item?.Name ?? line.ItemId.ToString()}»: " +
                    $"бракує {remaining}.", 422);
        }

        // Status flips before the transfer call so the transfer's own SaveChanges (same scoped
        // DbContext) commits both atomically — a retry can never double-ship.
        var originalStatus = request.Status;
        request.Status = ProductionRequestStatus.Fulfilled;
        request.ShippedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        var shortId = request.Id.ToString("N")[..8].ToUpperInvariant();
        var (transfer, error) = await _transfers.CreateAsync(
            tenantId, userId,
            new CreateTransferRequest(
                request.ProductionLocationId, request.StoreLocationId, "production_to_store",
                $"Заявка на виробництво #{shortId}", items),
            ct);

        if (error is not null || transfer is null)
        {
            request.Status = originalStatus;
            request.ShippedAt = null;
            return (null, error ?? "Не вдалося створити переміщення.", 422);
        }

        request.ShipmentTransferId = transfer.Id;
        await _repo.SaveChangesAsync(ct);
        return (await LoadDetailAsync(id, ct), null, null);
    }

    public async Task<(ProductionRequestDetailDto? Request, string? Error, int? StatusCode)> SyncOrdersAsync(
        Guid id, Guid userId, CancellationToken ct = default)
    {
        var request = await _repo.GetByIdAsync(id, ct);
        if (request is null) return (null, "Заявку не знайдено.", 404);

        if (request.Status is not (ProductionRequestStatus.Accepted or ProductionRequestStatus.Partial
                                   or ProductionRequestStatus.Rejected or ProductionRequestStatus.Cancelled))
            return (null, "Синхронізація виробничих замовлень доступна лише для розглянутих заявок.", 409);

        await _sync.SyncAsync(request, userId, ct);
        return (await ToDetailAsync(request, ct), null, null);
    }

    public async Task<(List<ProductionShipRequestResultDto>? Results, string? Error, int? StatusCode)> ShipOrderRequestsAsync(
        Guid orderId, Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var order = await _production.GetOrderByIdAsync(orderId, ct);
        if (order is null) return (null, "Виробниче замовлення не знайдено.", 404);
        if (order.Status != ProductionOrderStatus.Done)
            return (null, "Відвантажувати заявки можна лише із завершеного виробничого замовлення.", 409);

        var orderLinks = await _repo.GetLinksForOrderAsync(orderId, ct);
        var requestIds = orderLinks.Select(l => l.RequestLine?.RequestId ?? Guid.Empty)
            .Where(x => x != Guid.Empty).Distinct().ToList();

        var results = new List<ProductionShipRequestResultDto>();
        foreach (var requestId in requestIds)
        {
            var request = await _repo.GetByIdAsync(requestId, ct);
            if (request is null) continue;
            // Already shipped / rejected / cancelled requests are not part of this batch.
            if (request.Status is not (ProductionRequestStatus.Accepted or ProductionRequestStatus.Partial)) continue;

            var storeName = request.StoreLocation?.Name ?? string.Empty;
            try
            {
                var links = await _repo.GetLinksForRequestsAsync([requestId], ct);
                if (!ProductionRequestStages.AllOrdersDone(links))
                {
                    results.Add(new(requestId, storeName, false, "Очікує завершення інших виробничих замовлень.", null));
                    continue;
                }

                var (shipped, error, _) = await ShipAsync(requestId, tenantId, userId, ct);
                results.Add(error is null
                    ? new(requestId, storeName, true, null, shipped?.ShipmentTransferId)
                    : new(requestId, storeName, false, error, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new(requestId, storeName, false, ex.Message, null));
            }
        }

        return (results, null, null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<ProductionRequestDetailDto?> LoadDetailAsync(Guid id, CancellationToken ct)
    {
        var r = await _repo.GetByIdAsync(id, ct);
        return r is null ? null : await ToDetailAsync(r, ct);
    }

    /// <summary>Order links + recipe availability needed to derive stage / hasRecipe / linkedOrders.</summary>
    private sealed record StageContext(
        ILookup<Guid, ProductionOrderRequestLine> LinksByRequest,
        HashSet<Guid> ItemsWithRecipe);

    private async Task<StageContext> LoadContextAsync(IReadOnlyCollection<ProductionRequest> requests, CancellationToken ct)
    {
        if (requests.Count == 0)
            return new StageContext(Array.Empty<ProductionOrderRequestLine>().ToLookup(l => l.Id), []);

        var links = await _repo.GetLinksForRequestsAsync(requests.Select(r => r.Id).ToList(), ct);
        var itemIds = requests.SelectMany(r => r.Lines).Select(l => l.ItemId).Distinct().ToList();
        var withRecipe = await _repo.GetItemIdsWithActiveRecipeAsync(itemIds, ct);
        return new StageContext(links.ToLookup(l => l.RequestLine!.RequestId), withRecipe);
    }

    private async Task<ProductionRequestDetailDto> ToDetailAsync(ProductionRequest r, CancellationToken ct) =>
        ToDetail(r, await LoadContextAsync([r], ct));

    private async Task<(string Error, int Code)?> ValidateLinesAsync(
        List<ProductionRequestLineCreateDto>? lines, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
            return ("Заявка має містити хоча б одну позицію.", 400);
        if (lines.Any(l => l.RequestedQty <= 0))
            return ("Кількість у кожній позиції має бути більшою за 0.", 400);

        foreach (var itemId in lines.Select(l => l.ItemId).Distinct())
        {
            var item = await _production.GetItemByIdAsync(itemId, ct);
            if (item is null) return ($"Товар {itemId} не знайдено.", 404);
        }

        return null;
    }

    private static List<ProductionRequestLineCreateDto> MergeLines(List<ProductionRequestLineCreateDto> lines) =>
        lines.GroupBy(l => l.ItemId)
             .Select(g => new ProductionRequestLineCreateDto(g.Key, g.Sum(x => x.RequestedQty)))
             .ToList();

    private static string ResolveReviewStatus(IEnumerable<ProductionRequestLine> lines)
    {
        var list = lines.ToList();
        if (list.All(l => l.AcceptedQty == 0)) return ProductionRequestStatus.Rejected;
        if (list.All(l => l.AcceptedQty == l.RequestedQty)) return ProductionRequestStatus.Accepted;
        return ProductionRequestStatus.Partial;
    }

    private static List<string>? ParseStatuses(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var parts = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Where(ProductionRequestStatus.All.Contains)
            .ToList();
        // An unrecognised filter must match nothing rather than silently widen to everything.
        return parts.Count > 0 ? parts : ["__none__"];
    }

    private async Task EnqueueSubmittedNotificationAsync(ProductionRequest request, CancellationToken ct)
    {
        var storeName = request.StoreLocation?.Name ?? "Магазин";
        var payload = JsonSerializer.Serialize(new
        {
            requestId = request.Id,
            storeLocationId = request.StoreLocationId,
            storeName,
            productionLocationId = request.ProductionLocationId,
            neededBy = request.NeededBy.ToString("yyyy-MM-dd"),
            lineCount = request.Lines.Count,
        });

        await _notifications.EnqueueAsync(new NotificationQueue
        {
            TenantId = request.TenantId,
            UserId = null,
            StoreId = request.ProductionLocationId,
            Title = $"Нова заявка на виробництво: {storeName} (до {request.NeededBy:dd.MM.yyyy})",
            Channel = "system",
            EventType = SubmittedEventType,
            Payload = payload,
            Status = "pending",
        }, ct);
    }

    private static ProductionRequestListItemDto ToListItem(ProductionRequest r, StageContext ctx) => new(
        r.Id,
        r.StoreLocationId,
        r.StoreLocation?.Name ?? string.Empty,
        r.ProductionLocationId,
        r.ProductionLocation?.Name ?? string.Empty,
        r.NeededBy,
        r.Status,
        r.Source,
        r.Lines.Count,
        r.Lines.Sum(l => l.RequestedQty),
        r.Lines.Any(l => l.AcceptedQty.HasValue) ? r.Lines.Sum(l => l.AcceptedQty ?? 0) : null,
        r.CreatedAt,
        r.SubmittedAt,
        ProductionRequestStages.Derive(r.Status, r.Lines.ToList(), ctx.LinksByRequest[r.Id].ToList()));

    private static ProductionRequestDetailDto ToDetail(ProductionRequest r, StageContext ctx)
    {
        var links = ctx.LinksByRequest[r.Id].ToList();
        return new ProductionRequestDetailDto(
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
                l.RequestedQty, l.AcceptedQty, l.Reason, l.AutoReason,
                ctx.ItemsWithRecipe.Contains(l.ItemId),
                links.Where(k => k.RequestLineId == l.Id && k.ProductionOrder is { Status: not ProductionOrderStatus.Cancelled })
                     .Select(k => new ProductionRequestLinkedOrderDto(
                         k.ProductionOrderId, k.ProductionOrder!.Status.ToApiString(), k.Qty))
                     .ToList())).ToList(),
            ProductionRequestStages.Derive(r.Status, r.Lines.ToList(), links));
    }
}
