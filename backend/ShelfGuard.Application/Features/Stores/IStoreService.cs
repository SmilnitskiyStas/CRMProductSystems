using ShelfGuard.Application.Features.Stores.Dtos;

namespace ShelfGuard.Application.Features.Stores;

public interface IStoreService
{
    Task<List<StoreDto>> GetAllAsync(CancellationToken ct = default);
    Task<StoreDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(StoreDto? Store, string? Error)> CreateAsync(
        Guid tenantId, CreateStoreRequest request, CancellationToken ct = default);

    Task<(StoreDto? Store, string? Error)> UpdateAsync(
        Guid id, UpdateStoreRequest request, CancellationToken ct = default);

    Task<(StoreDto? Store, string? Error)> UpdateFloorPlanAsync(
        Guid id, UpdateFloorPlanRequest request, CancellationToken ct = default);

    Task<List<StoreZoneDto>> GetZonesAsync(Guid storeId, CancellationToken ct = default);

    Task<(StoreZoneDto? Zone, string? Error)> CreateZoneAsync(
        Guid storeId, CreateZoneRequest request, CancellationToken ct = default);

    Task<(StoreZoneDto? Zone, string? Error)> UpdateZoneAsync(
        Guid storeId, Guid zoneId, UpdateZoneRequest request, CancellationToken ct = default);

    Task<(bool Success, string? Error)> DeleteZoneAsync(
        Guid storeId, Guid zoneId, CancellationToken ct = default);
}
