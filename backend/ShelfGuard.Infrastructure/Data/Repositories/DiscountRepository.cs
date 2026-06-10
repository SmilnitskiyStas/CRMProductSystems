using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class DiscountRepository : IDiscountRepository
{
    private readonly AppDbContext _db;

    public DiscountRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Discount>> GetAllAsync(
        Guid tenantId,
        Guid? storeId  = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var query = _db.Discounts
            .Where(d => d.TenantId == tenantId)
            .AsQueryable();

        if (storeId.HasValue)
            query = query.Where(d => d.StoreId == storeId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status == status);

        return await query
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<Discount?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Discounts.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task AddAsync(Discount discount, CancellationToken ct = default) =>
        await _db.Discounts.AddAsync(discount, ct);

    public void Update(Discount discount) => _db.Discounts.Update(discount);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
