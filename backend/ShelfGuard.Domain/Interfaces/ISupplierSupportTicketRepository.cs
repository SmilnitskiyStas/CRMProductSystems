using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

/// <summary>
/// Repository for client → supplier support tickets (TASK-316). Two-tenant
/// RLS on tickets; messages inherit visibility via an EXISTS subquery on the
/// parent ticket (same pattern as supplier_chat_messages).
/// </summary>
public interface ISupplierSupportTicketRepository
{
    /// <summary>Loads a ticket including its messages (oldest first).</summary>
    Task<SupplierSupportTicket?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lists tickets on the supplier side, newest first.</summary>
    Task<IReadOnlyList<SupplierSupportTicket>> ListForSupplierAsync(
        Guid supplierTenantId, CancellationToken ct = default);

    /// <summary>Lists tickets on the client side, newest first.</summary>
    Task<IReadOnlyList<SupplierSupportTicket>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default);

    Task AddAsync(SupplierSupportTicket ticket, CancellationToken ct = default);

    void Update(SupplierSupportTicket ticket);

    Task AddMessageAsync(SupplierSupportTicketMessage message, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
