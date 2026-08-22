using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Domain.Entities;

/// <summary>
/// Support ticket opened by a client tenant to a supplier tenant (TASK-316).
/// Distinct from platform <c>support_tickets</c> (client → SaaS provider).
/// Visible to both tenant parties via the two-tenant RLS policy.
/// </summary>
public sealed class SupplierSupportTicket
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SupplierTenantId { get; set; }
    public Guid ClientTenantId { get; set; }
    public string Subject { get; set; } = string.Empty;
    /// <summary>See <see cref="SupplierSupportTicketStatus"/>.</summary>
    public string Status { get; set; } = SupplierSupportTicketStatus.Open;

    /// <summary>Client-side user who opened the ticket.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// Order this ticket was auto-opened for, when a receiving employee flags a discrepancy
    /// (damage, wrong qty) during mobile receiving (TASK-596). Null for manually-opened
    /// tickets unrelated to a specific order.
    /// </summary>
    public Guid? MarketplaceOrderId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SupplierSupportTicketMessage> Messages { get; init; } = new List<SupplierSupportTicketMessage>();
}
