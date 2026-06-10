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

    public async Task<IReadOnlyList<NotificationQueue>> GetHistoryAsync(
        Guid tenantId, int limit, CancellationToken ct = default)
    {
        return await _db.NotificationQueues
            .Where(q => q.TenantId == tenantId)
            .OrderByDescending(q => q.CreatedAt)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task EnqueueAsync(NotificationQueue item, CancellationToken ct = default)
    {
        _db.NotificationQueues.Add(item);
        await _db.SaveChangesAsync(ct);
    }
}
