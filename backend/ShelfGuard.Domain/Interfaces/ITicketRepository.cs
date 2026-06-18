using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface ITicketRepository
{
    Task<(List<SupportTicket> Items, int Total)> GetPagedAsync(
        Guid tenantId,
        string? status,
        string? priority,
        Guid? assignedTo,
        Guid? createdBy,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<SupportTicket?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct);

    Task<SupportTicket?> GetByIdWithCommentsAsync(Guid id, Guid tenantId, CancellationToken ct);

    Task<SupportTicket> CreateAsync(SupportTicket ticket, CancellationToken ct);

    Task UpdateAsync(SupportTicket ticket, CancellationToken ct);

    Task<TicketComment> AddCommentAsync(TicketComment comment, CancellationToken ct);

    Task<List<SupportTicket>> GetMyTicketsAsync(Guid userId, Guid tenantId, CancellationToken ct);
}
