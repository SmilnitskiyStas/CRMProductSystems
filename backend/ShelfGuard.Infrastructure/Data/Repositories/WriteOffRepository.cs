using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class WriteOffRepository : IWriteOffRepository
{
    private readonly AppDbContext _db;

    public WriteOffRepository(AppDbContext db) => _db = db;

    public async Task<List<WriteOff>> GetAllAsync(Guid? storeId, string? status, CancellationToken ct = default)
    {
        var query = _db.WriteOffs
            .Include(w => w.Items).ThenInclude(i => i.Product)
            .Include(w => w.Items).ThenInclude(i => i.ProductStock)
            .Include(w => w.Store)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(w => w.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(w => w.Status == status);

        return await query.OrderByDescending(w => w.CreatedAt).ToListAsync(ct);
    }

    public Task<WriteOff?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.WriteOffs
            .Include(w => w.Items).ThenInclude(i => i.Product)
            .Include(w => w.Items).ThenInclude(i => i.ProductStock)
            .Include(w => w.Store)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<ProductStock?> GetStockByIdAsync(Guid stockId, CancellationToken ct = default) =>
        _db.ProductStocks.FirstOrDefaultAsync(s => s.Id == stockId, ct);

    public async Task AddAsync(WriteOff writeOff, CancellationToken ct = default) =>
        await _db.WriteOffs.AddAsync(writeOff, ct);

    public async Task AddMovementAsync(StockMovement movement, CancellationToken ct = default) =>
        await _db.StockMovements.AddAsync(movement, ct);

    public void Update(WriteOff writeOff) => _db.WriteOffs.Update(writeOff);

    public void UpdateStock(ProductStock stock) => _db.ProductStocks.Update(stock);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
