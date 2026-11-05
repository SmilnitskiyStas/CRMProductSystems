using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class OrderTrackingRepository : IOrderTrackingRepository
{
    private readonly AppDbContext _db;

    public OrderTrackingRepository(AppDbContext db) => _db = db;

    // unified status -> raw statuses per source
    private static readonly Dictionary<string, string[]> ReceiptStatuses = new()
    {
        ["new"] = ["draft"],
        ["confirmed"] = ["ordered"],
        ["in_transit"] = ["in_transit"],
        ["delivered"] = ["received"],
        ["cancelled"] = ["cancelled"],
    };

    private static readonly Dictionary<string, string[]> MarketplaceStatuses = new()
    {
        ["new"] = ["new"],
        ["confirmed"] = ["confirmed"],
        ["in_transit"] = ["shipped"],
        ["delivered"] = ["delivered"],
        ["cancelled"] = ["cancelled"],
    };

    private static string UnifyReceipt(string s) => s switch
    {
        "draft" => "new",
        "ordered" => "confirmed",
        "in_transit" => "in_transit",
        "received" => "delivered",
        "cancelled" => "cancelled",
        _ => "new",
    };

    private static string UnifyMarketplace(string s) => s switch
    {
        "new" => "new",
        "confirmed" => "confirmed",
        "shipped" => "in_transit",
        "delivered" => "delivered",
        "cancelled" => "cancelled",
        _ => "new",
    };

    private static string[] Raw(Dictionary<string, string[]> map, IReadOnlyCollection<string>? unified) =>
        unified is { Count: > 0 }
            ? unified.Where(map.ContainsKey).SelectMany(u => map[u]).Distinct().ToArray()
            : [];

    public async Task<(IReadOnlyList<TrackedOrderRow> Items, int Total)> GetPagedAsync(
        Guid tenantId, string? source, IReadOnlyCollection<string>? statuses, Guid? storeId, string? search,
        int page, int pageSize, bool overdueOnly = false, DateOnly? today = null, CancellationToken ct = default)
    {
        var todayDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var wantReceipts = source is null or "receipt";
        var wantMarketplace = source is null or "marketplace";
        var filterStatuses = statuses is { Count: > 0 };
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();
        var take = page * pageSize;

        var rows = new List<TrackedOrderRow>();
        var total = 0;

        // ── internal receipts ────────────────────────────────────────────────────────────────────
        if (wantReceipts)
        {
            var raw = Raw(ReceiptStatuses, statuses);
            var q = _db.StockReceipts.AsNoTracking().Where(r => r.TenantId == tenantId);
            if (storeId.HasValue) q = q.Where(r => r.DestinationStoreId == storeId);
            if (filterStatuses) q = q.Where(r => raw.Contains(r.Status));
            if (overdueOnly)
                q = q.Where(r => (r.Status == "draft" || r.Status == "ordered" || r.Status == "in_transit")
                                 && r.ExpectedAt != null && r.ExpectedAt < todayDate);
            if (term is not null)
                q = q.Where(r => (r.Supplier != null && r.Supplier.Name.ToLower().Contains(term))
                                 || (r.DestinationStore != null && r.DestinationStore.Name.ToLower().Contains(term)));

            total += await q.CountAsync(ct);
            var list = await q
                .Include(r => r.Supplier).Include(r => r.DestinationStore)
                .Include(r => r.Items).ThenInclude(i => i.Product)
                .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
                .Take(take)
                .ToListAsync(ct);

            var userIds = list.SelectMany(r => new[] { r.CreatedBy, r.ReceivedBy })
                .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
            var names = userIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

            rows.AddRange(list.Select(r =>
            {
                var lines = r.Items.Select(i => new TrackedOrderLineRow(
                    i.Product?.Name ?? "", i.QuantityOrdered, i.QuantityReceived, null,
                    i.PricePurchase.HasValue ? i.PricePurchase.Value * i.QuantityOrdered : null)).ToList();
                return new TrackedOrderRow(
                    "receipt", r.Id, null, UnifyReceipt(r.Status), r.Supplier?.Name,
                    r.DestinationStoreId, r.DestinationStore?.Name, r.CreatedAt,
                    r.CreatedBy.HasValue ? names.GetValueOrDefault(r.CreatedBy.Value) : null,
                    null, null, null, null, r.ExpectedAt, r.ReceivedAt,
                    r.ReceivedBy.HasValue ? names.GetValueOrDefault(r.ReceivedBy.Value) : null,
                    lines.Sum(l => l.Quantity), lines.Sum(l => l.Amount ?? 0m), r.DelayReason ?? r.Notes, lines,
                    r.PostponeCount, r.OriginalExpectedAt, r.PostponedAt);
            }));
        }

        // ── marketplace orders ───────────────────────────────────────────────────────────────────
        if (wantMarketplace)
        {
            var raw = Raw(MarketplaceStatuses, statuses);
            var q = _db.MarketplaceOrders.AsNoTracking().Where(o => o.ClientTenantId == tenantId);
            if (storeId.HasValue) q = q.Where(o => o.DestinationStoreId == storeId);
            if (filterStatuses) q = q.Where(o => raw.Contains(o.Status));
            if (overdueOnly)
            {
                var overdueIds = await OverdueMarketplaceIdsAsync(tenantId, storeId, todayDate, ct);
                q = q.Where(o => overdueIds.Contains(o.Id));
            }
            if (term is not null)
            {
                var supplierIds = await _db.Tenants.AsNoTracking()
                    .Where(t => t.Name.ToLower().Contains(term)).Select(t => t.Id).ToListAsync(ct);
                q = q.Where(o => o.OrderNumber.ToLower().Contains(term) || supplierIds.Contains(o.SupplierTenantId));
            }

            total += await q.CountAsync(ct);
            var list = await q
                .Include(o => o.Items)
                .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
                .Take(take)
                .ToListAsync(ct);

            var supplierIdsInPage = list.Select(o => o.SupplierTenantId).Distinct().ToList();
            var suppliers = supplierIdsInPage.Count == 0
                ? new Dictionary<Guid, string>()
                : await _db.Tenants.AsNoTracking().Where(t => supplierIdsInPage.Contains(t.Id))
                    .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            var storeIds = list.Where(o => o.DestinationStoreId.HasValue)
                .Select(o => o.DestinationStoreId!.Value).Distinct().ToList();
            var stores = storeIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _db.Locations.AsNoTracking().Where(l => storeIds.Contains(l.Id))
                    .ToDictionaryAsync(l => l.Id, l => l.Name, ct);

            rows.AddRange(list.Select(o =>
            {
                DateOnly? expected = o.ExpectedDeliveryDate
                    ?? (o.ShippedAt.HasValue && o.EstimatedDeliveryDays.HasValue
                        ? DateOnly.FromDateTime(o.ShippedAt.Value.UtcDateTime.AddDays(o.EstimatedDeliveryDays.Value))
                        : null);
                var lines = o.Items.Select(i => new TrackedOrderLineRow(
                    i.ItemName, i.Qty, null, i.Unit, i.LineTotal)).ToList();
                return new TrackedOrderRow(
                    "marketplace", o.Id, o.OrderNumber, UnifyMarketplace(o.Status),
                    suppliers.GetValueOrDefault(o.SupplierTenantId),
                    o.DestinationStoreId,
                    o.DestinationStoreId.HasValue ? stores.GetValueOrDefault(o.DestinationStoreId.Value) : null,
                    o.CreatedAt.UtcDateTime, o.CreatedByUserName,
                    o.ConfirmedAt?.UtcDateTime, o.ConfirmedByUserName,
                    o.ShippedAt?.UtcDateTime, o.ShippedByUserName,
                    expected, o.DeliveredAt?.UtcDateTime, null,
                    lines.Sum(l => l.Quantity), o.TotalAmount,
                    o.CancelReason ?? o.DelayReason ?? o.Comment, lines);
            }));
        }

        var pageRows = rows
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (pageRows, total);
    }

    /// <summary>Ids of the buyer's open marketplace orders that are past their expected delivery date.</summary>
    private async Task<List<Guid>> OverdueMarketplaceIdsAsync(
        Guid tenantId, Guid? storeId, DateOnly today, CancellationToken ct)
    {
        var q = _db.MarketplaceOrders.AsNoTracking().Where(o => o.ClientTenantId == tenantId
            && (o.Status == "new" || o.Status == "confirmed" || o.Status == "shipped"));
        if (storeId.HasValue) q = q.Where(o => o.DestinationStoreId == storeId);

        var open = await q.Select(o => new { o.Id, o.ExpectedDeliveryDate, o.ShippedAt, o.EstimatedDeliveryDays }).ToListAsync(ct);
        return open.Where(o =>
        {
            var exp = o.ExpectedDeliveryDate
                ?? (o.ShippedAt.HasValue && o.EstimatedDeliveryDays.HasValue
                    ? DateOnly.FromDateTime(o.ShippedAt.Value.UtcDateTime.AddDays(o.EstimatedDeliveryDays.Value))
                    : (DateOnly?)null);
            return exp.HasValue && exp.Value < today;
        }).Select(o => o.Id).ToList();
    }

    public async Task<OrderTrackingSummaryRow> GetSummaryAsync(
        Guid tenantId, Guid? storeId, DateOnly today, CancellationToken ct = default)
    {
        var rq = _db.StockReceipts.AsNoTracking().Where(r => r.TenantId == tenantId);
        var mq = _db.MarketplaceOrders.AsNoTracking().Where(o => o.ClientTenantId == tenantId);
        if (storeId.HasValue)
        {
            rq = rq.Where(r => r.DestinationStoreId == storeId);
            mq = mq.Where(o => o.DestinationStoreId == storeId);
        }

        var rc = await rq.GroupBy(r => r.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var mc = await mq.GroupBy(o => o.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);

        int Count(string unified) =>
            rc.Where(x => UnifyReceipt(x.Key) == unified).Sum(x => x.N)
            + mc.Where(x => UnifyMarketplace(x.Key) == unified).Sum(x => x.N);

        // A receipt in "ordered/in_transit" or an open marketplace order past its expected date.
        var overdueReceipts = await rq.CountAsync(r =>
            (r.Status == "draft" || r.Status == "ordered" || r.Status == "in_transit")
            && r.ExpectedAt != null && r.ExpectedAt < today, ct);
        var openMp = await mq
            .Where(o => o.Status == "new" || o.Status == "confirmed" || o.Status == "shipped")
            .Select(o => new { o.ExpectedDeliveryDate, o.ShippedAt, o.EstimatedDeliveryDays })
            .ToListAsync(ct);
        var overdueMp = openMp.Count(o =>
        {
            var exp = o.ExpectedDeliveryDate
                ?? (o.ShippedAt.HasValue && o.EstimatedDeliveryDays.HasValue
                    ? DateOnly.FromDateTime(o.ShippedAt.Value.UtcDateTime.AddDays(o.EstimatedDeliveryDays.Value))
                    : (DateOnly?)null);
            return exp.HasValue && exp.Value < today;
        });

        // Units on order (open = new/confirmed/in transit), units in transit, units late.
        var openReceiptQty = await _db.StockReceipts.AsNoTracking()
            .Where(r => r.TenantId == tenantId && (!storeId.HasValue || r.DestinationStoreId == storeId)
                        && (r.Status == "draft" || r.Status == "ordered" || r.Status == "in_transit"))
            .SelectMany(r => r.Items).SumAsync(i => (decimal?)i.QuantityOrdered, ct) ?? 0m;
        var transitReceiptQty = await _db.StockReceipts.AsNoTracking()
            .Where(r => r.TenantId == tenantId && (!storeId.HasValue || r.DestinationStoreId == storeId) && r.Status == "in_transit")
            .SelectMany(r => r.Items).SumAsync(i => (decimal?)i.QuantityOrdered, ct) ?? 0m;
        var overdueReceiptQty = await _db.StockReceipts.AsNoTracking()
            .Where(r => r.TenantId == tenantId && (!storeId.HasValue || r.DestinationStoreId == storeId)
                        && (r.Status == "draft" || r.Status == "ordered" || r.Status == "in_transit")
                        && r.ExpectedAt != null && r.ExpectedAt < today)
            .SelectMany(r => r.Items).SumAsync(i => (decimal?)i.QuantityOrdered, ct) ?? 0m;

        var mpQty = await mq.Where(o => o.Status == "new" || o.Status == "confirmed" || o.Status == "shipped")
            .Select(o => new { o.Id, o.Status, Qty = o.Items.Sum(i => i.Qty) }).ToListAsync(ct);
        var overdueMpIds = (await OverdueMarketplaceIdsAsync(tenantId, storeId, today, ct)).ToHashSet();

        var postponed = await rq.CountAsync(r =>
            (r.Status == "draft" || r.Status == "ordered" || r.Status == "in_transit")
            && r.PostponeCount > 0 && (r.ExpectedAt == null || r.ExpectedAt >= today), ct);

        return new OrderTrackingSummaryRow(
            Count("new"), Count("confirmed"), Count("in_transit"), Count("delivered"), Count("cancelled"),
            overdueReceipts + overdueMp,
            openReceiptQty + mpQty.Sum(x => x.Qty),
            transitReceiptQty + mpQty.Where(x => x.Status == "shipped").Sum(x => x.Qty),
            overdueReceiptQty + mpQty.Where(x => overdueMpIds.Contains(x.Id)).Sum(x => x.Qty),
            postponed);
    }
}
