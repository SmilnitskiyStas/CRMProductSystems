using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class LocationRepository : ILocationRepository
{
    private readonly AppDbContext _db;

    public LocationRepository(AppDbContext db) => _db = db;

    public Task<List<Location>> GetAllAsync(CancellationToken ct = default) =>
        _db.Locations
            .Include(s => s.Zones.Where(z => z.IsActive))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public Task<Location?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Locations
            .Include(s => s.Zones.Where(z => z.IsActive))
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<List<LocationZone>> GetZonesAsync(Guid locationId, CancellationToken ct = default) =>
        _db.LocationZones
            .Where(z => z.LocationId == locationId)
            .OrderBy(z => z.Name)
            .ToListAsync(ct);

    public Task<LocationZone?> GetZoneByIdAsync(Guid zoneId, CancellationToken ct = default) =>
        _db.LocationZones.FirstOrDefaultAsync(z => z.Id == zoneId, ct);

    public async Task AddAsync(Location location, CancellationToken ct = default) =>
        await _db.Locations.AddAsync(location, ct);

    public async Task AddZoneAsync(LocationZone zone, CancellationToken ct = default) =>
        await _db.LocationZones.AddAsync(zone, ct);

    public void Update(Location location) => _db.Locations.Update(location);

    public void UpdateZone(LocationZone zone) => _db.LocationZones.Update(zone);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
