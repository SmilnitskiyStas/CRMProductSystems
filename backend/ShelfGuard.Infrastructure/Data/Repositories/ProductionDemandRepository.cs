using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>EF Core implementation of <see cref="IProductionDemandRepository"/>. Isolation via RLS.</summary>
public sealed class ProductionDemandRepository : IProductionDemandRepository
{
    private readonly AppDbContext _db;

    public ProductionDemandRepository(AppDbContext db) => _db = db;

    public async Task<List<Location>> GetActiveProductionLocationsAsync(CancellationToken ct = default)
    {
        var locations = await _db.Locations
            .Where(l => l.IsActive && l.Type == "production")
            .ToListAsync(ct);
        if (locations.Count <= 1) return locations;

        var ids = locations.Select(l => l.Id).ToList();
        var lastUsed = await _db.ProductionOrders
            .Where(o => ids.Contains(o.LocationId))
            .GroupBy(o => o.LocationId)
            .Select(g => new { Id = g.Key, At = g.Max(o => o.CreatedAt) })
            .ToDictionaryAsync(x => x.Id, x => x.At, ct);

        return locations
            .OrderByDescending(l => lastUsed.TryGetValue(l.Id, out var at) ? at : DateTimeOffset.MinValue)
            .ThenBy(l => l.Name)
            .ToList();
    }

    public Task<List<Location>> GetActiveStoresAsync(Guid? onlyLocationId, CancellationToken ct = default) =>
        _db.Locations
            .Where(l => l.IsActive && l.Type != "production"
                        && (onlyLocationId == null || l.Id == onlyLocationId))
            .OrderBy(l => l.Name)
            .ToListAsync(ct);

    public async Task<HashSet<Guid>> GetSelfProducedItemIdsAsync(CancellationToken ct = default) =>
        (await _db.Recipes.Where(r => r.IsActive).Select(r => r.OutputItemId).Distinct().ToListAsync(ct)).ToHashSet();

    public Task<Dictionary<Guid, decimal>> GetNonExpiredStockAsync(
        Guid storeId, IReadOnlyCollection<Guid> itemIds, DateOnly today, CancellationToken ct = default) =>
        _db.ProductStocks
            .Where(s => s.StoreId == storeId && itemIds.Contains(s.ProductId)
                        && s.Quantity > 0 && s.ExpiryDate >= today)
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(s => s.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);

    public Task<Dictionary<Guid, decimal>> GetTransferInTransitAsync(
        Guid storeId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default) =>
        _db.StockTransfers
            .Where(t => t.ToStoreId == storeId && t.Status == "in_transit")
            .SelectMany(t => t.Items)
            .Where(i => itemIds.Contains(i.ProductId))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);
}
