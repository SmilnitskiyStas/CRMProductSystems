using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class StoreRepository : IStoreRepository
{
    private readonly AppDbContext _db;

    public StoreRepository(AppDbContext db) => _db = db;

    public Task<List<Store>> GetAllAsync(CancellationToken ct = default) =>
        _db.Stores
            .Include(s => s.Zones.Where(z => z.IsActive))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public Task<Store?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Stores
            .Include(s => s.Zones.Where(z => z.IsActive))
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<List<StoreZone>> GetZonesAsync(Guid storeId, CancellationToken ct = default) =>
        _db.StoreZones
            .Where(z => z.StoreId == storeId)
            .OrderBy(z => z.Name)
            .ToListAsync(ct);

    public Task<StoreZone?> GetZoneByIdAsync(Guid zoneId, CancellationToken ct = default) =>
        _db.StoreZones.FirstOrDefaultAsync(z => z.Id == zoneId, ct);

    public async Task AddAsync(Store store, CancellationToken ct = default) =>
        await _db.Stores.AddAsync(store, ct);

    public async Task AddZoneAsync(StoreZone zone, CancellationToken ct = default) =>
        await _db.StoreZones.AddAsync(zone, ct);

    public void Update(Store store) => _db.Stores.Update(store);

    public void UpdateZone(StoreZone zone) => _db.StoreZones.Update(zone);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
