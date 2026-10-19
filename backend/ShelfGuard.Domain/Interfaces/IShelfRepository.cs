using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IShelfRepository
{
    /// <summary>Planogram rows of a zone (Item included), ordered by shelf then item name.</summary>
    Task<List<ShelfItem>> GetShelfItemsAsync(Guid zoneId, Guid? shelfId = null, CancellationToken ct = default);

    /// <summary>Replaces all rows of one shelf section with <paramref name="rows"/>.</summary>
    Task ReplaceShelfItemsAsync(Guid zoneId, Guid shelfId, List<ShelfItem> rows, CancellationToken ct = default);

    /// <summary>Ids of the given items that exist in the current tenant (RLS-scoped).</summary>
    Task<HashSet<Guid>> GetExistingItemIdsAsync(IEnumerable<Guid> itemIds, CancellationToken ct = default);

    /// <summary>Catalog images of the given items, ordered primary first then SortOrder (RLS-scoped).</summary>
    Task<List<ItemImage>> GetItemImagesAsync(IEnumerable<Guid> itemIds, CancellationToken ct = default);

    Task<bool> ZoneTagExistsAsync(Guid itemId, Guid zoneId, CancellationToken ct = default);
    Task AddZoneTagAsync(ItemZoneAssignment tag, CancellationToken ct = default);

    Task AddScanAsync(ShelfScan scan, CancellationToken ct = default);
    Task<ShelfScan?> GetScanAsync(Guid zoneId, Guid scanId, CancellationToken ct = default);
    Task<List<ShelfScan>> GetScansAsync(Guid zoneId, int limit, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
