using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.Analytics.PromoNoSales;
using ShelfGuard.Application.Features.Promotions;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class PromoNoSalesRepository : IPromoNoSalesRepository
{
    private readonly AppDbContext _db;
    private readonly IPromotionRuleRepository _rules;

    public PromoNoSalesRepository(AppDbContext db, IPromotionRuleRepository rules)
    {
        _db = db;
        _rules = rules;
    }

    // A promo reduced to "which products, in which stores (null = every store), over which dates".
    private sealed record PromoScope(PromoWindow Window, decimal? Percent, HashSet<Guid> ProductIds, HashSet<Guid>? StoreIds);

    public async Task<IReadOnlyList<PromoNoSalesCandidate>> GetCandidatesAsync(
        Guid tenantId, Guid? storeId, DateOnly today, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var scopes = new List<PromoScope>();

        // 1. Active discounts (one product x one store each).
        var discounts = await _db.Discounts.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Status == DiscountStatus.Active
                        && d.ValidFrom <= now && (d.ValidUntil == null || d.ValidUntil > now)
                        && (storeId == null || d.StoreId == storeId))
            .Select(d => new { d.Id, d.ProductId, d.StoreId, d.DiscountPercent, d.Reason, d.ValidFrom, d.ValidUntil })
            .ToListAsync(ct);
        foreach (var d in discounts)
            scopes.Add(new PromoScope(
                new PromoWindow("discount", d.Id, d.Reason, ToDate(d.ValidFrom), d.ValidUntil is { } u ? ToDate(u) : null),
                d.DiscountPercent, [d.ProductId], [d.StoreId]));

        // 2. Published, running campaigns (products x explicit stores).
        var campaigns = await _db.PromotionCampaigns.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status == PromotionCampaignStatus.Published
                        && c.StartsAt <= now && (c.EndsAt == null || c.EndsAt > now))
            .Select(c => new
            {
                c.Id, c.Title, c.StartsAt, c.EndsAt,
                Products = c.Products.Select(p => new { p.ProductId, p.DiscountPercent }).ToList(),
                Stores = c.Locations.Select(l => l.LocationId).ToList(),
            })
            .ToListAsync(ct);
        foreach (var c in campaigns)
        {
            if (c.Stores.Count == 0) continue;
            // One scope per distinct percent keeps the percent attached to its products.
            foreach (var g in c.Products.GroupBy(p => p.DiscountPercent))
                scopes.Add(new PromoScope(
                    new PromoWindow("campaign", c.Id, c.Title, ToDate(c.StartsAt), c.EndsAt is { } e ? ToDate(e) : null),
                    g.Key, g.Select(p => p.ProductId).ToHashSet(), c.Stores.ToHashSet()));
        }

        // 3. Published, running rules (item and category targets; empty storeIds = every store).
        var inputs = PromotionRuleLoader.ToInputs(await _rules.GetPublishedAsync(tenantId, ct));
        var nowOffset = new DateTimeOffset(now, TimeSpan.Zero);
        var categoryIds = inputs.SelectMany(r => r.Definition.Targets.CategoryIds).Distinct().ToList();
        var itemsByCategory = categoryIds.Count == 0
            ? new Dictionary<Guid, List<Guid>>()
            : (await _db.Items.AsNoTracking()
                    .Where(i => i.TenantId == tenantId && i.IsActive && i.CategoryId != null && categoryIds.Contains(i.CategoryId.Value))
                    .Select(i => new { i.Id, CategoryId = i.CategoryId!.Value })
                    .ToListAsync(ct))
                .GroupBy(i => i.CategoryId)
                .ToDictionary(g => g.Key, g => g.Select(i => i.Id).ToList());
        foreach (var r in inputs)
        {
            var s = r.Definition.Scope;
            if (s.ValidFrom is { } vf && vf > nowOffset) continue;
            if (s.ValidTo is { } vt && vt <= nowOffset) continue;
            var products = r.Definition.Targets.ItemIds.ToHashSet();
            foreach (var cid in r.Definition.Targets.CategoryIds)
                if (itemsByCategory.TryGetValue(cid, out var ids)) products.UnionWith(ids);
            if (products.Count == 0) continue;
            var start = s.ValidFrom is { } f ? DateOnly.FromDateTime(f.UtcDateTime) : today.AddDays(-PromoNoSalesThresholds.LookbackDays);
            scopes.Add(new PromoScope(
                new PromoWindow("rule", r.Id, r.Name, start, s.ValidTo is { } t ? DateOnly.FromDateTime(t.UtcDateTime) : null),
                r.Definition.Params.Percent,
                products,
                s.StoreIds.Count == 0 ? null : s.StoreIds.ToHashSet()));
        }

        if (scopes.Count == 0) return [];

        var productIds = scopes.SelectMany(s => s.ProductIds).Distinct().ToList();

        // Stock held by the promo products: per location, split into "placed in a zone" and total.
        var stockRows = await (
                from s in _db.ProductStocks.AsNoTracking()
                join l in _db.Locations.AsNoTracking() on s.StoreId equals l.Id
                where s.TenantId == tenantId && s.Quantity > 0 && productIds.Contains(s.ProductId)
                group s by new { s.StoreId, s.ProductId, l.Name, l.LocationType } into g
                select new
                {
                    g.Key.StoreId, g.Key.ProductId, StoreName = g.Key.Name, g.Key.LocationType,
                    Total = g.Sum(x => x.Quantity),
                    OnShelf = g.Where(x => x.ZoneId != null).Sum(x => x.Quantity),
                })
            .ToListAsync(ct);

        var warehouseByProduct = stockRows
            .Where(r => r.LocationType == "warehouse")
            .GroupBy(r => r.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Total));
        var storeStock = stockRows
            .Where(r => r.LocationType != "warehouse" && (storeId == null || r.StoreId == storeId))
            .ToList();
        if (storeStock.Count == 0) return [];

        var storeIds = storeStock.Select(r => r.StoreId).Distinct().ToList();
        var earliest = today.AddDays(-PromoNoSalesThresholds.LookbackDays);

        var sales = await _db.DailySales.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Date >= earliest && d.Date < today
                        && !d.IsAnomaly
                        && storeIds.Contains(d.StoreId) && productIds.Contains(d.ProductId))
            .Select(d => new { d.StoreId, d.ProductId, d.Date, d.QuantitySold })
            .ToListAsync(ct);
        var salesByPair = sales
            .GroupBy(d => (d.StoreId, d.ProductId))
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<DateOnly, decimal>)g
                .GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.Sum(y => y.QuantitySold)));

        var adus = (await _db.ProductAdus.AsNoTracking()
                .Where(a => a.TenantId == tenantId && storeIds.Contains(a.StoreId) && productIds.Contains(a.ProductId))
                .Select(a => new { a.StoreId, a.ProductId, a.AduEffective, a.Adu30d })
                .ToListAsync(ct))
            .ToDictionary(a => (a.StoreId, a.ProductId), a => a.AduEffective ?? a.Adu30d);

        var items = (await _db.Items.AsNoTracking()
                .Where(i => productIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Name, i.Barcodes })
                .ToListAsync(ct))
            .ToDictionary(i => i.Id);

        // Where the product is meant to stand: the planogram first, otherwise the zone with the most stock.
        var planogram = (await _db.ShelfItems.AsNoTracking()
                .Where(s => s.TenantId == tenantId && storeIds.Contains(s.LocationId) && productIds.Contains(s.ItemId))
                .Select(s => new { s.LocationId, s.ItemId, s.ZoneId })
                .ToListAsync(ct))
            .GroupBy(s => (s.LocationId, s.ItemId))
            .ToDictionary(g => g.Key, g => g.First().ZoneId);
        var stockZones = (await _db.ProductStocks.AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.Quantity > 0 && s.ZoneId != null
                            && storeIds.Contains(s.StoreId) && productIds.Contains(s.ProductId))
                .GroupBy(s => new { s.StoreId, s.ProductId, s.ZoneId })
                .Select(g => new { g.Key.StoreId, g.Key.ProductId, g.Key.ZoneId, Qty = g.Sum(x => x.Quantity) })
                .ToListAsync(ct))
            .GroupBy(r => (r.StoreId, r.ProductId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Qty).First().ZoneId);

        var zoneIds = planogram.Values
            .Concat(stockZones.Values.Where(z => z != null).Select(z => z!.Value))
            .Distinct().ToList();
        var zoneNames = zoneIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.LocationZones.AsNoTracking()
                .Where(z => zoneIds.Contains(z.Id))
                .ToDictionaryAsync(z => z.Id, z => z.Name, ct);

        var result = new List<PromoNoSalesCandidate>();
        foreach (var row in storeStock)
        {
            if (!items.TryGetValue(row.ProductId, out var item)) continue;
            Guid? zoneId = planogram.TryGetValue((row.StoreId, row.ProductId), out var pz)
                ? pz
                : stockZones.GetValueOrDefault((row.StoreId, row.ProductId));
            foreach (var sc in scopes)
            {
                if (!sc.ProductIds.Contains(row.ProductId)) continue;
                if (sc.StoreIds is not null && !sc.StoreIds.Contains(row.StoreId)) continue;
                result.Add(new PromoNoSalesCandidate(
                    row.StoreId, row.StoreName, row.ProductId, item.Name, item.Barcodes.FirstOrDefault(),
                    sc.Window, sc.Percent,
                    adus.GetValueOrDefault((row.StoreId, row.ProductId)),
                    row.Total, row.OnShelf, warehouseByProduct.GetValueOrDefault(row.ProductId),
                    zoneId, zoneId is { } z && zoneNames.TryGetValue(z, out var zn) ? zn : null,
                    salesByPair.GetValueOrDefault((row.StoreId, row.ProductId)) ?? new Dictionary<DateOnly, decimal>()));
            }
        }
        return result;
    }

    private static DateOnly ToDate(DateTime utc) => DateOnly.FromDateTime(utc);
}
