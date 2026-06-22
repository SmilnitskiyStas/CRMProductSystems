using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface INotificationRepository
{
    Task<IReadOnlyList<NotificationSetting>> GetSettingsByUserAsync(Guid userId, CancellationToken ct = default);
    Task UpsertSettingAsync(Guid userId, string eventType, string channel, bool isEnabled, CancellationToken ct = default);

    Task<IReadOnlyList<NotificationQueue>> GetHistoryAsync(Guid tenantId, int limit, CancellationToken ct = default);
    Task EnqueueAsync(NotificationQueue item, CancellationToken ct = default);

    Task<NotificationQueue?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task MarkAsReadAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task MarkAllAsReadAsync(Guid tenantId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid tenantId, CancellationToken ct = default);
}
