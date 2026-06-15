using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Obsolete — replaced by LocationRepository. Retained only for backward compatibility.
/// Not registered in DI. Use ILocationRepository / LocationRepository instead.
/// </summary>
[Obsolete("Use LocationRepository instead.")]
public sealed class StoreRepository : IStoreRepository
{
    public StoreRepository(AppDbContext db) { }

    public Task<List<Store>> GetAllAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("Use ILocationRepository.GetAllAsync instead.");

    public Task<Store?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        throw new NotSupportedException("Use ILocationRepository.GetByIdAsync instead.");

    public Task<List<StoreZone>> GetZonesAsync(Guid storeId, CancellationToken ct = default) =>
        throw new NotSupportedException("Use ILocationRepository.GetZonesAsync instead.");

    public Task<StoreZone?> GetZoneByIdAsync(Guid zoneId, CancellationToken ct = default) =>
        throw new NotSupportedException("Use ILocationRepository.GetZoneByIdAsync instead.");

    public Task AddAsync(Store store, CancellationToken ct = default) =>
        throw new NotSupportedException("Use ILocationRepository.AddAsync instead.");

    public Task AddZoneAsync(StoreZone zone, CancellationToken ct = default) =>
        throw new NotSupportedException("Use ILocationRepository.AddZoneAsync instead.");

    public void Update(Store store) =>
        throw new NotSupportedException("Use ILocationRepository.Update instead.");

    public void UpdateZone(StoreZone zone) =>
        throw new NotSupportedException("Use ILocationRepository.UpdateZone instead.");

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        throw new NotSupportedException("Use ILocationRepository.SaveChangesAsync instead.");
}
