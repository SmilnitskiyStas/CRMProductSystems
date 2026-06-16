using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class AduRepository : IAduRepository
{
    private readonly AppDbContext _db;

    public AduRepository(AppDbContext db) => _db = db;

    public async Task<List<Guid>> GetEligibleProductIdsAsync(Guid storeId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Suppliers with an active delivery schedule into this store
        var scheduledSuppliers = _db.SupplySchedules
            .Where(s => s.StoreId == storeId && s.IsActive)
            .Select(s => s.SupplierId);

        // Products under a discount active right now (excluded per v2-spec §1)
        var promoProducts = _db.Discounts
            .Where(d => d.StoreId == storeId
                && d.Status == DiscountStatus.Active
                && d.ValidFrom <= now
                && (d.ValidUntil == null || d.ValidUntil >= now))
            .Select(d => d.ProductId);

        return await _db.Items
            .Where(p => p.IsActive
                && p.ManagementType == "MTS"
                && p.DefaultSupplierId != null
                && scheduledSuppliers.Contains(p.DefaultSupplierId.Value)
                && !promoProducts.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    public Task<List<DailySale>> GetSalesWindowAsync(
        Guid storeId, IReadOnlyCollection<Guid> productIds, DateOnly fromDate,
        CancellationToken ct = default) =>
        _db.DailySales
            .Where(s => s.StoreId == storeId
                && productIds.Contains(s.ProductId)
                && s.Date >= fromDate)
            .ToListAsync(ct);

    public Task<ProductAdu?> GetAsync(Guid storeId, Guid productId, CancellationToken ct = default) =>
        _db.ProductAdus
            .Include(a => a.Product)
            .FirstOrDefaultAsync(a => a.StoreId == storeId && a.ProductId == productId, ct);

    public Task<Dictionary<Guid, ProductAdu>> GetByStoreAsync(Guid storeId, CancellationToken ct = default) =>
        _db.ProductAdus
            .Where(a => a.StoreId == storeId)
            .ToDictionaryAsync(a => a.ProductId, ct);

    public Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default) =>
        _db.Locations.AnyAsync(s => s.Id == storeId && s.IsActive, ct);

    public async Task AddAsync(ProductAdu adu, CancellationToken ct = default) =>
        await _db.ProductAdus.AddAsync(adu, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
