namespace ShelfGuard.Domain.Entities;

public sealed class NotificationSetting
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    public User? User { get; init; }
}
