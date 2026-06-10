using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface INotificationRepository
{
    Task<IReadOnlyList<NotificationSetting>> GetSettingsByUserAsync(Guid userId, CancellationToken ct = default);
    Task UpsertSettingAsync(Guid userId, string eventType, string channel, bool isEnabled, CancellationToken ct = default);

    Task<IReadOnlyList<NotificationQueue>> GetHistoryAsync(Guid tenantId, int limit, CancellationToken ct = default);
    Task EnqueueAsync(NotificationQueue item, CancellationToken ct = default);
}
