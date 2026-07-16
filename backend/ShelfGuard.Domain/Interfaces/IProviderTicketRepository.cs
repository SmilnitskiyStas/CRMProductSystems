using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IProviderTicketRepository
{
    /// <summary>Returns tickets from ALL tenants (cross-tenant, no RLS filter).</summary>
    Task<List<(SupportTicket Ticket, string TenantName)>> GetAllAsync(
        string? status,
        Guid? tenantId,
        CancellationToken ct);

    Task<(SupportTicket Ticket, string TenantName)?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Same as <see cref="GetByIdAsync"/> but also eager-loads comments + comment authors.</summary>
    Task<(SupportTicket Ticket, string TenantName)?> GetByIdWithCommentsAsync(Guid id, CancellationToken ct);

    Task<SupportTicket> CreateAsync(SupportTicket ticket, CancellationToken ct);

    Task<TicketComment> AddCommentAsync(TicketComment comment, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
