using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IScheduleRepository
{
    Task<List<WorkSchedule>> GetSchedulesAsync(Guid tenantId, Guid? locationId, DateOnly? weekStart, CancellationToken ct);
    Task<WorkSchedule?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<WorkSchedule?> GetByIdWithShiftsAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<WorkSchedule> CreateScheduleAsync(WorkSchedule schedule, CancellationToken ct);
    Task UpdateScheduleAsync(WorkSchedule schedule, CancellationToken ct);
    Task DeleteScheduleAsync(Guid id, Guid tenantId, CancellationToken ct);
    Task<ScheduleShift> AddShiftAsync(ScheduleShift shift, CancellationToken ct);
    Task<ScheduleShift?> GetShiftByIdAsync(Guid shiftId, Guid tenantId, CancellationToken ct);
    Task UpdateShiftAsync(ScheduleShift shift, CancellationToken ct);
    Task DeleteShiftAsync(Guid shiftId, Guid tenantId, CancellationToken ct);
    Task<List<ScheduleShift>> GetShiftsByUserAsync(Guid userId, Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct);
    Task<bool> LocationExistsAsync(Guid locationId, Guid tenantId, CancellationToken ct);
}
