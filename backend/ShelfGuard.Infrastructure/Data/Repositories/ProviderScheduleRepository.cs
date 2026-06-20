using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ProviderScheduleRepository(AppDbContext db) : IProviderScheduleRepository
{
    public async Task<List<ProviderScheduleSlot>> GetAllAsync(Guid? userId, CancellationToken ct)
    {
        var query = db.ProviderScheduleSlots
            .AsNoTracking()
            .Include(s => s.User)
            .AsQueryable();

        if (userId.HasValue)
            query = query.Where(s => s.UserId == userId.Value);

        return await query
            .OrderBy(s => s.UserId)
            .ThenBy(s => s.DayOfWeek)
            .ThenBy(s => s.StartTime)
            .ToListAsync(ct);
    }

    public async Task<ProviderScheduleSlot?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await db.ProviderScheduleSlots.FindAsync([id], ct);

    public async Task AddAsync(ProviderScheduleSlot slot, CancellationToken ct) =>
        await db.ProviderScheduleSlots.AddAsync(slot, ct);

    public void Remove(ProviderScheduleSlot slot) =>
        db.ProviderScheduleSlots.Remove(slot);

    public Task SaveChangesAsync(CancellationToken ct) =>
        db.SaveChangesAsync(ct);
}
