using ShelfGuard.Application.Common;

namespace ShelfGuard.Application.Features.Notifications.Dtos;

public sealed record NotificationSettingDto(
    Guid   Id,
    string EventType,
    string Channel,
    bool   IsEnabled
);

public sealed record UpsertNotificationSettingRequest(
    string EventType,
    string Channel,
    bool   IsEnabled
);

public sealed record NotificationHistoryDto(
    Guid      Id,
    string    EventType,
    string    Channel,
    string    Status,
    string?   Payload,
    DateTime  CreatedAt,
    bool      IsRead,
    DateTime? ReadAt,
    string?   Title,
    Guid?     StoreId,
    Guid?     UserId
);

/// <summary>
/// Filter + pagination parameters for GET /api/notifications/history (ADR-018 §3/§4).
/// <see cref="Search"/> runs against <c>Title</c> via <c>EF.Functions.ILike</c> to hit the
/// pg_trgm GIN index added in TASK-338.
/// </summary>
public sealed class NotificationHistoryQuery
{
    public string?   Search    { get; init; }
    public string?   EventType { get; init; }
    public Guid?     UserId    { get; init; }
    public Guid?     StoreId   { get; init; }
    public DateTime? DateFrom  { get; init; }
    public DateTime? DateTo    { get; init; }

    public int Page     { get; init; } = 1;
    public int PageSize { get; init; } = 50;

    /// <summary>Page clamped to [1, ∞). PageSize clamped to [1, 200] — same bounds as PagedQuery.</summary>
    public int ClampedPage => Math.Max(1, Page);
    public int ClampedPageSize => Math.Clamp(PageSize, 1, 200);
}

public sealed record TestNotificationRequest(
    string Channel,
    string EventType
);
