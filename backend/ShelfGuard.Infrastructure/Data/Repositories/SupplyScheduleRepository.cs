using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class SupplyScheduleRepository : ISupplyScheduleRepository
{
    private readonly AppDbContext _db;

    /// <summary>Ukrainian-collation ordering only where the provider supports it (Npgsql).</summary>
    private bool Uk => _db.Database.IsNpgsql();

    public SupplyScheduleRepository(AppDbContext db) => _db = db;

    public async Task<List<SupplySchedule>> GetAsync(
        Guid? storeId, Guid? supplierId, CancellationToken ct = default)
    {
        var query = _db.SupplySchedules
            .Include(s => s.Store)
            .Include(s => s.Supplier)
            .AsQueryable();

        if (storeId.HasValue) query = query.Where(s => s.StoreId == storeId);
        if (supplierId.HasValue) query = query.Where(s => s.SupplierId == supplierId);

        return await query
            .OrderByUk(s => s.Store!.Name, Uk)
            .ThenByUk(s => s.Supplier!.Name, Uk)
            .ToListAsync(ct);
    }

    public Task<SupplySchedule?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SupplySchedules
            .Include(s => s.Store)
            .Include(s => s.Supplier)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<bool> ActiveExistsAsync(
        Guid storeId, Guid supplierId, Guid? excludeId, CancellationToken ct = default) =>
        _db.SupplySchedules.AnyAsync(
            s => s.StoreId == storeId
                && s.SupplierId == supplierId
                && s.IsActive
                && (excludeId == null || s.Id != excludeId), ct);

    public Task<bool> StoreExistsAsync(Guid storeId, CancellationToken ct = default) =>
        _db.Locations.AnyAsync(s => s.Id == storeId && s.IsActive, ct);

    public Task<bool> SupplierExistsAsync(Guid supplierId, CancellationToken ct = default) =>
        _db.Suppliers.AnyAsync(s => s.Id == supplierId && s.IsActive, ct);

    public async Task AddAsync(SupplySchedule schedule, CancellationToken ct = default) =>
        await _db.SupplySchedules.AddAsync(schedule, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
