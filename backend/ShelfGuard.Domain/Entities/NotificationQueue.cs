namespace ShelfGuard.Domain.Entities;

public sealed class NotificationQueue
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string? EventType { get; init; }
    public string? Payload { get; init; }
    public string Status { get; set; } = "pending";
    public int RetryCount { get; set; }
    public DateTime? SentAt { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }

    public void MarkRead()
    {
        IsRead = true;
        ReadAt = DateTime.UtcNow;
    }
}
