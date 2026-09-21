using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ILocationRepository
{
    Task<List<Location>> GetAllAsync(CancellationToken ct = default);
    Task<Location?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<LocationZone>> GetZonesAsync(Guid locationId, CancellationToken ct = default);
    Task<LocationZone?> GetZoneByIdAsync(Guid zoneId, CancellationToken ct = default);

    /// <summary>
    /// TASK-714: zone lookup with its owning <see cref="Location"/> included, so callers can
    /// tenant-guard a zone (<c>zone.Location.TenantId</c>) without a second query. A new sibling
    /// method rather than widening <see cref="GetZoneByIdAsync"/>, which is reused by
    /// update/delete and shouldn't gain a bigger include graph.
    /// </summary>
    Task<LocationZone?> GetZoneWithLocationAsync(Guid zoneId, CancellationToken ct = default);

    Task AddAsync(Location location, CancellationToken ct = default);
    Task AddZoneAsync(LocationZone zone, CancellationToken ct = default);
    void Update(Location location);
    void UpdateZone(LocationZone zone);
    Task SaveChangesAsync(CancellationToken ct = default);
}
