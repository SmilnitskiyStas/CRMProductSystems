using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for consumer → tenant support tickets (TASK-616). Mirrors
/// <see cref="ISupplierSupportTicketRepository"/>'s shape: canonical tenant RLS triad +
/// direct-column <c>consumer_self_access</c> on tickets (see the AddConsumerSupportTickets
/// migration); messages inherit visibility via an EXISTS subquery on the parent ticket.
/// </summary>
public interface IConsumerSupportTicketRepository
{
    /// <summary>Loads a ticket including its messages (oldest first). Tracked query — callers
    /// may mutate the returned entity (e.g. flipping Message.IsRead) and call
    /// <see cref="SaveChangesAsync"/> without a separate <see cref="Update"/> call.</summary>
    Task<ConsumerSupportTicket?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>A consumer's own tickets at one tenant, newest first (no messages).</summary>
    Task<(List<ConsumerSupportTicket> Items, int Total)> GetPagedForConsumerAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Staff inbox: every ticket at a tenant, newest first, optionally filtered by
    /// <see cref="ConsumerSupportTicket.Status"/> (no messages).</summary>
    Task<(List<ConsumerSupportTicket> Items, int Total)> GetPagedForTenantAsync(
        Guid tenantId, string? status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// TASK-618: count of a CRM <see cref="Entities.Customer"/>'s still-open tickets (Open or
    /// InProgress — see <see cref="ShelfGuard.Domain.Constants.ConsumerSupportTicketStatus"/>) —
    /// backs the Customers detail view. Resolved/Closed tickets don't count.
    /// </summary>
    Task<int> CountOpenByCustomerIdAsync(Guid customerId, Guid tenantId, CancellationToken ct = default);

    Task AddAsync(ConsumerSupportTicket ticket, CancellationToken ct = default);

    void Update(ConsumerSupportTicket ticket);

    Task AddMessageAsync(ConsumerSupportTicketMessage message, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
