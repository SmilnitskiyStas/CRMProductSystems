namespace ShelfGuard.Domain.Entities;

public sealed class SupportMessage
{
    public Guid     Id                { get; init; } = Guid.NewGuid();
    public Guid     TicketId          { get; init; }
    public Guid     SenderId          { get; init; }
    public bool     IsProviderMessage { get; init; }
    public string   Body              { get; init; } = string.Empty;
    public DateTime CreatedAt         { get; init; } = DateTime.UtcNow;

    public SupportTicket Ticket { get; init; } = null!;
}
