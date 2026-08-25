namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A single message within a <see cref="ConsumerSupportTicket"/> (TASK-613). Exactly one
/// of <see cref="SenderConsumerAccountId"/>/<see cref="SenderUserId"/> is set per message
/// — the former for the consumer's own messages, the latter for staff replies — mirroring
/// how <see cref="SupplierSupportTicketMessage"/> distinguishes sender identity, but
/// across a consumer/staff boundary instead of two tenants.
/// </summary>
public sealed class ConsumerSupportTicketMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    /// <summary>Set when the consumer sent this message; null for staff replies.</summary>
    public Guid? SenderConsumerAccountId { get; set; }
    /// <summary>Set when staff sent this message; null for consumer messages.</summary>
    public Guid? SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public ConsumerSupportTicket? Ticket { get; init; }
}
