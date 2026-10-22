using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ShelfRepository : IShelfRepository
{
    private readonly AppDbContext _db;

    public ShelfRepository(AppDbContext db) => _db = db;

    public Task<List<ShelfItem>> GetShelfItemsAsync(Guid zoneId, Guid? shelfId = null, CancellationToken ct = default) =>
        _db.ShelfItems
            .Include(s => s.Item)
            .Where(s => s.ZoneId == zoneId && (shelfId == null || s.ShelfId == shelfId))
            .OrderBy(s => s.ShelfId).ThenBy(s => s.Item!.Name)
            .ToListAsync(ct);

    public async Task ReplaceShelfItemsAsync(Guid zoneId, Guid shelfId, List<ShelfItem> rows, CancellationToken ct = default)
    {
        var existing = await _db.ShelfItems
            .Where(s => s.ZoneId == zoneId && s.ShelfId == shelfId)
            .ToListAsync(ct);

        var byItem = rows.ToDictionary(r => r.ItemId);
        foreach (var row in existing)
        {
            if (byItem.TryGetValue(row.ItemId, out var incoming))
            {
                row.ExpectedFacings = incoming.ExpectedFacings;
                byItem.Remove(row.ItemId);
            }
            else
            {
                _db.ShelfItems.Remove(row);
            }
        }

        // Explicit DbSet.Add: Guid default + store-generated key would otherwise mark a
        // navigation-attached row Modified (see supplier add-line incident).
        foreach (var row in byItem.Values)
            _db.ShelfItems.Add(row);
    }

    public Task<List<ItemImage>> GetItemImagesAsync(IEnumerable<Guid> itemIds, CancellationToken ct = default)
    {
        var ids = itemIds.Distinct().ToList();
        return _db.ItemImages
            .Where(i => ids.Contains(i.ItemId))
            .OrderBy(i => i.ItemId).ThenByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<HashSet<Guid>> GetExistingItemIdsAsync(IEnumerable<Guid> itemIds, CancellationToken ct = default)
    {
        var ids = itemIds.Distinct().ToList();
        var found = await _db.Items.Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
        return found.ToHashSet();
    }

    public Task<bool> ZoneTagExistsAsync(Guid itemId, Guid zoneId, CancellationToken ct = default) =>
        _db.ItemZoneAssignments.AnyAsync(a => a.ItemId == itemId && a.ZoneId == zoneId, ct);

    public async Task AddZoneTagAsync(ItemZoneAssignment tag, CancellationToken ct = default) =>
        await _db.ItemZoneAssignments.AddAsync(tag, ct);

    public async Task AddScanAsync(ShelfScan scan, CancellationToken ct = default) =>
        await _db.ShelfScans.AddAsync(scan, ct);

    public Task<ShelfScan?> GetScanAsync(Guid zoneId, Guid scanId, CancellationToken ct = default) =>
        _db.ShelfScans.FirstOrDefaultAsync(s => s.Id == scanId && s.ZoneId == zoneId, ct);

    public Task<List<ShelfScan>> GetScansAsync(Guid zoneId, int limit, CancellationToken ct = default) =>
        _db.ShelfScans
            .Where(s => s.ZoneId == zoneId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<List<ShelfStockAggregate>> GetStockAggregatesAsync(IEnumerable<Guid> itemIds, CancellationToken ct = default)
    {
        var ids = itemIds.Distinct().ToList();
        var rows = await _db.ProductStocks
            .Where(s => ids.Contains(s.ProductId) && s.Quantity > 0)
            .GroupBy(s => new { s.ProductId, s.StoreId, s.ZoneId, s.Store!.LocationType })
            .Select(g => new { g.Key.ProductId, g.Key.StoreId, g.Key.ZoneId, g.Key.LocationType, Qty = g.Sum(x => x.Quantity), Exp = g.Min(x => x.ExpiryDate) })
            .ToListAsync(ct);
        return rows.Select(r => new ShelfStockAggregate(r.ProductId, r.StoreId, r.ZoneId, r.LocationType, r.Qty, r.Exp)).ToList();
    }

    public async Task<List<ShelfSalesAggregate>> GetSalesAggregatesAsync(
        Guid locationId, IEnumerable<Guid> itemIds, DateOnly today, CancellationToken ct = default)
    {
        var ids = itemIds.Distinct().ToList();
        var from30 = today.AddDays(-29);
        var from7 = today.AddDays(-6);
        var fromPrev = today.AddDays(-13);
        var rows = await _db.DailySales
            .Where(d => d.StoreId == locationId && ids.Contains(d.ProductId) && d.Date >= from30 && d.Date <= today)
            .GroupBy(d => d.ProductId)
            .Select(g => new
            {
                ItemId = g.Key,
                S7 = g.Sum(x => x.Date >= from7 ? x.QuantitySold : 0m),
                SPrev = g.Sum(x => x.Date >= fromPrev && x.Date < from7 ? x.QuantitySold : 0m),
                S30 = g.Sum(x => x.QuantitySold),
            })
            .ToListAsync(ct);
        return rows.Select(r => new ShelfSalesAggregate(r.ItemId, r.S7, r.SPrev, r.S30)).ToList();
    }

    public async Task<Dictionary<Guid, decimal>> GetAduAsync(Guid locationId, IEnumerable<Guid> itemIds, CancellationToken ct = default)
    {
        var ids = itemIds.Distinct().ToList();
        var rows = await _db.ProductAdus
            .Where(a => a.StoreId == locationId && ids.Contains(a.ProductId) && a.AduEffective != null)
            .Select(a => new { a.ProductId, Adu = a.AduEffective!.Value })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.First().Adu);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
