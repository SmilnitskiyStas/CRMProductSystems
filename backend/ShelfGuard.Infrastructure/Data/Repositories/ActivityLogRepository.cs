using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class ActivityLogRepository : IActivityLogRepository
{
    private readonly AppDbContext _db;

    public ActivityLogRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ActivityLog>> GetByUserAsync(
        Guid tenantId, Guid userId,
        int limit = 50,
        CancellationToken ct = default) =>
        await _db.ActivityLogs
            .Where(a => a.TenantId == tenantId && a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActivityLog>> GetByTenantAsync(
        Guid tenantId,
        int limit = 50,
        CancellationToken ct = default) =>
        await _db.ActivityLogs
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActivityLog>> GetAllTenantsAsync(
        int limit = 100,
        CancellationToken ct = default) =>
        await _db.ActivityLogs
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task LogAsync(ActivityLog entry, CancellationToken ct = default) =>
        await _db.ActivityLogs.AddAsync(entry, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
