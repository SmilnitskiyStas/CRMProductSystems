using System.Globalization;
using ShelfGuard.Application.Features.Production.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Production;

/// <summary>
/// TASK-763: read-only aggregates behind the production overview, plan/MRP, shipments and warehouse pages.
///
/// Conventions:
///  - Location scope: an explicit <c>locationId</c> narrows to that production location; otherwise ALL active
///    locations of Type "production" of the tenant. No production location => empty result.
///  - "On hand" = non-expired batches (ExpiryDate &gt;= today) with Quantity &gt; 0 at the scoped locations.
///  - Expiring soon = Quantity &gt; 0 and ExpiryDate &lt;= today + 7 (already expired batches included, they need action).
///  - Item kind: "finished" = output of an active recipe (wins when also an ingredient), "raw" = ingredient of an
///    active recipe, otherwise "other".
///  - Shortages (overview) = MRP over Planned + InProgress orders only (no extra forecast demand).
/// </summary>
public sealed class ProductionInsightsService : IProductionInsightsService
{
    public const int ExpiringSoonDays = 7;
    public const int MaxPlanDays = 92;
    private const int TopN = 5;

    private static readonly string[] OpenRequestStatuses =
    [
        ProductionRequestStatus.Submitted, ProductionRequestStatus.Accepted, ProductionRequestStatus.Partial,
    ];

    private static readonly ProductionOrderStatus[] OpenOrderStatuses =
        [ProductionOrderStatus.Planned, ProductionOrderStatus.InProgress];

    private readonly IProductionInsightsRepository _repo;
    private readonly TimeProvider _clock;

    public ProductionInsightsService(IProductionInsightsRepository repo, TimeProvider? clock = null)
    {
        _repo = repo;
        _clock = clock ?? TimeProvider.System;
    }

    private DateOnly Today => DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);

    private async Task<List<Guid>> ScopeAsync(Guid? locationId, CancellationToken ct) =>
        (await _repo.GetProductionLocationsAsync(locationId, ct)).Select(l => l.Id).ToList();

    private static string Kind(Guid itemId, HashSet<Guid> outputs, HashSet<Guid> ingredients) =>
        outputs.Contains(itemId) ? "finished" : ingredients.Contains(itemId) ? "raw" : "other";

    // ── Overview ─────────────────────────────────────────────────────────────

    public async Task<ProductionOverviewDto> GetOverviewAsync(Guid? locationId, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(locationId, ct);
        if (scope.Count == 0)
            return new ProductionOverviewDto(0, 0, 0, 0, 0, 0, 0, [], [], [], [], []);

        var today = Today;
        var requests = await _repo.GetRequestsAsync(scope, OpenRequestStatuses, ct: ct);
        var linkedIds = requests.Where(r => r.Status != ProductionRequestStatus.Submitted).Select(r => r.Id).ToList();
        var links = linkedIds.Count == 0 ? [] : await _repo.GetLinksForRequestsAsync(linkedIds, ct);
        var orders = await _repo.GetOrdersAsync(scope, OpenOrderStatuses, ct);
        var stock = await _repo.GetStockAsync(scope, ct);
        var recipes = await _repo.GetActiveRecipesAsync(ct);

        var submitted = requests.Where(r => r.Status == ProductionRequestStatus.Submitted).ToList();
        var stages = StagesOf(requests, links);
        var ready = requests.Where(r => stages[r.Id] == ProductionRequestStage.Ready).ToList();
        var awaiting = requests.Count(r => stages[r.Id] == ProductionRequestStage.AwaitingOrder);

        var newRequests = submitted
            .OrderBy(r => r.SubmittedAt ?? r.CreatedAt).ThenBy(r => r.Id)
            .Take(TopN)
            .Select(r => new OverviewNewRequestDto(
                r.Id, r.StoreLocation?.Name ?? "—", r.NeededBy, r.Lines.Sum(l => l.RequestedQty)))
            .ToList();

        var inProgress = orders.Where(o => o.Status == ProductionOrderStatus.InProgress).ToList();
        var inProgressList = inProgress
            .OrderBy(o => o.StartedAt ?? DateTimeOffset.MaxValue).ThenBy(o => o.Id)
            .Take(TopN)
            .Select(o => new OverviewOrderDto(o.Id, o.Recipe?.Name ?? "—", o.PlannedQty, o.StartedAt))
            .ToList();

        var readyList = ready
            .OrderBy(r => r.NeededBy).ThenBy(r => r.Id)
            .Take(TopN)
            .Select(r => new OverviewReadyDto(r.Id, r.StoreLocation?.Name ?? "—", AcceptedTotal(r)))
            .ToList();

        var outputs = recipes.Select(r => r.OutputItemId).ToHashSet();
        var ingredients = recipes.SelectMany(r => r.Ingredients.Select(i => i.ItemId)).ToHashSet();

        var limit = today.AddDays(ExpiringSoonDays);
        var expiringAll = stock.Where(s => s.ExpiryDate <= limit).OrderBy(s => s.ExpiryDate).ThenBy(s => s.Id).ToList();
        var expiring = expiringAll.Take(TopN).Select(s => new OverviewExpiringDto(
            s.Product?.Name ?? "—", s.Quantity, s.Product?.Unit ?? "", s.ExpiryDate,
            s.Store?.Name ?? "—", Kind(s.ProductId, outputs, ingredients))).ToList();

        var onHand = OnHand(stock, today);
        var required = new Dictionary<Guid, decimal>();
        var meta = new Dictionary<Guid, Item>();
        foreach (var o in orders) AddRecipeNeed(required, meta, o.Recipe, o.PlannedQty);
        var shortages = BuildShortages(required, meta, onHand);

        return new ProductionOverviewDto(
            IncomingRequests: submitted.Count,
            AwaitingOrder: awaiting,
            OrdersPlanned: orders.Count(o => o.Status == ProductionOrderStatus.Planned),
            OrdersInProgress: inProgress.Count,
            ReadyToShip: ready.Count,
            Shortages: shortages.Count,
            ExpiringSoon: expiringAll.Count,
            NewRequests: newRequests,
            InProgressOrders: inProgressList,
            ReadyToShipList: readyList,
            Expiring: expiring,
            ShortageList: shortages.Take(TopN)
                .Select(s => new OverviewShortageDto(s.ItemId, s.ItemName, s.Unit, s.Required, s.OnHand, s.Missing))
                .ToList());
    }

    // ── Plan / MRP ───────────────────────────────────────────────────────────

    /// <summary>
    /// Definitions (per product, per bucket; demand is bucketed by the request's NeededBy, overdue demand
    /// before <c>from</c> folds into the first bucket, demand after <c>to</c> is ignored):
    ///  requestedQty = Σ AcceptedQty of accepted/partial requests (still unfulfilled);
    ///  pendingQty   = Σ RequestedQty of submitted requests (awaiting review);
    ///  forecastQty  = Σ RequestedQty of DRAFT requests with Source auto_buffer/auto_promo (system forecast, TASK-752).
    /// finishedOnHand = non-expired stock of the item at the scoped locations;
    /// openOrderQty = Σ PlannedQty of Planned+InProgress orders of the item (already-planned production, any date).
    /// toProduce = max(0, totalRequested + totalForecast − finishedOnHand − openOrderQty). Pending (unreviewed)
    /// requests are shown but not counted: they may be rejected, and once accepted they enter "requested" and
    /// get their own order (which then lowers toProduce through openOrderQty — no double count).
    /// MRP: required(ingredient) = Σ over open orders (order's own recipe) of PlannedQty × qty/outputQty
    ///    + Σ over products with an active recipe of toProduce × qty/outputQty.
    /// </summary>
    public async Task<ProductionPlanDto> GetPlanAsync(
        DateOnly? from, DateOnly? to, string? granularity, Guid? locationId, CancellationToken ct = default)
    {
        var today = Today;
        var gran = granularity?.ToLowerInvariant() switch { "week" => "week", "month" => "month", _ => "day" };
        var start = from ?? today;
        var end = to ?? start.AddDays(13);
        if (end < start) end = start;
        if (end.DayNumber - start.DayNumber + 1 > MaxPlanDays) end = start.AddDays(MaxPlanDays - 1);

        var buckets = BuildBuckets(start, end, gran);
        var scope = await ScopeAsync(locationId, ct);
        if (scope.Count == 0)
            return new ProductionPlanDto(start, end, gran, buckets, [], []);

        var requests = await _repo.GetRequestsAsync(scope,
            [.. OpenRequestStatuses, ProductionRequestStatus.Draft], ct: ct);
        var orders = await _repo.GetOrdersAsync(scope, OpenOrderStatuses, ct);
        var stock = await _repo.GetStockAsync(scope, ct);
        var recipes = await _repo.GetActiveRecipesAsync(ct);

        // Newest active recipe per output item (repo returns newest first).
        var recipeByOutput = new Dictionary<Guid, Recipe>();
        foreach (var r in recipes) recipeByOutput.TryAdd(r.OutputItemId, r);

        var demand = new Dictionary<Guid, Dictionary<string, decimal[]>>(); // item -> bucket -> [req, pend, fc]
        var itemMeta = new Dictionary<Guid, Item>();
        void Add(Guid itemId, Item? item, DateOnly neededBy, int slot, decimal qty)
        {
            if (qty <= 0 || neededBy > end) return;
            var key = BucketKey(neededBy < start ? start : neededBy, gran);
            if (!demand.TryGetValue(itemId, out var perBucket)) demand[itemId] = perBucket = [];
            if (!perBucket.TryGetValue(key, out var arr)) perBucket[key] = arr = new decimal[3];
            arr[slot] += qty;
            if (item != null) itemMeta[itemId] = item;
        }

        foreach (var r in requests)
        {
            foreach (var l in r.Lines)
            {
                if (r.Status is ProductionRequestStatus.Accepted or ProductionRequestStatus.Partial)
                    Add(l.ItemId, l.Item, r.NeededBy, 0, l.AcceptedQty ?? 0m);
                else if (r.Status == ProductionRequestStatus.Submitted)
                    Add(l.ItemId, l.Item, r.NeededBy, 1, l.RequestedQty);
                else if (r.Status == ProductionRequestStatus.Draft
                         && r.Source is ProductionRequestSource.AutoBuffer or ProductionRequestSource.AutoPromo)
                    Add(l.ItemId, l.Item, r.NeededBy, 2, l.RequestedQty);
            }
        }

        var openByItem = new Dictionary<Guid, decimal>();
        foreach (var o in orders.Where(o => o.Recipe != null))
        {
            var id = o.Recipe!.OutputItemId;
            openByItem[id] = openByItem.GetValueOrDefault(id) + o.PlannedQty;
            if (o.Recipe.OutputItem != null) itemMeta[id] = o.Recipe.OutputItem;
        }

        var onHand = OnHand(stock, today);
        var required = new Dictionary<Guid, decimal>();
        var ingMeta = new Dictionary<Guid, Item>();
        foreach (var o in orders) AddRecipeNeed(required, ingMeta, o.Recipe, o.PlannedQty);

        var products = new List<PlanProductDto>();
        foreach (var itemId in demand.Keys.Union(openByItem.Keys))
        {
            var perBucket = demand.GetValueOrDefault(itemId) ?? [];
            var rows = buckets.Select(b =>
            {
                var a = perBucket.GetValueOrDefault(b.Key) ?? new decimal[3];
                return new PlanBucketQtyDto(b.Key, a[0], a[1], a[2]);
            }).ToList();
            var totalReq = rows.Sum(x => x.RequestedQty);
            var totalPend = rows.Sum(x => x.PendingQty);
            var totalFc = rows.Sum(x => x.ForecastQty);
            var finished = onHand.GetValueOrDefault(itemId);
            var open = openByItem.GetValueOrDefault(itemId);
            var toProduce = Math.Max(0m, totalReq + totalFc - finished - open);
            var hasRecipe = recipeByOutput.TryGetValue(itemId, out var recipe);
            if (hasRecipe) AddRecipeNeed(required, ingMeta, recipe, toProduce);
            itemMeta.TryGetValue(itemId, out var item);
            products.Add(new PlanProductDto(
                itemId, item?.Name ?? "—", item?.Unit ?? "", hasRecipe, rows,
                totalReq, totalPend, totalFc, finished, open, toProduce));
        }
        products = products
            .OrderByDescending(p => p.ToProduce).ThenBy(p => p.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var withSupplier = required.Count == 0 ? [] : await _repo.GetItemsAsync(required.Keys.ToList(), ct);
        var supplierOf = withSupplier.ToDictionary(i => i.Id);
        var ingredients = required
            .Select(kv =>
            {
                var meta = ingMeta[kv.Key];
                var have = onHand.GetValueOrDefault(kv.Key);
                supplierOf.TryGetValue(kv.Key, out var full);
                return new PlanIngredientDto(
                    kv.Key, meta.Name, meta.Unit, kv.Value, have, Math.Max(0m, kv.Value - have),
                    full?.DefaultSupplierId, full?.DefaultSupplier?.Name);
            })
            .OrderByDescending(i => i.Missing).ThenBy(i => i.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new ProductionPlanDto(start, end, gran, buckets, products, ingredients);
    }

    private static List<PlanBucketDto> BuildBuckets(DateOnly start, DateOnly end, string gran)
    {
        var list = new List<PlanBucketDto>();
        var cursor = start;
        while (cursor <= end)
        {
            var bucketEnd = gran switch
            {
                "week" => cursor.AddDays(6 - IsoDayIndex(cursor)),
                "month" => new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month)),
                _ => cursor,
            };
            if (bucketEnd > end) bucketEnd = end;
            var label = gran switch
            {
                "week" => $"{cursor:dd.MM}–{bucketEnd:dd.MM}",
                "month" => cursor.ToString("MM.yyyy", CultureInfo.InvariantCulture),
                _ => cursor.ToString("dd.MM", CultureInfo.InvariantCulture),
            };
            list.Add(new PlanBucketDto(BucketKey(cursor, gran), label, cursor, bucketEnd));
            cursor = bucketEnd.AddDays(1);
        }
        return list;
    }

    /// <summary>0 = Monday … 6 = Sunday.</summary>
    private static int IsoDayIndex(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;

    private static string BucketKey(DateOnly d, string gran) => gran switch
    {
        "week" => d.AddDays(-IsoDayIndex(d)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "month" => d.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    // ── Shipments ────────────────────────────────────────────────────────────

    public async Task<List<ReadyShipmentDto>> GetReadyShipmentsAsync(Guid? locationId, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(locationId, ct);
        if (scope.Count == 0) return [];

        var requests = await _repo.GetRequestsAsync(
            scope, [ProductionRequestStatus.Accepted, ProductionRequestStatus.Partial], ct: ct);
        if (requests.Count == 0) return [];
        var links = await _repo.GetLinksForRequestsAsync(requests.Select(r => r.Id).ToList(), ct);
        var stages = StagesOf(requests, links);

        return requests
            .Where(r => stages[r.Id] == ProductionRequestStage.Ready)
            .OrderBy(r => r.NeededBy).ThenBy(r => r.Id)
            .Select(r => new ReadyShipmentDto(
                r.Id, r.StoreLocationId, r.StoreLocation?.Name ?? "—", r.NeededBy, stages[r.Id],
                ShipLines(r), OrderIdsOf(r, links), CanShip: true))
            .ToList();
    }

    public async Task<List<ShippedShipmentDto>> GetShippedShipmentsAsync(
        DateOnly? from, DateOnly? to, Guid? locationId, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(locationId, ct);
        if (scope.Count == 0) return [];

        var end = to ?? Today;
        var start = from ?? end.AddDays(-30);
        var fromUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtcExclusive = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var requests = await _repo.GetRequestsAsync(
            scope, [ProductionRequestStatus.Fulfilled], fromUtc, toUtcExclusive, ct);

        return requests
            .OrderByDescending(r => r.ShippedAt).ThenBy(r => r.Id)
            .Select(r => new ShippedShipmentDto(
                r.Id, r.StoreLocationId, r.StoreLocation?.Name ?? "—", r.ShippedAt,
                r.ShipmentTransferId, r.ShipmentTransfer?.Status, ShipLines(r)))
            .ToList();
    }

    private static List<ShipmentLineDto> ShipLines(ProductionRequest r) =>
        r.Lines.Where(l => l.AcceptedQty is > 0)
            .Select(l => new ShipmentLineDto(l.ItemId, l.Item?.Name ?? "—", l.Item?.Unit ?? "", l.AcceptedQty!.Value))
            .OrderBy(l => l.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static List<Guid> OrderIdsOf(ProductionRequest r, List<ProductionOrderRequestLine> links)
    {
        var lineIds = r.Lines.Select(l => l.Id).ToHashSet();
        return links.Where(l => lineIds.Contains(l.RequestLineId)
                                && l.ProductionOrder is { Status: not ProductionOrderStatus.Cancelled })
            .Select(l => l.ProductionOrderId).Distinct().ToList();
    }

    // ── Warehouse ────────────────────────────────────────────────────────────

    public async Task<List<ProductionStockItemDto>> GetStockAsync(
        Guid? locationId, string? kind, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(locationId, ct);
        if (scope.Count == 0) return [];

        var wanted = kind?.ToLowerInvariant();
        var stock = await _repo.GetStockAsync(scope, ct);
        var recipes = await _repo.GetActiveRecipesAsync(ct);
        var outputs = recipes.Select(r => r.OutputItemId).ToHashSet();
        var ingredients = recipes.SelectMany(r => r.Ingredients.Select(i => i.ItemId)).ToHashSet();
        var today = Today;

        return stock
            .GroupBy(s => (s.ProductId, s.StoreId))
            .Select(g =>
            {
                var first = g.First();
                var k = Kind(g.Key.ProductId, outputs, ingredients);
                var batches = g.OrderBy(b => b.ExpiryDate).ThenBy(b => b.Id)
                    .Select(b => new ProductionStockBatchDto(
                        b.Id, b.BatchNumber, b.Quantity, b.ExpiryDate, b.ExpiryDate.DayNumber - today.DayNumber,
                        b.Status, b.SourceType, b.SourceId))
                    .ToList();
                return new ProductionStockItemDto(
                    g.Key.ProductId, first.Product?.Name ?? "—", first.Product?.Unit ?? "", k,
                    g.Sum(b => b.Quantity), batches.Count > 0 ? batches[0].ExpiryDate : null,
                    first.Store?.Name ?? "—", batches);
            })
            .Where(i => wanted is not ("raw" or "finished") || i.Kind == wanted)
            .OrderBy(i => i.NearestExpiry).ThenBy(i => i.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ── Shared helpers ───────────────────────────────────────────────────────

    private static Dictionary<Guid, string> StagesOf(
        List<ProductionRequest> requests, List<ProductionOrderRequestLine> links)
    {
        var lineToRequest = requests.SelectMany(r => r.Lines.Select(l => (LineId: l.Id, RequestId: r.Id)))
            .ToDictionary(x => x.LineId, x => x.RequestId);
        var byRequest = links.Where(l => lineToRequest.ContainsKey(l.RequestLineId))
            .GroupBy(l => lineToRequest[l.RequestLineId]).ToDictionary(g => g.Key, g => g.ToList());
        return requests.ToDictionary(
            r => r.Id,
            r => ProductionRequestStages.Derive(
                r.Status, r.Lines.ToList(), byRequest.GetValueOrDefault(r.Id) ?? []));
    }

    private static decimal AcceptedTotal(ProductionRequest r) => r.Lines.Sum(l => l.AcceptedQty ?? 0m);

    /// <summary>Non-expired quantity per item.</summary>
    private static Dictionary<Guid, decimal> OnHand(List<ProductStock> stock, DateOnly today) =>
        stock.Where(s => s.ExpiryDate >= today && s.Quantity > 0)
            .GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));

    private static void AddRecipeNeed(
        Dictionary<Guid, decimal> required, Dictionary<Guid, Item> meta, Recipe? recipe, decimal outputQty)
    {
        if (recipe == null || recipe.OutputQty <= 0 || outputQty <= 0) return;
        foreach (var ing in recipe.Ingredients)
        {
            required[ing.ItemId] = required.GetValueOrDefault(ing.ItemId) + outputQty * ing.Qty / recipe.OutputQty;
            if (ing.Item != null) meta[ing.ItemId] = ing.Item;
            else meta.TryAdd(ing.ItemId, new Item { Id = ing.ItemId, Name = "—", Unit = ing.Unit });
        }
    }

    private sealed record ShortageRow(
        Guid ItemId, string ItemName, string Unit, decimal Required, decimal OnHand, decimal Missing);

    private static List<ShortageRow> BuildShortages(
        Dictionary<Guid, decimal> required, Dictionary<Guid, Item> meta, Dictionary<Guid, decimal> onHand) =>
        required
            .Select(kv =>
            {
                var have = onHand.GetValueOrDefault(kv.Key);
                return new ShortageRow(kv.Key, meta[kv.Key].Name, meta[kv.Key].Unit, kv.Value, have, kv.Value - have);
            })
            .Where(r => r.Missing > 0)
            .OrderByDescending(r => r.Missing).ThenBy(r => r.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
