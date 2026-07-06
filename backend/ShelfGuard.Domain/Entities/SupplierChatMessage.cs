namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A single message within a <see cref="SupplierChatSession"/>. SenderTenantId lets
/// the frontend determine "mine vs. theirs" by comparing against the caller's own
/// tenant id, regardless of which side (supplier or client) is viewing the thread.
/// </summary>
public sealed class SupplierChatMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid SenderUserId { get; set; }
    public Guid SenderTenantId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public SupplierChatSession? Session { get; init; }
}
