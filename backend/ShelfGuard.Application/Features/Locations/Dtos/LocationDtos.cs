namespace ShelfGuard.Application.Features.Locations.Dtos;

public sealed record LocationDto(
    Guid Id,
    string Name,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string LocationType,
    string? FloorPlan,
    bool IsActive,
    DateTime CreatedAt,
    List<LocationZoneDto> Zones,
    /// <summary>Optional legal entity this location is registered under (TASK-321).</summary>
    Guid? LegalEntityId = null,
    /// <summary>Structured Ukraine region code (ISO 3166-2:UA oblast or city code), nullable (TASK-658).</summary>
    string? RegionCode = null
);

public sealed record LocationZoneDto(
    Guid Id,
    Guid LocationId,
    string Name,
    string Type,
    string? Position,
    int ShelvesCount,
    decimal? TempMin,
    decimal? TempMax,
    bool IsActive
);

public sealed record CreateLocationRequest(
    string Name,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string LocationType,
    Guid? LegalEntityId = null,
    string? RegionCode = null
);

public sealed record UpdateLocationRequest(
    string Name,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string LocationType,
    bool IsActive,
    Guid? LegalEntityId = null,
    string? RegionCode = null
);

public sealed record UpdateFloorPlanRequest(
    string FloorPlan
);

/// <summary>Body for <c>POST /api/locations/geocode</c> — the free-text address to resolve.</summary>
public sealed record GeocodeAddressRequest(string Query);

/// <summary>Resolved coordinates for an address. The location form sends the lat/lon back via the normal update.</summary>
public sealed record GeocodeAddressResult(decimal Latitude, decimal Longitude, string DisplayName);

public sealed record CreateZoneRequest(
    string Name,
    string Type,
    string? Position,
    int ShelvesCount,
    decimal? TempMin,
    decimal? TempMax
);

public sealed record UpdateZoneRequest(
    string Name,
    string Type,
    string? Position,
    int ShelvesCount,
    decimal? TempMin,
    decimal? TempMax,
    bool IsActive
);
