namespace ShelfGuard.Application.Features.Events.Dtos;

public sealed record EventCoefficientDto(
    Guid Id,
    string ScopeType,
    Guid? ScopeId,
    decimal Coefficient,
    string Source
);

public sealed record DemandEventDto(
    Guid Id,
    string Name,
    string EventType,
    string Scope,
    Guid? StoreId,
    string StartsAt,  // yyyy-MM-dd
    string EndsAt,
    bool IsRecurring,
    string? Notes,
    List<EventCoefficientDto> Coefficients
);

public sealed record UpsertEventRequest(
    string Name,
    string EventType,
    string Scope,
    Guid? StoreId,
    DateOnly StartsAt,
    DateOnly EndsAt,
    bool IsRecurring,
    string? Notes
);

public sealed record CreateCoefficientRequest(
    string ScopeType,
    Guid? ScopeId,
    decimal Coefficient
);

public sealed record UpdateCoefficientRequest(decimal Coefficient);

public sealed record SeedDefaultsResult(int EventsCreated, int CoefficientsCreated);
