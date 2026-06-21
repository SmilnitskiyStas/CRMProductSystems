namespace ShelfGuard.Domain.Entities;

public sealed class ChatSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = "open";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ChatMessage> Messages { get; init; } = new List<ChatMessage>();
}
