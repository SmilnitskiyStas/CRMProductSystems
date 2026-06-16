using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class CannibalizationRepository : ICannibalizationRepository
{
    private readonly AppDbContext _db;

    public CannibalizationRepository(AppDbContext db) => _db = db;

    public Task<Discount?> GetDiscountAsync(Guid discountId, CancellationToken ct = default) =>
        _db.Discounts.FirstOrDefaultAsync(d => d.Id == discountId, ct);

    public async Task<Item?> GetDiscountProductAsync(Guid discountId, CancellationToken ct = default)
    {
        var productId = await _db.Discounts
            .Where(d => d.Id == discountId)
            .Select(d => (Guid?)d.ProductId)
            .FirstOrDefaultAsync(ct);

        return productId is null
            ? null
            : await _db.Items.FirstOrDefaultAsync(p => p.Id == productId, ct);
    }

    public Task<List<PromoCannibalization>> GetByDiscountAsync(Guid discountId, CancellationToken ct = default) =>
        _db.PromoCannibalizations
            .Include(p => p.AffectedProduct)
            .Where(p => p.DiscountId == discountId)
            .ToListAsync(ct);

    public Task<PromoCannibalization?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.PromoCannibalizations
            .Include(p => p.AffectedProduct)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<List<Item>> GetSegmentSiblingsAsync(
        Guid segmentId, Guid excludeProductId, CancellationToken ct = default) =>
        _db.Items
            .Where(p => p.IsActive && p.SegmentId == segmentId && p.Id != excludeProductId)
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, decimal>> GetActivePromoCoefficientsAsync(
        Guid storeId, DateTime now, CancellationToken ct = default)
    {
        var rows = await _db.PromoCannibalizations
            .Where(p => p.IsApplied
                && p.Discount!.StoreId == storeId
                && p.Discount.Status == DiscountStatus.Active
                && p.Discount.ValidFrom <= now
                && (p.Discount.ValidUntil == null || p.Discount.ValidUntil >= now))
            .Select(p => new { p.AffectedProductId, p.OrderCoefficient })
            .ToListAsync(ct);

        // Several simultaneous promos can affect the same product — multiply.
        return rows
            .GroupBy(r => r.AffectedProductId)
            .ToDictionary(
                g => g.Key,
                g => Math.Round(g.Aggregate(1m, (acc, r) => acc * r.OrderCoefficient), 2));
    }

    public async Task AddAsync(PromoCannibalization row, CancellationToken ct = default) =>
        await _db.PromoCannibalizations.AddAsync(row, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
