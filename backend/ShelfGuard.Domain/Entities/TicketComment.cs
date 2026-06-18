namespace ShelfGuard.Domain.Entities;

public sealed class TicketComment
{
    public Guid   Id         { get; init; } = Guid.NewGuid();
    public Guid   TenantId   { get; init; }
    public Guid   TicketId   { get; init; }
    public Guid   AuthorId   { get; init; }
    public string Body       { get; init; } = string.Empty;

    /// <summary>Internal note — not visible to the ticket creator.</summary>
    public bool IsInternal { get; init; } = false;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    // Navigation
    public SupportTicket Ticket { get; init; } = null!;
    public User          Author { get; init; } = null!;
}
