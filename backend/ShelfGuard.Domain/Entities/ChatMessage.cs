namespace ShelfGuard.Domain.Entities;

public sealed class ChatMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;
    public bool IsSystem { get; set; } = false;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public ChatSession? Session { get; init; }
}
