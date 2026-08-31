using ShelfGuard.Application.Features.Locations.Dtos;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Locations;

public sealed class LocationService : ILocationService
{
    /// <summary>
    /// Roles whose location list is narrowed to their user_locations assignments (TASK-401).
    /// Mirrors the ADR-022 restricted ranks: everything below enterprise_admin. Provider-team
    /// roles and enterprise_admin are deliberately absent — they always see the full tenant
    /// list. Defined here from Domain's AppRoles (not Infrastructure's AppPolicies) to keep
    /// the Application → Infrastructure dependency direction clean.
    /// </summary>
    private static readonly IReadOnlySet<string> StoreScopedRoles = new HashSet<string>
    {
        AppRoles.NetworkManager,
        AppRoles.StoreManager,
        AppRoles.Merchandiser,
        AppRoles.Storekeeper,
        AppRoles.Cashier,
        AppRoles.Staff,
    };

    private readonly ILocationRepository _repo;
    private readonly IUserLocationRepository _userLocations;

    public LocationService(ILocationRepository repo, IUserLocationRepository userLocations)
    {
        _repo = repo;
        _userLocations = userLocations;
    }

    public async Task<List<LocationDto>> GetAllAsync(
        Guid? tenantId, Guid? userId, string? role, CancellationToken ct = default)
    {
        var locations = await _repo.GetAllAsync(ct);

        if (role is not null && StoreScopedRoles.Contains(role)
            && tenantId is not null && userId is not null)
        {
            var assignedIds = await _userLocations.GetLocationIdsForUserAsync(
                tenantId.Value, userId.Value, ct);

            // Fail-open by design (transitional, ADR-022 Stage 3 rollout): a scoped user with
            // ZERO user_locations rows sees the full tenant list. Until the Stage 2 backfill
            // completes, unassigned users exist and the frontend StoreSelector takes stores[0]
            // and hides itself on an empty list — returning [] here would break their UI
            // entirely. Actual data protection comes from the RESTRICTIVE store_scope RLS
            // policies (Stage 3); this list filter is a cosmetic layer on top. Once backfill
            // is verified complete, this branch can be tightened to fail-closed.
            if (assignedIds.Count > 0)
            {
                var assigned = assignedIds.ToHashSet();
                locations = locations.Where(l => assigned.Contains(l.Id)).ToList();
            }
        }

        return locations.Select(ToDto).ToList();
    }

    public async Task<LocationDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var location = await _repo.GetByIdAsync(id, ct);
        return location is null ? null : ToDto(location);
    }

    public async Task<(LocationDto? Location, string? Error)> CreateAsync(
        Guid tenantId, CreateLocationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Location name is required.");

        if (!IsValidLocationType(request.LocationType))
            return (null, $"Invalid location type '{request.LocationType}'.");

        var (regionCode, regionError) = NormalizeRegionCode(request.RegionCode);
        if (regionError is not null)
            return (null, regionError);

        var location = new Location
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Address = request.Address?.Trim(),
            RegionCode = regionCode,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Type = request.LocationType,
        };

        await _repo.AddAsync(location, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(location), null);
    }

    public async Task<(LocationDto? Location, string? Error)> UpdateAsync(
        Guid id, UpdateLocationRequest request, CancellationToken ct = default)
    {
        var location = await _repo.GetByIdAsync(id, ct);
        if (location is null)
            return (null, "Location not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Location name is required.");

        if (!IsValidLocationType(request.LocationType))
            return (null, $"Invalid location type '{request.LocationType}'.");

        var (regionCode, regionError) = NormalizeRegionCode(request.RegionCode);
        if (regionError is not null)
            return (null, regionError);

        location.Name = request.Name.Trim();
        location.Address = request.Address?.Trim();
        location.RegionCode = regionCode;
        location.Latitude = request.Latitude;
        location.Longitude = request.Longitude;
        location.Type = request.LocationType;
        location.IsActive = request.IsActive;

        _repo.Update(location);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(location), null);
    }

    public async Task<(LocationDto? Location, string? Error)> UpdateFloorPlanAsync(
        Guid id, UpdateFloorPlanRequest request, CancellationToken ct = default)
    {
        var location = await _repo.GetByIdAsync(id, ct);
        if (location is null)
            return (null, "Location not found.");

        location.FloorPlan = request.FloorPlan;

        _repo.Update(location);
        await _repo.SaveChangesAsync(ct);

        return (ToDto(location), null);
    }

    public async Task<List<LocationZoneDto>> GetZonesAsync(Guid locationId, CancellationToken ct = default)
    {
        var zones = await _repo.GetZonesAsync(locationId, ct);
        return zones.Select(ToZoneDto).ToList();
    }

    public async Task<(LocationZoneDto? Zone, string? Error)> CreateZoneAsync(
        Guid locationId, CreateZoneRequest request, CancellationToken ct = default)
    {
        var location = await _repo.GetByIdAsync(locationId, ct);
        if (location is null)
            return (null, "Location not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Zone name is required.");

        if (!IsValidZoneType(request.Type))
            return (null, $"Invalid zone type '{request.Type}'. Valid: shelf, fridge, freezer, display, production, warehouse.");

        if (request.ShelvesCount < 1)
            return (null, "ShelvesCount must be at least 1.");

        var zone = new LocationZone
        {
            LocationId = locationId,
            Name = request.Name.Trim(),
            Type = request.Type,
            Position = request.Position,
            ShelvesCount = request.ShelvesCount,
            TempMin = request.TempMin,
            TempMax = request.TempMax,
        };

        await _repo.AddZoneAsync(zone, ct);
        await _repo.SaveChangesAsync(ct);

        return (ToZoneDto(zone), null);
    }

    public async Task<(LocationZoneDto? Zone, string? Error)> UpdateZoneAsync(
        Guid locationId, Guid zoneId, UpdateZoneRequest request, CancellationToken ct = default)
    {
        var zone = await _repo.GetZoneByIdAsync(zoneId, ct);
        if (zone is null || zone.LocationId != locationId)
            return (null, "Zone not found.");

        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Zone name is required.");

        if (!IsValidZoneType(request.Type))
            return (null, $"Invalid zone type '{request.Type}'. Valid: shelf, fridge, freezer, display, production, warehouse.");

        if (request.ShelvesCount < 1)
            return (null, "ShelvesCount must be at least 1.");

        zone.Name = request.Name.Trim();
        zone.Type = request.Type;
        zone.Position = request.Position;
        zone.ShelvesCount = request.ShelvesCount;
        zone.TempMin = request.TempMin;
        zone.TempMax = request.TempMax;
        zone.IsActive = request.IsActive;

        _repo.UpdateZone(zone);
        await _repo.SaveChangesAsync(ct);

        return (ToZoneDto(zone), null);
    }

    public async Task<(bool Success, string? Error)> DeleteZoneAsync(
        Guid locationId, Guid zoneId, CancellationToken ct = default)
    {
        var zone = await _repo.GetZoneByIdAsync(zoneId, ct);
        if (zone is null || zone.LocationId != locationId)
            return (false, "Zone not found.");

        zone.IsActive = false;
        _repo.UpdateZone(zone);
        await _repo.SaveChangesAsync(ct);

        return (true, null);
    }

    public async Task<bool> BelongsToTenantAsync(Guid tenantId, Guid locationId, CancellationToken ct = default)
    {
        var location = await _repo.GetByIdAsync(locationId, ct);
        return location is not null && location.TenantId == tenantId;
    }

    // ── mapping ────────────────────────────────────────────────────────────

    private static LocationDto ToDto(Location s) => new(
        s.Id,
        s.Name,
        s.Address,
        s.Latitude,
        s.Longitude,
        s.Type,
        s.FloorPlan,
        s.IsActive,
        s.CreatedAt,
        s.Zones.Where(z => z.IsActive).Select(ToZoneDto).ToList(),
        RegionCode: s.RegionCode
    );

    /// <summary>
    /// Trims a submitted region code and checks it against <see cref="UkraineRegions"/>.
    /// Blank/whitespace/null → <c>(null, null)</c> (region is optional). Unknown code →
    /// <c>(null, error)</c> for the standard 400 pattern.
    /// </summary>
    private static (string? Value, string? Error) NormalizeRegionCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);

        var code = raw.Trim();
        return UkraineRegions.IsValid(code)
            ? (code, null)
            : (null, $"Invalid region code '{code}'.");
    }

    private static LocationZoneDto ToZoneDto(LocationZone z) => new(
        z.Id,
        z.LocationId,
        z.Name,
        z.Type,
        z.Position,
        z.ShelvesCount,
        z.TempMin,
        z.TempMax,
        z.IsActive
    );

    private static bool IsValidLocationType(string type) =>
        type is "retail_store" or "warehouse" or "auto_service"
               or "office" or "production" or "restaurant"
               or "shop" or "central_warehouse" or "distribution";

    private static bool IsValidZoneType(string type) =>
        type is "shelf" or "fridge" or "freezer" or "display" or "production" or "warehouse";
}
