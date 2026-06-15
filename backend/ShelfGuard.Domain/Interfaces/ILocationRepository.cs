using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ILocationRepository
{
    Task<List<Location>> GetAllAsync(CancellationToken ct = default);
    Task<Location?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<LocationZone>> GetZonesAsync(Guid locationId, CancellationToken ct = default);
    Task<LocationZone?> GetZoneByIdAsync(Guid zoneId, CancellationToken ct = default);

    Task AddAsync(Location location, CancellationToken ct = default);
    Task AddZoneAsync(LocationZone zone, CancellationToken ct = default);
    void Update(Location location);
    void UpdateZone(LocationZone zone);
    Task SaveChangesAsync(CancellationToken ct = default);
}
