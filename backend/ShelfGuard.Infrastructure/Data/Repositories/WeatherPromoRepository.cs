using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Features.WeatherPromo;
using ShelfGuard.Application.Features.WeatherPromo.Dtos;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// TASK-711 — cross-table context reads for the weather-promo feature. Every query is
/// tenant-scoped by RLS; <c>platform_categories</c> is global reference data (no RLS).
/// </summary>
public sealed class WeatherPromoRepository : IWeatherPromoRepository
{
    private const int TopSellerCount = 30;

    private readonly AppDbContext _db;

    public WeatherPromoRepository(AppDbContext db) => _db = db;

    public async Task<WeatherPromoContextData> GetContextDataAsync(
        Guid tenantId, DateOnly today, CancellationToken ct = default)
    {
        var forecastLocations = await _db.Locations
            .Where(l => l.TenantId == tenantId && l.IsActive && l.Latitude != null && l.Longitude != null)
            .OrderBy(l => l.Name)
            .Select(l => new WeatherPromoForecastLocation(l.Id, l.Name))
            .ToListAsync(ct);

        // ── Top sellers, last 45 days, anomalies excluded ────────────────────
        var since = today.AddDays(-45);
        var salesByProduct = await _db.DailySales
            .Where(d => d.TenantId == tenantId && d.Date >= since && !d.IsAnomaly)
            .GroupBy(d => d.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.QuantitySold) })
            .OrderByDescending(x => x.Qty)
            .Take(TopSellerCount)
            .ToListAsync(ct);

        var soldIds = salesByProduct.Select(s => s.ProductId).ToList();
        var items = await _db.Items
            .Where(i => i.TenantId == tenantId && soldIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Name, i.CategoryId, i.PriceRetail })
            .ToListAsync(ct);
        var itemById = items.ToDictionary(i => i.Id);

        // ── Weather-demand coefficients ─────────────────────────────────────
        var coefficients = await _db.WeatherCoefficients
            .Where(c => c.TenantId == tenantId)
            .Select(c => new
            {
                c.CategoryId,
                c.SegmentId,
                c.TempAbove,
                c.TempBelow,
                c.WeatherCode,
                c.Coefficient,
            })
            .ToListAsync(ct);

        // ── Resolve category / segment display names ────────────────────────
        var categoryIds = items.Where(i => i.CategoryId != null).Select(i => i.CategoryId!.Value)
            .Concat(coefficients.Where(c => c.CategoryId != null).Select(c => c.CategoryId!.Value))
            .Distinct()
            .ToList();
        var categoryNames = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.PlatformCategories
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var segmentIds = coefficients.Where(c => c.SegmentId != null).Select(c => c.SegmentId!.Value).Distinct().ToList();
        var segmentNames = segmentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.ProductSegments
                .Where(s => segmentIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var topSellers = salesByProduct
            .Where(s => itemById.ContainsKey(s.ProductId))
            .Select(s =>
            {
                var it = itemById[s.ProductId];
                string? category = it.CategoryId != null && categoryNames.TryGetValue(it.CategoryId.Value, out var cn)
                    ? cn
                    : null;
                return new WeatherPromoTopSellerLine(it.Id, it.Name, category, it.PriceRetail, s.Qty);
            })
            .ToList();

        var coefficientLines = coefficients
            .Select(c => new WeatherPromoCoefficientLine(
                c.CategoryId != null && categoryNames.TryGetValue(c.CategoryId.Value, out var cn) ? cn : null,
                c.SegmentId != null && segmentNames.TryGetValue(c.SegmentId.Value, out var sn) ? sn : null,
                c.TempAbove,
                c.TempBelow,
                c.WeatherCode,
                c.Coefficient))
            .ToList();

        // ── Already-running promos / discounts (so the AI doesn't duplicate) ─
        var activePromos = await _db.DemandEvents
            .Where(e => e.TenantId == tenantId && e.EventType == "promo" && e.EndsAt >= today)
            .OrderBy(e => e.StartsAt)
            .Select(e => e.Name)
            .ToListAsync(ct);

        var discountedProducts = await _db.Discounts
            .Where(d => d.TenantId == tenantId && d.Status == "active")
            .Join(_db.Items, d => d.ProductId, i => i.Id, (d, i) => i.Name)
            .Distinct()
            .ToListAsync(ct);

        return new WeatherPromoContextData(
            forecastLocations, topSellers, coefficientLines, activePromos, discountedProducts);
    }

    public async Task<IReadOnlyList<WeatherPromoItemRow>> GetItemsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default)
    {
        if (itemIds.Count == 0) return Array.Empty<WeatherPromoItemRow>();

        var ids = itemIds.ToList();
        return await _db.Items
            .Where(i => i.TenantId == tenantId && ids.Contains(i.Id))
            .Select(i => new WeatherPromoItemRow(i.Id, i.Name, i.PriceRetail))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetActiveLocationIdsAsync(Guid tenantId, CancellationToken ct = default) =>
        await _db.Locations
            .Where(l => l.TenantId == tenantId && l.IsActive)
            .Select(l => l.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WeatherPromoActiveDiscount>> GetActivePromoDiscountsAsync(
        Guid tenantId, CancellationToken ct = default) =>
        await _db.Discounts
            .Where(d => d.TenantId == tenantId && d.Status == "active" && d.Reason == "promo")
            .Join(_db.Items, d => d.ProductId, i => i.Id, (d, i) => new { d, ItemName = i.Name })
            .Join(_db.Locations, x => x.d.StoreId, l => l.Id, (x, l) => new { x.d, x.ItemName, StoreName = l.Name })
            .OrderByDescending(x => x.d.CreatedAt)
            .Select(x => new WeatherPromoActiveDiscount(
                x.d.Id,
                x.ItemName,
                x.StoreName,
                x.d.DiscountPercent,
                x.d.PriceOriginal,
                x.d.PriceDiscounted,
                x.d.ValidFrom.ToString("yyyy-MM-dd"),
                x.d.ValidUntil == null ? null : x.d.ValidUntil.Value.ToString("yyyy-MM-dd")))
            .ToListAsync(ct);
}
