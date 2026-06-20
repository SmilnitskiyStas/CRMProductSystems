namespace ShelfGuard.Application.Features.Provider.Dtos;

public record ProviderScheduleSlotDto(
    Guid   Id,
    Guid   UserId,
    string UserFullName,
    string UserRole,
    byte   DayOfWeek,
    string StartTime,   // "HH:mm"
    string EndTime,     // "HH:mm"
    string? Notes,
    bool   IsActive
);

public record CreateProviderScheduleSlotRequest(
    Guid   UserId,
    byte   DayOfWeek,
    string StartTime,  // "HH:mm"
    string EndTime,    // "HH:mm"
    string? Notes
);