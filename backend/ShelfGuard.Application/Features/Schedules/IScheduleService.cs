namespace ShelfGuard.Application.Features.Schedules;

public interface IScheduleService
{
    Task<List<WorkScheduleDto>> GetSchedulesAsync(Guid tenantId, Guid? locationId, DateOnly? weekStart, CancellationToken ct = default);
    Task<WorkScheduleDetailDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task<(WorkScheduleDto? Schedule, string? Error)> CreateScheduleAsync(Guid tenantId, Guid createdBy, CreateWorkScheduleDto dto, CancellationToken ct = default);
    Task<(WorkScheduleDto? Schedule, string? Error)> UpdateScheduleAsync(Guid id, Guid tenantId, UpdateWorkScheduleDto dto, CancellationToken ct = default);
    Task<bool> DeleteScheduleAsync(Guid id, Guid tenantId, CancellationToken ct = default);

    Task<(ScheduleShiftDto? Shift, string? Error)> AddShiftAsync(Guid scheduleId, Guid tenantId, CreateShiftDto dto, CancellationToken ct = default);
    Task<(ScheduleShiftDto? Shift, string? Error)> UpdateShiftAsync(Guid scheduleId, Guid shiftId, Guid tenantId, UpdateShiftDto dto, CancellationToken ct = default);
    Task<bool> DeleteShiftAsync(Guid scheduleId, Guid shiftId, Guid tenantId, CancellationToken ct = default);

    Task<List<ScheduleShiftDto>> GetMyShiftsAsync(Guid userId, Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
