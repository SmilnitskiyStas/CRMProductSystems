namespace ShelfGuard.Domain.Entities;

/// <summary>
/// A single message within a <see cref="SupplierSupportTicket"/> (TASK-316).
/// SenderTenantId lets the frontend derive "mine vs. theirs" by comparing
/// against the caller's own tenant id, mirroring SupplierChatMessage.
/// </summary>
public sealed class SupplierSupportTicketMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Guid SenderTenantId { get; set; }
    public Guid SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public SupplierSupportTicket? Ticket { get; init; }
}
