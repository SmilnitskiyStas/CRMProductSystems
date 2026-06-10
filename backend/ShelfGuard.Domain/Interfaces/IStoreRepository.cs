using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IStoreRepository
{
    Task<List<Store>> GetAllAsync(CancellationToken ct = default);
    Task<Store?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<StoreZone>> GetZonesAsync(Guid storeId, CancellationToken ct = default);
    Task<StoreZone?> GetZoneByIdAsync(Guid zoneId, CancellationToken ct = default);

    Task AddAsync(Store store, CancellationToken ct = default);
    Task AddZoneAsync(StoreZone zone, CancellationToken ct = default);
    void Update(Store store);
    void UpdateZone(StoreZone zone);
    Task SaveChangesAsync(CancellationToken ct = default);
}
