using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class OrderCalcRepository : IOrderCalcRepository
{
    private readonly AppDbContext _db;

    public OrderCalcRepository(AppDbContext db) => _db = db;

    public Task<List<ProductBuffer>> GetBuffersAsync(Guid storeId, CancellationToken ct = default) =>
        _db.ProductBuffers
            .Include(b => b.Product)
            .Where(b => b.StoreId == storeId)
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, decimal>> GetStockOnHandAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        return await _db.ProductStocks
            .Where(s => s.StoreId == storeId && productIds.Contains(s.ProductId) && s.Quantity > 0)
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(s => s.Quantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);
    }

    public async Task<Dictionary<Guid, decimal>> GetInTransitAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        // Draft receipts = ordered but not yet received into stock.
        return await _db.StockReceipts
            .Where(r => r.DestinationStoreId == storeId && r.Status == "draft")
            .SelectMany(r => r.Items)
            .Where(i => productIds.Contains(i.ProductId))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(i => i.QuantityOrdered) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Qty, ct);
    }

    public async Task<Dictionary<Guid, (decimal Moq, decimal Usq)>> GetMoqUsqAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        var settings = await _db.Set<ProductSupplierSetting>()
            .Where(s => productIds.Contains(s.ProductId) && s.IsActive)
            .OrderByDescending(s => s.IsPrimary)
            .ToListAsync(ct);

        return settings
            .GroupBy(s => s.ProductId)
            .ToDictionary(g => g.Key, g => (g.First().Moq, g.First().Usq));
    }

    public Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default) =>
        _db.Locations.AnyAsync(s => s.Id == storeId && s.IsActive, ct);
}
