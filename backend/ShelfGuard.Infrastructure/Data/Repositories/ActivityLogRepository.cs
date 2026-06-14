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

    public async Task<(IReadOnlyList<ActivityLog> Items, int Total)> GetFilteredAsync(
        Guid?     tenantId,
        Guid?     userId,
        string?   action,
        DateTime? dateFrom,
        DateTime? dateTo,
        int       page,
        int       pageSize,
        CancellationToken ct = default)
    {
        var query = _db.ActivityLogs.AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(a => a.TenantId == tenantId);

        if (userId.HasValue)
            query = query.Where(a => a.UserId == userId);

        if (!string.IsNullOrEmpty(action))
            query = query.Where(a => a.Action == action);

        if (dateFrom.HasValue)
            query = query.Where(a => a.CreatedAt >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(a => a.CreatedAt <= dateTo.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task LogAsync(ActivityLog entry, CancellationToken ct = default) =>
        await _db.ActivityLogs.AddAsync(entry, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
