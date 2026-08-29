using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>See <see cref="IBannerRepository"/> for the contract and transaction-boundary convention.</summary>
public sealed class BannerRepository : IBannerRepository
{
    private readonly AppDbContext _db;

    public BannerRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Banner>> GetAllAsync(
        Guid tenantId, Guid? locationId, bool? activeOnly, CancellationToken ct = default)
    {
        var query = _db.Banners.Where(b => b.TenantId == tenantId);

        if (locationId.HasValue)
            query = query.Where(b => _db.BannerLocations.Any(
                bl => bl.BannerId == b.Id && bl.LocationId == locationId.Value));

        if (activeOnly == true)
        {
            var now = DateTime.UtcNow;
            query = query.Where(b =>
                b.IsActive && b.ValidFrom <= now && (b.ValidUntil == null || b.ValidUntil >= now));
        }

        return await query
            .OrderBy(b => b.SortOrder)
            .ThenByDescending(b => b.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<Banner?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        _db.Banners.FirstOrDefaultAsync(b => b.Id == id && b.TenantId == tenantId, ct);

    public async Task<Dictionary<Guid, List<Guid>>> GetLocationIdsForBannersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> bannerIds, CancellationToken ct = default)
    {
        if (bannerIds.Count == 0) return new Dictionary<Guid, List<Guid>>();

        var ids = bannerIds.ToList();
        var rows = await _db.BannerLocations
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && ids.Contains(x.BannerId))
            .Select(x => new { x.BannerId, x.LocationId })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.BannerId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.LocationId).ToList());
    }

    public async Task<Dictionary<Guid, List<Guid>>> GetProductIdsForBannersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> bannerIds, CancellationToken ct = default)
    {
        if (bannerIds.Count == 0) return new Dictionary<Guid, List<Guid>>();

        var ids = bannerIds.ToList();
        var rows = await _db.BannerProducts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && ids.Contains(x.BannerId))
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.BannerId, x.ItemId })
            .ToListAsync(ct);

        // Grouping after materialization (ToListAsync above) preserves the SortOrder-driven
        // sequence within each group — same batching approach as UserLocationRepository, plus
        // ordering (that repository's join rows have no display order to preserve).
        return rows
            .GroupBy(r => r.BannerId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ItemId).ToList());
    }

    public async Task<BannerEventAnalyticsSnapshot> GetEventAnalyticsAsync(
        Guid tenantId, Guid bannerId, DateTime? from = null, DateTime? toExclusive = null,
        IReadOnlyCollection<Guid>? storeIds = null,
        CancellationToken ct = default)
    {
        var query = _db.BannerEvents
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.BannerId == bannerId);
        if (from.HasValue) query = query.Where(x => x.OccurredAt >= from.Value);
        if (toExclusive.HasValue) query = query.Where(x => x.OccurredAt < toExclusive.Value);
        if (storeIds is { Count: > 0 })
        {
            var selectedStores = storeIds.ToList();
            query = query.Where(x => x.StoreId.HasValue && selectedStores.Contains(x.StoreId.Value));
        }

        var totals = await query.GroupBy(x => x.EventType)
            .Select(g => new { EventType = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var views = totals.Where(x => x.EventType == BannerEventType.View).Sum(x => x.Count);
        var clicks = totals.Where(x => x.EventType == BannerEventType.Click).Sum(x => x.Count);

        var dailyRows = await query.GroupBy(x => new { Day = x.OccurredAt.Date, x.EventType })
            .Select(g => new { g.Key.Day, g.Key.EventType, Count = g.Count() })
            .ToListAsync(ct);
        var daily = dailyRows.GroupBy(x => x.Day).OrderBy(x => x.Key)
            .Select(g => new BannerEventDailyCount(DateOnly.FromDateTime(g.Key),
                g.Where(x => x.EventType == BannerEventType.View).Sum(x => x.Count),
                g.Where(x => x.EventType == BannerEventType.Click).Sum(x => x.Count)))
            .ToList();

        var storeRows = await query.Where(x => x.StoreId.HasValue)
            .GroupBy(x => new { StoreId = x.StoreId!.Value, x.EventType })
            .Select(g => new { g.Key.StoreId, g.Key.EventType, Count = g.Count() })
            .ToListAsync(ct);
        var storeNames = await _db.Locations.AsNoTracking()
            .Where(x => x.TenantId == tenantId && storeRows.Select(r => r.StoreId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var stores = storeRows.GroupBy(x => x.StoreId)
            .Select(g => new BannerEventStoreCount(g.Key, storeNames.GetValueOrDefault(g.Key, "Невідомий магазин"),
                g.Where(x => x.EventType == BannerEventType.View).Sum(x => x.Count),
                g.Where(x => x.EventType == BannerEventType.Click).Sum(x => x.Count)))
            .OrderByDescending(x => x.Views).ToList();

        return new BannerEventAnalyticsSnapshot(views, clicks, daily, stores);
    }

    public async Task<Dictionary<Guid, (int Views, int Clicks)>> GetEventCountsForBannersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> bannerIds, DateTime? from = null, DateTime? toExclusive = null,
        CancellationToken ct = default)
    {
        var result = bannerIds.ToDictionary(id => id, _ => (Views: 0, Clicks: 0));
        if (bannerIds.Count == 0) return result;
        var ids = bannerIds.ToList();
        var query = _db.BannerEvents.AsNoTracking()
            .Where(x => x.TenantId == tenantId && ids.Contains(x.BannerId));
        if (from.HasValue) query = query.Where(x => x.OccurredAt >= from.Value);
        if (toExclusive.HasValue) query = query.Where(x => x.OccurredAt < toExclusive.Value);
        var rows = await query.GroupBy(x => new { x.BannerId, x.EventType })
            .Select(g => new { g.Key.BannerId, g.Key.EventType, Count = g.Count() }).ToListAsync(ct);
        foreach (var row in rows)
        {
            var current = result[row.BannerId];
            result[row.BannerId] = row.EventType == BannerEventType.View
                ? (current.Views + row.Count, current.Clicks)
                : (current.Views, current.Clicks + row.Count);
        }
        return result;
    }

    public async Task AddAsync(Banner banner, CancellationToken ct = default) =>
        await _db.Banners.AddAsync(banner, ct);

    public void Update(Banner banner) => _db.Banners.Update(banner);

    public async Task ReplaceLocationsAsync(
        Guid tenantId, Guid bannerId, IReadOnlyCollection<Guid> locationIds, CancellationToken ct = default)
    {
        var existing = await _db.BannerLocations
            .Where(x => x.TenantId == tenantId && x.BannerId == bannerId)
            .ToListAsync(ct);

        if (existing.Count > 0)
            _db.BannerLocations.RemoveRange(existing);

        foreach (var locationId in locationIds.Distinct())
            await _db.BannerLocations.AddAsync(BannerLocation.Create(tenantId, bannerId, locationId), ct);
    }

    public async Task ReplaceProductsAsync(
        Guid tenantId, Guid bannerId, IReadOnlyList<Guid> productIds, CancellationToken ct = default)
    {
        var existing = await _db.BannerProducts
            .Where(x => x.TenantId == tenantId && x.BannerId == bannerId)
            .ToListAsync(ct);

        if (existing.Count > 0)
            _db.BannerProducts.RemoveRange(existing);

        var distinct = productIds.Distinct().ToList();
        for (var i = 0; i < distinct.Count; i++)
            await _db.BannerProducts.AddAsync(BannerProduct.Create(tenantId, bannerId, distinct[i], i), ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
