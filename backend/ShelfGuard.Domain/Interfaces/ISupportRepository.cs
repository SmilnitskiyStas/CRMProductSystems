using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ISupportTicketRepository
{
    Task<IReadOnlyList<SupportTicket>> GetByTenantAsync(Guid tenantId, string? status, CancellationToken ct);
    Task<IReadOnlyList<SupportTicket>> GetAllAsync(string? status, Guid? assignedTo, CancellationToken ct);
    Task<SupportTicket?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(SupportTicket ticket, CancellationToken ct);
    void Update(SupportTicket ticket);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface ISupportMessageRepository
{
    Task<IReadOnlyList<SupportMessage>> GetByTicketAsync(Guid ticketId, CancellationToken ct);
    Task AddAsync(SupportMessage message, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
