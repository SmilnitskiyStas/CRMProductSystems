using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class BufferRepository : IBufferRepository
{
    private readonly AppDbContext _db;

    public BufferRepository(AppDbContext db) => _db = db;

    public Task<ProductBuffer?> GetAsync(Guid storeId, Guid productId, CancellationToken ct = default) =>
        _db.ProductBuffers
            .Include(b => b.Product)
            .FirstOrDefaultAsync(b => b.StoreId == storeId && b.ProductId == productId, ct);

    public Task<Dictionary<Guid, ProductBuffer>> GetByStoreAsync(Guid storeId, CancellationToken ct = default) =>
        _db.ProductBuffers
            .Where(b => b.StoreId == storeId)
            .ToDictionaryAsync(b => b.ProductId, ct);

    public Task<List<ProductAdu>> GetEffectiveAdusAsync(Guid storeId, CancellationToken ct = default) =>
        _db.ProductAdus
            .Where(a => a.StoreId == storeId && a.AduEffective != null)
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, Guid>> GetProductSuppliersAsync(
        IReadOnlyCollection<Guid> productIds, CancellationToken ct = default)
    {
        return await _db.Items
            .Where(p => productIds.Contains(p.Id) && p.DefaultSupplierId != null)
            .ToDictionaryAsync(p => p.Id, p => p.DefaultSupplierId!.Value, ct);
    }

    public Task<Dictionary<Guid, SupplySchedule>> GetActiveSchedulesAsync(
        Guid storeId, CancellationToken ct = default) =>
        _db.SupplySchedules
            .Where(s => s.StoreId == storeId && s.IsActive)
            .ToDictionaryAsync(s => s.SupplierId, ct);

    public Task<List<DailySale>> GetSalesWindowAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, DateOnly fromDate,
        CancellationToken ct = default) =>
        _db.DailySales
            .Where(s => s.StoreId == storeId
                && productIds.Contains(s.ProductId)
                && s.Date >= fromDate)
            .ToListAsync(ct);

    public Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default) =>
        _db.Locations.AnyAsync(s => s.Id == storeId && s.IsActive, ct);

    public async Task AddAsync(ProductBuffer buffer, CancellationToken ct = default) =>
        await _db.ProductBuffers.AddAsync(buffer, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
