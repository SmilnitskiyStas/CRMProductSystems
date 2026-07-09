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
    Guid? LegalEntityId = null
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
    Guid? LegalEntityId = null
);

public sealed record UpdateLocationRequest(
    string Name,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string LocationType,
    bool IsActive,
    Guid? LegalEntityId = null
);

public sealed record UpdateFloorPlanRequest(
    string FloorPlan
);

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
