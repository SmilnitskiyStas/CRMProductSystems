using ShelfGuard.Application.Common;

namespace ShelfGuard.Application.Features.ServiceDesk;

public interface ITicketService
{
    Task<PagedResult<TicketDto>> GetPagedAsync(
        Guid tenantId,
        string? status,
        string? priority,
        Guid? assignedTo,
        Guid? createdBy,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<List<TicketDto>> GetMyTicketsAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken ct = default);

    Task<TicketDetailDto?> GetByIdAsync(
        Guid id,
        Guid tenantId,
        Guid requestUserId,
        bool isManager,
        CancellationToken ct = default);

    Task<(TicketDto? Ticket, string? Error)> CreateAsync(
        Guid tenantId,
        Guid currentUserId,
        CreateTicketDto dto,
        CancellationToken ct = default);

    Task<(TicketDto? Ticket, string? Error)> UpdateAsync(
        Guid id,
        Guid tenantId,
        Guid currentUserId,
        bool isManager,
        UpdateTicketDto dto,
        CancellationToken ct = default);

    Task<(TicketCommentDto? Comment, string? Error)> AddCommentAsync(
        Guid ticketId,
        Guid tenantId,
        Guid currentUserId,
        bool isManager,
        AddCommentDto dto,
        CancellationToken ct = default);
}
