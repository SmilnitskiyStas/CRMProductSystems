namespace ShelfGuard.Application.Features.Schedules;

public record WorkScheduleDto(
    Guid Id,
    Guid LocationId,
    string LocationName,
    string Name,
    DateOnly WeekStart,
    string Status,
    int ShiftCount,
    DateTime CreatedAt);

public record WorkScheduleDetailDto(
    Guid Id,
    Guid LocationId,
    string LocationName,
    string Name,
    DateOnly WeekStart,
    string Status,
    List<ScheduleShiftDto> Shifts,
    DateTime CreatedAt);

public record ScheduleShiftDto(
    Guid Id,
    Guid UserId,
    string UserName,
    Guid LocationId,
    DateOnly ShiftDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    string? RoleOverride,
    string? Notes,
    string Status);

public record CreateWorkScheduleDto(
    Guid LocationId,
    string Name,
    DateOnly WeekStart);

public record UpdateWorkScheduleDto(
    string Name,
    string Status);

public record CreateShiftDto(
    Guid UserId,
    Guid LocationId,
    DateOnly ShiftDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes = 60,
    string? RoleOverride = null,
    string? Notes = null);

public record UpdateShiftDto(
    TimeOnly StartTime,
    TimeOnly EndTime,
    int BreakMinutes,
    string? RoleOverride,
    string? Notes,
    string Status);
