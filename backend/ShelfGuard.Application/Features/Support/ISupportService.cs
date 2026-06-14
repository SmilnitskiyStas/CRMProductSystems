using ShelfGuard.Application.Features.Support.Dtos;

namespace ShelfGuard.Application.Features.Support;

public interface ISupportService
{
    // Client side
    Task<IReadOnlyList<SupportTicketDto>> GetTenantTicketsAsync(Guid tenantId, string? status, CancellationToken ct);
    Task<SupportTicketDto?>               GetTicketAsync(Guid ticketId, Guid tenantId, CancellationToken ct);
    Task<SupportTicketDto>                CreateTicketAsync(Guid tenantId, Guid userId, CreateTicketRequest req, CancellationToken ct);
    Task<SupportMessageDto>               AddClientMessageAsync(Guid ticketId, Guid tenantId, Guid userId, AddMessageRequest req, CancellationToken ct);

    // Provider side
    Task<IReadOnlyList<SupportTicketDto>> GetAllTicketsAsync(string? status, Guid? assignedTo, CancellationToken ct);
    Task<SupportTicketDto?>               GetTicketByIdAsync(Guid ticketId, CancellationToken ct);
    Task                                  AssignTicketAsync(Guid ticketId, Guid agentId, CancellationToken ct);
    Task                                  UpdateStatusAsync(Guid ticketId, string status, CancellationToken ct);
    Task<SupportMessageDto>               AddProviderMessageAsync(Guid ticketId, Guid senderId, AddMessageRequest req, CancellationToken ct);
}
