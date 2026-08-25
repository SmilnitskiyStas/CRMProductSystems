using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Support ticket opened by a consumer (<see cref="ConsumerAccount"/>) to a tenant
/// (TASK-613) — mirrors the <see cref="SupplierSupportTicket"/>/
/// <see cref="SupplierSupportTicketMessage"/> shape, but for consumer↔tenant instead of
/// tenant↔supplier. Distinct from ServiceDesk (tenant↔provider) and
/// <see cref="SupplierSupportTicket"/> (tenant↔supplier) — three separate relationships,
/// three separate tables.
/// </summary>
public sealed class ConsumerSupportTicket
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ConsumerAccountId { get; set; }
    /// <summary>Auto-linked CRM customer record within this tenant, when one exists. Nullable/SetNull.</summary>
    public Guid? CustomerId { get; set; }
    public string Subject { get; set; } = string.Empty;
    /// <summary>See <see cref="ConsumerSupportTicketStatus"/>.</summary>
    public string Status { get; set; } = ConsumerSupportTicketStatus.Open;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ConsumerSupportTicketMessage> Messages { get; init; } = new List<ConsumerSupportTicketMessage>();
}
