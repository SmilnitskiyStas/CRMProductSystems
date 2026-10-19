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

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
