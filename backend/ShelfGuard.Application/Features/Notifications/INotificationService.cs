using ShelfGuard.Application.Features.Notifications.Dtos;

namespace ShelfGuard.Application.Features.Notifications;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationSettingDto>> GetSettingsAsync(Guid userId, CancellationToken ct = default);
    Task UpsertSettingAsync(Guid userId, UpsertNotificationSettingRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<NotificationHistoryDto>> GetHistoryAsync(Guid tenantId, CancellationToken ct = default);
    Task SendTestAsync(Guid tenantId, Guid userId, TestNotificationRequest request, CancellationToken ct = default);

    Task<NotificationHistoryDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task MarkAsReadAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task MarkAllAsReadAsync(Guid tenantId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid tenantId, CancellationToken ct = default);
}
