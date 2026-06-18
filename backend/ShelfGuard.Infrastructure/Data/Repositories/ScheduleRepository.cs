using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ScheduleRepository : IScheduleRepository
{
    private readonly AppDbContext _db;

    public ScheduleRepository(AppDbContext db) => _db = db;

    public async Task<List<WorkSchedule>> GetSchedulesAsync(
        Guid tenantId, Guid? locationId, DateOnly? weekStart, CancellationToken ct)
    {
        var q = _db.WorkSchedules
            .Include(s => s.Location)
            .Include(s => s.Shifts)
            .Where(s => s.TenantId == tenantId);

        if (locationId.HasValue)
            q = q.Where(s => s.LocationId == locationId.Value);

        if (weekStart.HasValue)
            q = q.Where(s => s.WeekStart == weekStart.Value);

        return await q.OrderByDescending(s => s.WeekStart).ToListAsync(ct);
    }

    public async Task<WorkSchedule?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        await _db.WorkSchedules
            .Include(s => s.Location)
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);

    public async Task<WorkSchedule?> GetByIdWithShiftsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        await _db.WorkSchedules
            .Include(s => s.Location)
            .Include(s => s.Shifts)
                .ThenInclude(sh => sh.User)
            .Include(s => s.Shifts)
                .ThenInclude(sh => sh.Location)
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);

    public async Task<WorkSchedule> CreateScheduleAsync(WorkSchedule schedule, CancellationToken ct)
    {
        _db.WorkSchedules.Add(schedule);
        await _db.SaveChangesAsync(ct);
        return schedule;
    }

    public async Task UpdateScheduleAsync(WorkSchedule schedule, CancellationToken ct)
    {
        _db.WorkSchedules.Update(schedule);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteScheduleAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        var schedule = await _db.WorkSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);
        if (schedule is not null)
        {
            _db.WorkSchedules.Remove(schedule);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<ScheduleShift> AddShiftAsync(ScheduleShift shift, CancellationToken ct)
    {
        _db.ScheduleShifts.Add(shift);
        await _db.SaveChangesAsync(ct);
        return shift;
    }

    public async Task<ScheduleShift?> GetShiftByIdAsync(Guid shiftId, Guid tenantId, CancellationToken ct) =>
        await _db.ScheduleShifts
            .Include(sh => sh.User)
            .Include(sh => sh.Location)
            .FirstOrDefaultAsync(sh => sh.Id == shiftId && sh.TenantId == tenantId, ct);

    public async Task UpdateShiftAsync(ScheduleShift shift, CancellationToken ct)
    {
        _db.ScheduleShifts.Update(shift);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteShiftAsync(Guid shiftId, Guid tenantId, CancellationToken ct)
    {
        var shift = await _db.ScheduleShifts
            .FirstOrDefaultAsync(sh => sh.Id == shiftId && sh.TenantId == tenantId, ct);
        if (shift is not null)
        {
            _db.ScheduleShifts.Remove(shift);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<ScheduleShift>> GetShiftsByUserAsync(
        Guid userId, Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct) =>
        await _db.ScheduleShifts
            .Include(sh => sh.Location)
            .Where(sh => sh.UserId == userId
                      && sh.TenantId == tenantId
                      && sh.ShiftDate >= from
                      && sh.ShiftDate <= to)
            .OrderBy(sh => sh.ShiftDate)
            .ThenBy(sh => sh.StartTime)
            .ToListAsync(ct);
}
