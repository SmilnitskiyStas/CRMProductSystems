using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Notifications.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repo;

    public NotificationService(INotificationRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<NotificationSettingDto>> GetSettingsAsync(
        Guid userId, CancellationToken ct = default)
    {
        var settings = await _repo.GetSettingsByUserAsync(userId, ct);
        return settings
            .Select(s => new NotificationSettingDto(s.Id, s.EventType, s.Channel, s.IsEnabled))
            .ToList();
    }

    public Task UpsertSettingAsync(
        Guid userId, UpsertNotificationSettingRequest request, CancellationToken ct = default)
    {
        ValidateEventType(request.EventType);
        ValidateChannel(request.Channel);
        return _repo.UpsertSettingAsync(userId, request.EventType, request.Channel, request.IsEnabled, ct);
    }

    public async Task<PagedResult<NotificationHistoryDto>> GetHistoryAsync(
        Guid tenantId, NotificationHistoryQuery query, CancellationToken ct = default)
    {
        var page = query.ClampedPage;
        var pageSize = query.ClampedPageSize;

        var (items, total) = await _repo.GetHistoryAsync(
            tenantId,
            query.Search,
            query.EventType,
            query.UserId,
            query.StoreId,
            query.DateFrom,
            query.DateTo,
            page,
            pageSize,
            ct);

        return new PagedResult<NotificationHistoryDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<NotificationHistoryDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var item = await _repo.GetByIdAsync(id, tenantId, ct);
        return item is null ? null : ToDto(item);
    }

    public Task MarkAsReadAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        => _repo.MarkAsReadAsync(id, tenantId, ct);

    public Task MarkAsUnreadAsync(Guid id, Guid tenantId, CancellationToken ct = default)
        => _repo.MarkAsUnreadAsync(id, tenantId, ct);

    public Task MarkAllAsReadAsync(Guid tenantId, CancellationToken ct = default)
        => _repo.MarkAllAsReadAsync(tenantId, ct);

    public Task<int> GetUnreadCountAsync(Guid tenantId, CancellationToken ct = default)
        => _repo.GetUnreadCountAsync(tenantId, ct);

    public Task SendTestAsync(
        Guid tenantId, Guid userId, TestNotificationRequest request, CancellationToken ct = default)
    {
        ValidateChannel(request.Channel);

        var item = new NotificationQueue
        {
            TenantId  = tenantId,
            UserId    = userId,
            Channel   = request.Channel,
            EventType = request.EventType,
            Payload   = $"{{\"message\":\"Тестове сповіщення\",\"eventType\":\"{request.EventType}\"}}",
            Status    = "pending",
        };

        return _repo.EnqueueAsync(item, ct);
    }

    // ── validation ────────────────────────────────────────────────────────────

    private static readonly HashSet<string> ValidEventTypes =
    [
        "stock.expiry_warning",
        "stock.expiry_critical",
        "stock.expired",
        "stock.needs_verification",
        "weekly_report",
        "iot.temp_alert",
        "iot.offline",
        "receipt.created",
        "order.replenishment_suggested",
        "supplier.message",
        "supplier_agreement.signed",
    ];

    private static readonly HashSet<string> ValidChannels =
        ["telegram", "push", "email", "webhook"];

    private static void ValidateEventType(string value)
    {
        if (!ValidEventTypes.Contains(value))
            throw new ArgumentException($"Unknown event type: {value}");
    }

    private static NotificationHistoryDto ToDto(NotificationQueue q) =>
        new(q.Id, q.EventType ?? string.Empty, q.Channel, q.Status, q.Payload, q.CreatedAt, q.IsRead, q.ReadAt,
            q.Title, q.StoreId, q.UserId);

    private static void ValidateChannel(string value)
    {
        if (!ValidChannels.Contains(value))
            throw new ArgumentException($"Unknown channel: {value}");
    }
}
