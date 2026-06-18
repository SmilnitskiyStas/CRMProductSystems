using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class SupplierRepository : ISupplierRepository
{
    private readonly AppDbContext _db;

    public SupplierRepository(AppDbContext db) => _db = db;

    public Task<List<Supplier>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _db.Suppliers.AsQueryable();
        if (!includeInactive)
            query = query.Where(s => s.IsActive);
        return query.OrderBy(s => s.Name).ToListAsync(ct);
    }

    public async Task<(List<Supplier> Items, int Total)> GetPagedAsync(
        bool includeInactive, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Suppliers.AsQueryable();
        if (!includeInactive)
            query = query.Where(s => s.IsActive);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<bool> ExistsByNameAsync(string name, Guid? excludeId, CancellationToken ct = default)
    {
        var query = _db.Suppliers.Where(s => s.Name == name && s.IsActive);
        if (excludeId.HasValue)
            query = query.Where(s => s.Id != excludeId.Value);
        return query.AnyAsync(ct);
    }

    public async Task AddAsync(Supplier supplier, CancellationToken ct = default) =>
        await _db.Suppliers.AddAsync(supplier, ct);

    public void Update(Supplier supplier) => _db.Suppliers.Update(supplier);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
