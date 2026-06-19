using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Schedules;

public sealed class ScheduleService : IScheduleService
{
    private readonly IScheduleRepository _repo;

    public ScheduleService(IScheduleRepository repo) => _repo = repo;

    // ── Schedules ────────────────────────────────────────────────────────────

    public async Task<List<WorkScheduleDto>> GetSchedulesAsync(
        Guid tenantId, Guid? locationId, DateOnly? weekStart, CancellationToken ct = default)
    {
        var schedules = await _repo.GetSchedulesAsync(tenantId, locationId, weekStart, ct);
        return schedules.Select(ToDto).ToList();
    }

    public async Task<WorkScheduleDetailDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var schedule = await _repo.GetByIdWithShiftsAsync(id, tenantId, ct);
        return schedule is null ? null : ToDetailDto(schedule);
    }

    public async Task<(WorkScheduleDto? Schedule, string? Error)> CreateScheduleAsync(
        Guid tenantId, Guid createdBy, CreateWorkScheduleDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return (null, "Schedule name is required.");

        if (!await _repo.LocationExistsAsync(dto.LocationId, tenantId, ct))
            return (null, "Location not found.");

        var schedule = new WorkSchedule
        {
            TenantId   = tenantId,
            LocationId = dto.LocationId,
            Name       = dto.Name.Trim(),
            WeekStart  = dto.WeekStart,
            Status     = "draft",
            CreatedBy  = createdBy,
        };

        await _repo.CreateScheduleAsync(schedule, ct);

        // Reload with navigation to get LocationName
        var created = await _repo.GetByIdAsync(schedule.Id, tenantId, ct);
        return (ToDto(created!), null);
    }

    public async Task<(WorkScheduleDto? Schedule, string? Error)> UpdateScheduleAsync(
        Guid id, Guid tenantId, UpdateWorkScheduleDto dto, CancellationToken ct = default)
    {
        var schedule = await _repo.GetByIdWithShiftsAsync(id, tenantId, ct);
        if (schedule is null)
            return (null, "Schedule not found.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return (null, "Schedule name is required.");

        var validStatuses = new[] { "draft", "published", "archived" };
        if (!validStatuses.Contains(dto.Status))
            return (null, $"Invalid status '{dto.Status}'. Allowed: draft, published, archived.");

        // When publishing, validate that no employee has overlapping shifts on the same day
        if (dto.Status == "published" && schedule.Status != "published")
        {
            var conflict = DetectShiftConflicts(schedule.Shifts.ToList());
            if (conflict is not null)
                return (null, conflict);
        }

        schedule.Name      = dto.Name.Trim();
        schedule.Status    = dto.Status;
        schedule.UpdatedAt = DateTimeOffset.UtcNow;

        await _repo.UpdateScheduleAsync(schedule, ct);
        return (ToDto(schedule), null);
    }

    public async Task<bool> DeleteScheduleAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var schedule = await _repo.GetByIdAsync(id, tenantId, ct);
        if (schedule is null) return false;

        await _repo.DeleteScheduleAsync(id, tenantId, ct);
        return true;
    }

    // ── Shifts ───────────────────────────────────────────────────────────────

    public async Task<(ScheduleShiftDto? Shift, string? Error)> AddShiftAsync(
        Guid scheduleId, Guid tenantId, CreateShiftDto dto, CancellationToken ct = default)
    {
        var schedule = await _repo.GetByIdAsync(scheduleId, tenantId, ct);
        if (schedule is null)
            return (null, "Schedule not found.");

        if (!await _repo.LocationExistsAsync(dto.LocationId, tenantId, ct))
            return (null, "Location not found.");

        if (dto.EndTime <= dto.StartTime)
            return (null, "EndTime must be after StartTime.");

        if (dto.BreakMinutes < 0)
            return (null, "BreakMinutes cannot be negative.");

        var shift = new ScheduleShift
        {
            TenantId     = tenantId,
            ScheduleId   = scheduleId,
            UserId       = dto.UserId,
            LocationId   = dto.LocationId,
            ShiftDate    = dto.ShiftDate,
            StartTime    = dto.StartTime,
            EndTime      = dto.EndTime,
            BreakMinutes = dto.BreakMinutes,
            RoleOverride = dto.RoleOverride?.Trim(),
            Notes        = dto.Notes?.Trim(),
            Status       = "scheduled",
        };

        await _repo.AddShiftAsync(shift, ct);

        // Reload with navigation for response
        var created = await _repo.GetShiftByIdAsync(shift.Id, tenantId, ct);
        return (ToShiftDto(created!), null);
    }

    public async Task<(ScheduleShiftDto? Shift, string? Error)> UpdateShiftAsync(
        Guid scheduleId, Guid shiftId, Guid tenantId, UpdateShiftDto dto, CancellationToken ct = default)
    {
        var shift = await _repo.GetShiftByIdAsync(shiftId, tenantId, ct);
        if (shift is null || shift.ScheduleId != scheduleId)
            return (null, "Shift not found.");

        if (dto.EndTime <= dto.StartTime)
            return (null, "EndTime must be after StartTime.");

        if (dto.BreakMinutes < 0)
            return (null, "BreakMinutes cannot be negative.");

        var validStatuses = new[] { "scheduled", "confirmed", "completed", "cancelled" };
        if (!validStatuses.Contains(dto.Status))
            return (null, $"Invalid status '{dto.Status}'. Allowed: scheduled, confirmed, completed, cancelled.");

        shift.StartTime    = dto.StartTime;
        shift.EndTime      = dto.EndTime;
        shift.BreakMinutes = dto.BreakMinutes;
        shift.RoleOverride = dto.RoleOverride?.Trim();
        shift.Notes        = dto.Notes?.Trim();
        shift.Status       = dto.Status;

        await _repo.UpdateShiftAsync(shift, ct);
        return (ToShiftDto(shift), null);
    }

    public async Task<bool> DeleteShiftAsync(Guid scheduleId, Guid shiftId, Guid tenantId, CancellationToken ct = default)
    {
        var shift = await _repo.GetShiftByIdAsync(shiftId, tenantId, ct);
        if (shift is null || shift.ScheduleId != scheduleId) return false;

        await _repo.DeleteShiftAsync(shiftId, tenantId, ct);
        return true;
    }

    public async Task<List<ScheduleShiftDto>> GetMyShiftsAsync(
        Guid userId, Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var shifts = await _repo.GetShiftsByUserAsync(userId, tenantId, from, to, ct);
        return shifts.Select(ToShiftDto).ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects if any employee has two shifts on the same day that overlap in time.
    /// Returns an error message if a conflict is found, null otherwise.
    /// </summary>
    private static string? DetectShiftConflicts(List<Domain.Entities.ScheduleShift> shifts)
    {
        // Group by (userId, shiftDate)
        var groups = shifts
            .Where(s => s.Status != "cancelled")
            .GroupBy(s => (s.UserId, s.ShiftDate));

        foreach (var group in groups)
        {
            var list = group.OrderBy(s => s.StartTime).ToList();
            for (int i = 0; i < list.Count - 1; i++)
            {
                // Overlap: next shift starts before previous shift ends
                if (list[i + 1].StartTime < list[i].EndTime)
                {
                    return $"User {group.Key.UserId} has overlapping shifts on {group.Key.ShiftDate:yyyy-MM-dd}: " +
                           $"{list[i].StartTime}-{list[i].EndTime} overlaps with {list[i + 1].StartTime}-{list[i + 1].EndTime}.";
                }
            }
        }

        return null;
    }

    private static WorkScheduleDto ToDto(WorkSchedule s) => new(
        s.Id,
        s.LocationId,
        s.Location?.Name ?? string.Empty,
        s.Name,
        s.WeekStart,
        s.Status,
        s.Shifts.Count,
        s.CreatedAt.UtcDateTime);

    private static WorkScheduleDetailDto ToDetailDto(WorkSchedule s) => new(
        s.Id,
        s.LocationId,
        s.Location?.Name ?? string.Empty,
        s.Name,
        s.WeekStart,
        s.Status,
        s.Shifts.OrderBy(sh => sh.ShiftDate).ThenBy(sh => sh.StartTime).Select(ToShiftDto).ToList(),
        s.CreatedAt.UtcDateTime);

    private static ScheduleShiftDto ToShiftDto(Domain.Entities.ScheduleShift sh) => new(
        sh.Id,
        sh.UserId,
        sh.User?.FullName ?? string.Empty,
        sh.LocationId,
        sh.ShiftDate,
        sh.StartTime,
        sh.EndTime,
        sh.BreakMinutes,
        sh.RoleOverride,
        sh.Notes,
        sh.Status);
}
