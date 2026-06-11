using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class DailySalesRepository : IDailySalesRepository
{
    private readonly AppDbContext _db;

    public DailySalesRepository(AppDbContext db) => _db = db;

    public async Task<List<DailySale>> GetAsync(
        Guid? storeId, Guid? productId, DateOnly? from, DateOnly? to,
        CancellationToken ct = default)
    {
        var query = _db.DailySales
            .Include(s => s.Product)
            .Include(s => s.Store)
            .AsQueryable();

        if (storeId.HasValue) query = query.Where(s => s.StoreId == storeId);
        if (productId.HasValue) query = query.Where(s => s.ProductId == productId);
        if (from.HasValue) query = query.Where(s => s.Date >= from);
        if (to.HasValue) query = query.Where(s => s.Date <= to);

        return await query
            .OrderByDescending(s => s.Date)
            .ThenBy(s => s.Product!.Name)
            .ToListAsync(ct);
    }

    public Task<DailySale?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.DailySales
            .Include(s => s.Product)
            .Include(s => s.Store)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<DailySale?> FindAsync(Guid storeId, Guid productId, DateOnly date, CancellationToken ct = default) =>
        _db.DailySales.FirstOrDefaultAsync(
            s => s.StoreId == storeId && s.ProductId == productId && s.Date == date, ct);

    public async Task<Dictionary<string, Guid>> GetProductIdsByBarcodesAsync(
        IReadOnlyCollection<string> barcodes, CancellationToken ct = default)
    {
        return await _db.CatalogProducts
            .Where(p => p.Barcode != null && barcodes.Contains(p.Barcode))
            .ToDictionaryAsync(p => p.Barcode!, p => p.Id, ct);
    }

    public Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default) =>
        _db.Stores.AnyAsync(s => s.Id == storeId && s.IsActive, ct);

    public Task<bool> ProductExistsAsync(Guid productId, CancellationToken ct = default) =>
        _db.CatalogProducts.AnyAsync(p => p.Id == productId && p.IsActive, ct);

    public async Task AddAsync(DailySale sale, CancellationToken ct = default) =>
        await _db.DailySales.AddAsync(sale, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
