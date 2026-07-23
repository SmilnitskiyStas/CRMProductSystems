using ShelfGuard.Application.Features.Locations.Dtos;

namespace ShelfGuard.Application.Features.Locations;

public interface ILocationService
{
    /// <summary>
    /// Lists locations visible to the acting user (TASK-401, ADR-022 Stage 3 companion).
    /// Admin-tier roles (provider team, enterprise_admin) see every tenant location; scoped
    /// roles (network_manager and below) see only their user_locations assignments — unless
    /// they have zero assignments, in which case the full list is returned (deliberate
    /// fail-open, see implementation comment).
    /// </summary>
    Task<List<LocationDto>> GetAllAsync(
        Guid? tenantId, Guid? userId, string? role, CancellationToken ct = default);
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

    /// <summary>Checks whether a Location id belongs to the given tenant (used for cross-feature FK validation, e.g. UserService's StoreId/user_locations validation — TASK-392b).</summary>
    Task<bool> BelongsToTenantAsync(Guid tenantId, Guid locationId, CancellationToken ct = default);
}
