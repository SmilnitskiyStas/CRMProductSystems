using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>Stock (quantity &gt; 0) of one item aggregated per location + zone (RLS-scoped).</summary>
public sealed record ShelfStockAggregate(
    Guid ItemId, Guid LocationId, Guid? ZoneId, string LocationType, decimal Quantity, DateOnly NearestExpiry);

/// <summary>Sales of one item in a location: last 7 days, the 7 days before, and last 30 days.</summary>
public sealed record ShelfSalesAggregate(Guid ItemId, decimal Sold7d, decimal SoldPrev7d, decimal Sold30d);

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

    /// <summary>One grouped query: stock of all given items across every location of the tenant (no N+1).</summary>
    Task<List<ShelfStockAggregate>> GetStockAggregatesAsync(IEnumerable<Guid> itemIds, CancellationToken ct = default);

    /// <summary>One grouped query over <c>daily_sales</c> for the given items in one location (windows end at <paramref name="today"/>).</summary>
    Task<List<ShelfSalesAggregate>> GetSalesAggregatesAsync(
        Guid locationId, IEnumerable<Guid> itemIds, DateOnly today, CancellationToken ct = default);

    /// <summary>Cached <c>adu_effective</c> per item for one location (items without an ADU row are absent).</summary>
    Task<Dictionary<Guid, decimal>> GetAduAsync(Guid locationId, IEnumerable<Guid> itemIds, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
