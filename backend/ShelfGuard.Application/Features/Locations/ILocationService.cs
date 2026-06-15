using ShelfGuard.Application.Features.Locations.Dtos;

namespace ShelfGuard.Application.Features.Locations;

public interface ILocationService
{
    Task<List<LocationDto>> GetAllAsync(CancellationToken ct = default);
    Task<LocationDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(LocationDto? Location, string? Error)> CreateAsync(
        Guid tenantId, CreateLocationRequest request, CancellationToken ct = default);

    Task<(LocationDto? Location, string? Error)> UpdateAsync(
        Guid id, UpdateLocationRequest request, CancellationToken ct = default);

    Task<(LocationDto? Location, string? Error)> UpdateFloorPlanAsync(
        Guid id, UpdateFloorPlanRequest request, CancellationToken ct = default);

    Task<List<LocationZoneDto>> GetZonesAsync(Guid locationId, CancellationToken ct = default);

    Task<(LocationZoneDto? Zone, string? Error)> CreateZoneAsync(
        Guid locationId, CreateZoneRequest request, CancellationToken ct = default);

    Task<(LocationZoneDto? Zone, string? Error)> UpdateZoneAsync(
        Guid locationId, Guid zoneId, UpdateZoneRequest request, CancellationToken ct = default);

    Task<(bool Success, string? Error)> DeleteZoneAsync(
        Guid locationId, Guid zoneId, CancellationToken ct = default);
}
