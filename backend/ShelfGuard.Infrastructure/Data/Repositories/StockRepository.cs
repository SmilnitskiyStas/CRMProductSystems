using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class StockRepository : IStockRepository
{
    private readonly AppDbContext _db;

    public StockRepository(AppDbContext db) => _db = db;

    public async Task<List<ProductStock>> GetAllAsync(
        Guid? storeId, string? status, Guid? zoneId, Guid? productId,
        CancellationToken ct = default)
    {
        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(s => s.Status == status);

        if (zoneId.HasValue)
            query = query.Where(s => s.ZoneId == zoneId);

        if (productId.HasValue)
            query = query.Where(s => s.ProductId == productId);

        return await query
            .OrderBy(s => s.ExpiryDate)
            .ThenBy(s => s.Product!.Name)
            .ToListAsync(ct);
    }

    public Task<ProductStock?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<List<ProductStock>> GetExpiringAsync(Guid? storeId, int days, CancellationToken ct = default)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(days));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .Where(s => s.Quantity > 0 && s.ExpiryDate > today && s.ExpiryDate <= cutoff);

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        return await query.OrderBy(s => s.ExpiryDate).ToListAsync(ct);
    }

    public async Task<List<ProductStock>> GetExpiredAsync(Guid? storeId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .Where(s => s.Quantity > 0 && s.ExpiryDate <= today);

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        return await query.OrderBy(s => s.ExpiryDate).ToListAsync(ct);
    }

    public async Task<List<ProductStock>> GetNeedsCheckAsync(Guid? storeId, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);

        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .Where(s => s.Quantity > 0 && s.LastCheckedAt < cutoff);

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        return await query.OrderBy(s => s.LastCheckedAt).ToListAsync(ct);
    }

    public Task<List<ProductStock>> GetFefoOrderedAsync(Guid productId, Guid storeId, CancellationToken ct = default) =>
        _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .Where(s => s.ProductId == productId && s.StoreId == storeId && s.Quantity > 0)
            .OrderBy(s => s.ExpiryDate)
            .ToListAsync(ct);

    public async Task<List<ProductStock>> GetActionRequiredAsync(Guid? storeId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var warningCutoff = today.AddDays(14);

        var query = _db.ProductStocks
            .Include(s => s.Product).ThenInclude(p => p!.DefaultSupplier)
            .Include(s => s.Store)
            .Include(s => s.Zone)
            .Where(s => s.Quantity > 0 && s.ExpiryDate <= warningCutoff);

        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        return await query.OrderBy(s => s.ExpiryDate).ToListAsync(ct);
    }

    public Task<List<ProductStock>> GetDeficitStocksAsync(Guid productId, Guid excludeStoreId, CancellationToken ct = default) =>
        _db.ProductStocks
            .Include(s => s.Store)
            .Include(s => s.Product)
            .Where(s => s.ProductId == productId
                && s.StoreId != excludeStoreId
                && s.Quantity > 0
                && s.Product != null
                && s.Quantity < s.Product.MinStock)
            .ToListAsync(ct);

    public Task<List<Store>> GetProductionStoresAsync(CancellationToken ct = default) =>
        _db.Stores
            .Where(s => s.IsActive && (s.Type == "production" || s.Type == "distribution"))
            .ToListAsync(ct);

    public async Task<Dictionary<string, int>> GetStatusCountsAsync(Guid? storeId, CancellationToken ct = default)
    {
        var query = _db.ProductStocks.Where(s => s.Quantity > 0).AsQueryable();
        if (storeId.HasValue)
            query = query.Where(s => s.StoreId == storeId);

        return await query
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
    }

    public async Task AddAsync(ProductStock stock, CancellationToken ct = default) =>
        await _db.ProductStocks.AddAsync(stock, ct);

    public async Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) =>
        await _db.StockMovements.AddAsync(movement, ct);

    public void Update(ProductStock stock) =>
        _db.ProductStocks.Update(stock);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
