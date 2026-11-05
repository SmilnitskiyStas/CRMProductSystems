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

    // ── Pipeline (TASK-765) ──────────────────────────────────────────────────

    public const int MaxPipelineDays = 366;
    private const int MaxShippedList = 200;
    private const string DefaultZoneId = "Europe/Kyiv";

    /// <summary>
    /// 4-stage pipeline. Stages 1-3 (new / in work / ready) are CURRENT snapshots; stage 4 (shipped) and stats are
    /// for the inclusive date period [from, to] interpreted in the time zone of the scoped production location(s)
    /// (single distinct zone => that zone, otherwise Europe/Kyiv). Stats: received = SubmittedAt in period;
    /// accepted = ReviewedAt in period and status accepted/partial/fulfilled; rejected = ReviewedAt in period and
    /// status rejected; shipped = shipped.count.
    /// </summary>
    public async Task<ProductionPipelineDto> GetPipelineAsync(
        DateOnly? from, DateOnly? to, Guid? locationId, CancellationToken ct = default)
    {
        var locations = await _repo.GetProductionLocationsAsync(locationId, ct);
        var tz = ResolveZone(locations);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), tz).DateTime);
        var (start, end) = ResolvePeriod(from, to, todayLocal, defaultDays: 1);

        var days = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(i => start.AddDays(i)).ToList();
        var fromUtc = LocalMidnightUtc(start, tz);
        var toUtcExclusive = LocalMidnightUtc(end.AddDays(1), tz);

        if (locations.Count == 0)
            return new ProductionPipelineDto(start, end,
                new PipelineNewDto(0, []), new PipelineInWorkDto(0, []), new PipelineInWorkByStageDto(0, 0, 0),
                new PipelineReadyDto(0, [], []),
                new PipelineShippedDto(0, 0m, [], [], [], days.Select(d => new PipelineDayDto(d, 0, 0m)).ToList()),
                new PipelineStatsDto(0, 0, 0, 0));

        var scope = locations.Select(l => l.Id).ToList();
        var requests = await _repo.GetPipelineRequestsAsync(scope, fromUtc, toUtcExclusive, ct);
        var openIds = requests
            .Where(r => r.Status is ProductionRequestStatus.Accepted or ProductionRequestStatus.Partial)
            .Select(r => r.Id).ToList();
        var links = openIds.Count == 0 ? [] : await _repo.GetLinksForRequestsAsync(openIds, ct);
        var openRequests = requests.Where(r => openIds.Contains(r.Id)).ToList();
        var stages = StagesOf(openRequests, links);

        bool In(DateTime? t) => t.HasValue && t.Value >= fromUtc && t.Value < toUtcExclusive;

        // Stage 1 — new (oldest first)
        var newList = requests.Where(r => r.Status == ProductionRequestStatus.Submitted)
            .OrderBy(r => r.SubmittedAt ?? r.CreatedAt).ThenBy(r => r.Id)
            .Select(r => new PipelineNewRequestDto(
                r.Id, r.StoreLocation?.Name ?? "—", r.NeededBy, r.SubmittedAt,
                r.Lines.Sum(l => l.RequestedQty),
                r.Lines.Where(l => l.RequestedQty > 0)
                    .Select(l => new PipelineLineDto(l.Item?.Name ?? "—", l.Item?.Unit ?? "", l.RequestedQty))
                    .OrderBy(l => l.ItemName, StringComparer.CurrentCultureIgnoreCase).ToList()))
            .ToList();

        // Stage 2 — in work
        var inWorkStages = new[]
        {
            ProductionRequestStage.AwaitingOrder, ProductionRequestStage.Planned, ProductionRequestStage.InProduction,
        };
        var inWorkReqs = openRequests.Where(r => inWorkStages.Contains(stages[r.Id]))
            .OrderBy(r => r.NeededBy).ThenBy(r => r.Id).ToList();
        var inWork = inWorkReqs.Select(r => new PipelineInWorkRequestDto(
            r.Id, r.StoreLocation?.Name ?? "—", r.NeededBy, stages[r.Id], AcceptedTotal(r),
            PipeLines(r), OrdersOf(r, links))).ToList();
        var byStage = new PipelineInWorkByStageDto(
            inWork.Count(x => x.ProductionStage == ProductionRequestStage.AwaitingOrder),
            inWork.Count(x => x.ProductionStage == ProductionRequestStage.Planned),
            inWork.Count(x => x.ProductionStage == ProductionRequestStage.InProduction));

        // Stage 3 — ready
        var readyReqs = openRequests.Where(r => stages[r.Id] == ProductionRequestStage.Ready)
            .OrderBy(r => r.NeededBy).ThenBy(r => r.Id).ToList();
        var ready = new PipelineReadyDto(
            readyReqs.Count,
            readyReqs.Select(r => new PipelineReadyRequestDto(
                r.Id, r.StoreLocation?.Name ?? "—", r.NeededBy, AcceptedTotal(r), PipeLines(r), OrderIdsOf(r, links)))
                .ToList(),
            ByItem(readyReqs));

        // Stage 4 — shipped in period
        var shippedReqs = requests.Where(r => r.Status == ProductionRequestStatus.Fulfilled && In(r.ShippedAt))
            .OrderByDescending(r => r.ShippedAt).ThenBy(r => r.Id).ToList();
        var perDay = shippedReqs
            .GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(r.ShippedAt!.Value), tz)))
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Qty: g.Sum(AcceptedTotal)));
        var shipped = new PipelineShippedDto(
            shippedReqs.Count,
            shippedReqs.Sum(AcceptedTotal),
            shippedReqs.Take(MaxShippedList).Select(r => new PipelineShippedRequestDto(
                r.Id, r.StoreLocation?.Name ?? "—", r.ShippedAt, AcceptedTotal(r), PipeLines(r),
                r.ShipmentTransferId, r.ShipmentTransfer?.Status)).ToList(),
            ByItem(shippedReqs),
            shippedReqs.GroupBy(r => r.StoreLocationId)
                .Select(g => new PipelineStoreQtyDto(
                    g.Key, g.First().StoreLocation?.Name ?? "—", g.Count(), g.Sum(AcceptedTotal)))
                .OrderByDescending(s => s.Qty).ThenBy(s => s.StoreName, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            days.Select(d => perDay.TryGetValue(d, out var v)
                ? new PipelineDayDto(d, v.Count, v.Qty)
                : new PipelineDayDto(d, 0, 0m)).ToList());

        var stats = new PipelineStatsDto(
            Received: requests.Count(r => In(r.SubmittedAt)),
            Accepted: requests.Count(r => In(r.ReviewedAt) && r.Status is ProductionRequestStatus.Accepted
                or ProductionRequestStatus.Partial or ProductionRequestStatus.Fulfilled),
            Shipped: shippedReqs.Count,
            Rejected: requests.Count(r => In(r.ReviewedAt) && r.Status == ProductionRequestStatus.Rejected));

        return new ProductionPipelineDto(
            start, end,
            new PipelineNewDto(newList.Count, newList),
            new PipelineInWorkDto(inWork.Count, inWork), byStage, ready, shipped, stats);
    }

    /// <summary>
    /// Shared period resolution (pipeline + analytics): both given => as is; only one given => that single day;
    /// none => the last <paramref name="defaultDays"/> days ending today (local). Throws ArgumentException on from &gt; to
    /// or a span over <see cref="MaxPipelineDays"/>.
    /// </summary>
    private static (DateOnly Start, DateOnly End) ResolvePeriod(
        DateOnly? from, DateOnly? to, DateOnly todayLocal, int defaultDays)
    {
        DateOnly start, end;
        if (from.HasValue && to.HasValue) { start = from.Value; end = to.Value; }
        else if (from.HasValue) { start = end = from.Value; }
        else if (to.HasValue) { start = end = to.Value; }
        else { end = todayLocal; start = todayLocal.AddDays(-(defaultDays - 1)); }
        if (end < start) throw new ArgumentException("'from' must be on or before 'to'.");
        if (end.DayNumber - start.DayNumber + 1 > MaxPipelineDays)
            throw new ArgumentException($"Period must not exceed {MaxPipelineDays} days.");
        return (start, end);
    }

    private static DateOnly LocalDate(DateTime utc, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), tz));

    private static DateTime AsUtc(DateTime t) => DateTime.SpecifyKind(t, DateTimeKind.Utc);

    private static List<PipelineLineDto> PipeLines(ProductionRequest r) =>
        ShipLines(r).Select(l => new PipelineLineDto(l.ItemName, l.Unit, l.Qty)).ToList();

    private static List<PipelineOrderDto> OrdersOf(ProductionRequest r, List<ProductionOrderRequestLine> links)
    {
        var lineIds = r.Lines.Select(l => l.Id).ToHashSet();
        return links.Where(l => lineIds.Contains(l.RequestLineId)
                                && l.ProductionOrder is { Status: not ProductionOrderStatus.Cancelled })
            .GroupBy(l => l.ProductionOrderId)
            .Select(g => new PipelineOrderDto(g.Key, g.First().ProductionOrder!.Status.ToApiString(), g.Sum(x => x.Qty)))
            .OrderBy(o => o.OrderId).ToList();
    }

    private static List<PipelineItemQtyDto> ByItem(IEnumerable<ProductionRequest> requests) =>
        requests.SelectMany(r => r.Lines.Where(l => l.AcceptedQty is > 0))
            .GroupBy(l => l.ItemId)
            .Select(g => new PipelineItemQtyDto(
                g.Key, g.First().Item?.Name ?? "—", g.First().Item?.Unit ?? "", g.Sum(l => l.AcceptedQty!.Value)))
            .OrderByDescending(i => i.Qty).ThenBy(i => i.ItemName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static TimeZoneInfo ResolveZone(List<Location> locations)
    {
        var ids = locations.Select(l => l.TimeZone).Where(z => !string.IsNullOrWhiteSpace(z)).Distinct().ToList();
        var wanted = ids.Count == 1 ? ids[0] : DefaultZoneId;
        foreach (var id in new[] { wanted, DefaultZoneId, "Europe/Kiev", "FLE Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>UTC instant of 00:00 local on <paramref name="date"/> (skips a DST gap by moving to 01:00).</summary>
    private static DateTime LocalMidnightUtc(DateOnly date, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local)) local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    // ── Analytics (TASK-767) ─────────────────────────────────────────────────

    private const int DefaultAnalyticsDays = 30;
    private const int TopReasons = 10;

    private static bool IsAcceptedStatus(string s) =>
        s is ProductionRequestStatus.Accepted or ProductionRequestStatus.Partial or ProductionRequestStatus.Fulfilled;

    private static double? Hours(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 2);
    }

    /// <summary>
    /// Read-only analytics for the inclusive local period [from, to] (zone as in the pipeline; default last 30 days).
    /// received = SubmittedAt in period; reviewed = ReviewedAt in period (accepted = status accepted/partial/fulfilled,
    /// rejected = status rejected); shipped = Fulfilled with ShippedAt in period.
    /// totals.requestedQty = Σ RequestedQty of received requests; acceptedQty = Σ AcceptedQty of accepted-reviewed requests;
    /// shippedQty = Σ AcceptedQty of shipped requests. fillRate = acceptedQty / Σ RequestedQty of ALL reviewed requests
    /// (rejected ones count as unfilled), null when nothing was reviewed. avgReviewHours = mean(ReviewedAt − SubmittedAt)
    /// over reviewed requests; avgLeadHours = mean(ShippedAt − SubmittedAt) over shipped requests.
    /// byItem uses the same definitions per item; previous = the immediately preceding window of equal length.
    /// </summary>
    public async Task<ProductionAnalyticsDto> GetAnalyticsAsync(
        DateOnly? from, DateOnly? to, Guid? locationId, CancellationToken ct = default)
    {
        var locations = await _repo.GetProductionLocationsAsync(locationId, ct);
        var tz = ResolveZone(locations);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), tz).DateTime);
        var (start, end) = ResolvePeriod(from, to, todayLocal, DefaultAnalyticsDays);

        var length = end.DayNumber - start.DayNumber + 1;
        var prevEnd = start.AddDays(-1);
        var prevStart = start.AddDays(-length);
        var days = Enumerable.Range(0, length).Select(i => start.AddDays(i)).ToList();
        var fromUtc = LocalMidnightUtc(start, tz);
        var toUtcExclusive = LocalMidnightUtc(end.AddDays(1), tz);
        var prevFromUtc = LocalMidnightUtc(prevStart, tz);

        var requests = new List<ProductionRequest>();
        var orders = new List<ProductionOrder>();
        if (locations.Count > 0)
        {
            var scope = locations.Select(l => l.Id).ToList();
            requests = await _repo.GetAnalyticsRequestsAsync(scope, prevFromUtc, toUtcExclusive, ct);
            orders = await _repo.GetCompletedOrdersAsync(scope, fromUtc, toUtcExclusive, ct);
        }

        bool In(DateTime? t, DateTime a, DateTime b) => t.HasValue && t.Value >= a && t.Value < b;
        bool InCur(DateTime? t) => In(t, fromUtc, toUtcExclusive);

        var received = requests.Where(r => InCur(r.SubmittedAt)).ToList();
        var reviewed = requests.Where(r => InCur(r.ReviewedAt)).ToList();
        var acceptedReqs = reviewed.Where(r => IsAcceptedStatus(r.Status)).ToList();
        var rejectedReqs = reviewed.Where(r => r.Status == ProductionRequestStatus.Rejected).ToList();
        var shippedReqs = requests.Where(r => r.Status == ProductionRequestStatus.Fulfilled && InCur(r.ShippedAt)).ToList();

        static decimal Req(ProductionRequest r) => r.Lines.Sum(l => l.RequestedQty);
        var reviewedRequested = reviewed.Sum(Req);
        var acceptedQty = acceptedReqs.Sum(AcceptedTotal);

        var totals = new AnalyticsTotalsDto(
            received.Count, acceptedReqs.Count, rejectedReqs.Count, shippedReqs.Count,
            received.Sum(Req), acceptedQty, shippedReqs.Sum(AcceptedTotal),
            reviewedRequested > 0 ? Math.Round(acceptedQty / reviewedRequested, 4) : null,
            Hours(reviewed.Where(r => r.SubmittedAt.HasValue)
                .Select(r => (r.ReviewedAt!.Value - r.SubmittedAt!.Value).TotalHours)),
            Hours(shippedReqs.Where(r => r.SubmittedAt.HasValue)
                .Select(r => (r.ShippedAt!.Value - r.SubmittedAt!.Value).TotalHours)));

        var ordersDone = new AnalyticsOrdersDoneDto(
            orders.Count, orders.Sum(o => o.PlannedQty),
            Hours(orders.Where(o => o.StartedAt.HasValue)
                .Select(o => (o.CompletedAt!.Value - o.StartedAt!.Value).TotalHours)));

        // byItem
        var itemAgg = new Dictionary<Guid, (string Name, string Unit, decimal Req, decimal Acc, decimal Ship, decimal RevReq)>();
        void Bump(ProductionRequestLine l, decimal req, decimal acc, decimal ship, decimal revReq)
        {
            var cur = itemAgg.TryGetValue(l.ItemId, out var v) ? v : (l.Item?.Name ?? "—", l.Item?.Unit ?? "", 0m, 0m, 0m, 0m);
            itemAgg[l.ItemId] = (cur.Item1, cur.Item2, cur.Item3 + req, cur.Item4 + acc, cur.Item5 + ship, cur.Item6 + revReq);
        }
        foreach (var l in received.SelectMany(r => r.Lines)) Bump(l, l.RequestedQty, 0, 0, 0);
        foreach (var l in reviewed.SelectMany(r => r.Lines)) Bump(l, 0, 0, 0, l.RequestedQty);
        foreach (var l in acceptedReqs.SelectMany(r => r.Lines)) Bump(l, 0, l.AcceptedQty ?? 0m, 0, 0);
        foreach (var l in shippedReqs.SelectMany(r => r.Lines)) Bump(l, 0, 0, l.AcceptedQty ?? 0m, 0);
        var byItem = itemAgg
            .Select(kv => new AnalyticsItemDto(kv.Key, kv.Value.Name, kv.Value.Unit, kv.Value.Req, kv.Value.Acc, kv.Value.Ship,
                kv.Value.RevReq > 0 ? Math.Round(kv.Value.Acc / kv.Value.RevReq, 4) : null))
            .OrderByDescending(i => i.ShippedQty).ThenByDescending(i => i.RequestedQty)
            .ThenBy(i => i.ItemName, StringComparer.CurrentCultureIgnoreCase).ToList();

        // byStore
        var storeIds = received.Concat(shippedReqs).Concat(rejectedReqs).Select(r => r.StoreLocationId).Distinct().ToList();
        var byStore = storeIds.Select(id =>
        {
            var rec = received.Where(r => r.StoreLocationId == id).ToList();
            var shp = shippedReqs.Where(r => r.StoreLocationId == id).ToList();
            var name = requests.Where(r => r.StoreLocationId == id).Select(r => r.StoreLocation?.Name)
                .FirstOrDefault(n => n != null) ?? "—";
            return new AnalyticsStoreDto(id, name, rec.Count, rec.Sum(Req), shp.Sum(AcceptedTotal),
                rejectedReqs.Count(r => r.StoreLocationId == id),
                Hours(shp.Where(r => r.SubmittedAt.HasValue)
                    .Select(r => (r.ShippedAt!.Value - r.SubmittedAt!.Value).TotalHours)));
        })
        .OrderByDescending(s => s.ShippedQty).ThenByDescending(s => s.Requests)
        .ThenBy(s => s.StoreName, StringComparer.CurrentCultureIgnoreCase).ToList();

        // perDay
        var dayIdx = days.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);
        var recC = new int[length]; var accC = new int[length]; var rejC = new int[length];
        var shipC = new int[length]; var shipQ = new decimal[length]; var prod = new decimal[length];
        void Inc(DateTime? t, Action<int> act)
        {
            if (t.HasValue && dayIdx.TryGetValue(LocalDate(t.Value, tz), out var i)) act(i);
        }
        foreach (var r in received) Inc(r.SubmittedAt, i => recC[i]++);
        foreach (var r in acceptedReqs) Inc(r.ReviewedAt, i => accC[i]++);
        foreach (var r in rejectedReqs) Inc(r.ReviewedAt, i => rejC[i]++);
        foreach (var r in shippedReqs) Inc(r.ShippedAt, i => { shipC[i]++; shipQ[i] += AcceptedTotal(r); });
        foreach (var o in orders)
            if (o.CompletedAt.HasValue && dayIdx.TryGetValue(LocalDate(o.CompletedAt.Value.UtcDateTime, tz), out var i))
                prod[i] += o.PlannedQty;
        var perDay = days.Select((d, i) => new AnalyticsDayDto(d, recC[i], accC[i], rejC[i], shipC[i], shipQ[i], prod[i])).ToList();

        // rejection reasons
        var reasons = new List<string>();
        foreach (var r in rejectedReqs)
            if (!string.IsNullOrWhiteSpace(r.ReviewNote)) reasons.Add(r.ReviewNote.Trim());
        foreach (var l in reviewed.SelectMany(r => r.Lines))
            if ((l.AcceptedQty ?? 0m) < l.RequestedQty && !string.IsNullOrWhiteSpace(l.Reason)) reasons.Add(l.Reason.Trim());
        var rejectionReasons = reasons
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AnalyticsReasonDto(g.First(), g.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Reason, StringComparer.CurrentCultureIgnoreCase)
            .Take(TopReasons).ToList();

        // previous window
        bool InPrev(DateTime? t) => In(t, prevFromUtc, fromUtc);
        var prevShipped = requests.Where(r => r.Status == ProductionRequestStatus.Fulfilled && InPrev(r.ShippedAt)).ToList();
        var previous = new AnalyticsPreviousDto(
            prevStart, prevEnd,
            requests.Count(r => InPrev(r.SubmittedAt)),
            requests.Count(r => InPrev(r.ReviewedAt) && IsAcceptedStatus(r.Status)),
            requests.Count(r => InPrev(r.ReviewedAt) && r.Status == ProductionRequestStatus.Rejected),
            prevShipped.Count, prevShipped.Sum(AcceptedTotal));

        return new ProductionAnalyticsDto(
            start, end, totals, ordersDone, byItem, byStore, perDay, rejectionReasons, previous);
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
