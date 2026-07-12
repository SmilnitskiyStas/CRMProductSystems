using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface INotificationRepository
{
    Task<IReadOnlyList<NotificationSetting>> GetSettingsByUserAsync(Guid userId, CancellationToken ct = default);
    Task UpsertSettingAsync(Guid userId, string eventType, string channel, bool isEnabled, CancellationToken ct = default);

    /// <summary>
    /// Filtered, paginated tenant notification history (ADR-018 §3/§4). Always excludes
    /// <c>Channel = 'system'</c> rows — those are undispatched outbox intents
    /// (<see cref="EnqueueAsync"/>), not real per-user notifications.
    /// </summary>
    Task<(IReadOnlyList<NotificationQueue> Items, int Total)> GetHistoryAsync(
        Guid tenantId,
        string? search,
        string? eventType,
        Guid? userId,
        Guid? storeId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts a <see cref="NotificationQueue"/> row. Used both for real per-user rows
    /// (<see cref="ShelfGuard.Application.Features.Notifications.NotificationService.SendTestAsync"/>)
    /// and for the Postgres outbox pattern (ADR-018 §2): backend services pass
    /// <c>UserId = null</c>, <c>Channel = "system"</c>, <c>Status = "pending"</c> to enqueue a
    /// broadcast-intent row that <c>worker/src/jobs/notification-dispatch.job.ts</c> later
    /// resolves into real per-user rows and marks <c>Status = "dispatched"</c>.
    /// </summary>
    Task EnqueueAsync(NotificationQueue item, CancellationToken ct = default);

    Task<NotificationQueue?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task MarkAsReadAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task MarkAsUnreadAsync(Guid id, Guid tenantId, CancellationToken ct = default);
    Task MarkAllAsReadAsync(Guid tenantId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid tenantId, CancellationToken ct = default);
}
