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
    DateTime  CreatedAt
);

public sealed record TestNotificationRequest(
    string Channel,
    string EventType
);
