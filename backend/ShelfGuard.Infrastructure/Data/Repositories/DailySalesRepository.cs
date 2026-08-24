using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class DailySalesRepository : IDailySalesRepository
{
    private readonly AppDbContext _db;

    public DailySalesRepository(AppDbContext db) => _db = db;

    public async Task<List<DailySale>> GetAsync(
        Guid[]? storeIds, Guid? productId, DateOnly? from, DateOnly? to,
        CancellationToken ct = default)
    {
        var query = _db.DailySales
            .Include(s => s.Product)
            .Include(s => s.Store)
            .AsQueryable();

        if (storeIds is { Length: > 0 }) query = query.Where(s => storeIds.Contains(s.StoreId));
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
        // Materialise to client side: JSONB array intersection can't be expressed
        // as a single EF/SQL predicate when filtering against an in-memory list.
        // Items table is small; this is acceptable.
        return await _db.Items
            .Where(p => p.Barcodes.Any(b => barcodes.Contains(b)))
            .Select(p => new { p.Id, p.Barcodes })
            .ToListAsync(ct)
            .ContinueWith(t => t.Result
                .SelectMany(p => p.Barcodes
                    .Where(b => barcodes.Contains(b))
                    .Select(b => new { Barcode = b, p.Id }))
                .GroupBy(x => x.Barcode)
                .ToDictionary(g => g.Key, g => g.First().Id));
    }

    public Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default) =>
        _db.Locations.AnyAsync(s => s.Id == storeId && s.IsActive, ct);

    public Task<bool> ProductExistsAsync(Guid productId, CancellationToken ct = default) =>
        _db.Items.AnyAsync(p => p.Id == productId && p.IsActive, ct);

    public async Task AddAsync(DailySale sale, CancellationToken ct = default) =>
        await _db.DailySales.AddAsync(sale, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
