namespace ShelfGuard.Application.Features.Stores.Dtos;

public sealed record StoreDto(
    Guid Id,
    string Name,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string Type,
    string? FloorPlan,
    bool IsActive,
    DateTime CreatedAt,
    List<StoreZoneDto> Zones
);

public sealed record StoreZoneDto(
    Guid Id,
    Guid StoreId,
    string Name,
    string Type,
    string? Position,
    int ShelvesCount,
    decimal? TempMin,
    decimal? TempMax,
    bool IsActive
);

public sealed record CreateStoreRequest(
    string Name,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string Type
);

public sealed record UpdateStoreRequest(
    string Name,
    string? Address,
    decimal? Latitude,
    decimal? Longitude,
    string Type,
    bool IsActive
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
