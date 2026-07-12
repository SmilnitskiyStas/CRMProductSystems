using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _db;

    public NotificationRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<NotificationSetting>> GetSettingsByUserAsync(
        Guid userId, CancellationToken ct = default)
    {
        return await _db.NotificationSettings
            .Where(s => s.UserId == userId)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task UpsertSettingAsync(
        Guid userId, string eventType, string channel, bool isEnabled, CancellationToken ct = default)
    {
        var existing = await _db.NotificationSettings
            .FirstOrDefaultAsync(
                s => s.UserId == userId && s.EventType == eventType && s.Channel == channel,
                ct);

        if (existing is not null)
        {
            existing.IsEnabled = isEnabled;
        }
        else
        {
            _db.NotificationSettings.Add(new NotificationSetting
            {
                UserId    = userId,
                EventType = eventType,
                Channel   = channel,
                IsEnabled = isEnabled,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<NotificationQueue> Items, int Total)> GetHistoryAsync(
        Guid tenantId,
        string? search,
        string? eventType,
        Guid? userId,
        Guid? storeId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        // Channel = 'system' rows are undispatched outbox intents (ADR-018 §1/§2) — never
        // real per-user notifications, so they must never leak into the UI history feed.
        var query = _db.NotificationQueues
            .Where(q => q.TenantId == tenantId && q.Channel != "system")
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            // ILike hits the pg_trgm GIN index on Title (TASK-338) — plain .Contains()/LIKE
            // translation is not guaranteed to.
            query = query.Where(q => q.Title != null && EF.Functions.ILike(q.Title, $"%{search}%"));

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(q => q.EventType == eventType);

        if (userId.HasValue)
            query = query.Where(q => q.UserId == userId);

        if (storeId.HasValue)
            query = query.Where(q => q.StoreId == storeId);

        if (dateFrom.HasValue)
            query = query.Where(q => q.CreatedAt >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(q => q.CreatedAt <= dateTo.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(q => q.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task EnqueueAsync(NotificationQueue item, CancellationToken ct = default)
    {
        _db.NotificationQueues.Add(item);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<NotificationQueue?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        return await _db.NotificationQueues
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == tenantId, ct);
    }

    public async Task MarkAsReadAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var item = await _db.NotificationQueues
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == tenantId, ct);
        if (item is null || item.IsRead) return;
        item.MarkRead();
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAsUnreadAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var item = await _db.NotificationQueues
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == tenantId, ct);
        if (item is null || !item.IsRead) return;
        item.MarkUnread();
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAllAsReadAsync(Guid tenantId, CancellationToken ct = default)
    {
        var items = await _db.NotificationQueues
            .Where(q => q.TenantId == tenantId && !q.IsRead)
            .ToListAsync(ct);
        foreach (var item in items) item.MarkRead();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> GetUnreadCountAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.NotificationQueues
            .CountAsync(q => q.TenantId == tenantId && !q.IsRead, ct);
    }
}
