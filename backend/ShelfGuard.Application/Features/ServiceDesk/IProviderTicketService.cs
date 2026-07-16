namespace ShelfGuard.Application.Features.ServiceDesk;

public interface IProviderTicketService
{
    Task<List<ProviderTicketListItemDto>> GetAllAsync(
        string? status,
        Guid? tenantId,
        CancellationToken ct = default);

    Task<(ProviderTicketListItemDto? Ticket, string? Error)> CreateAsync(
        Guid providerUserId,
        CreateProviderTicketDto dto,
        CancellationToken ct = default);

    Task<ProviderTicketDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(TicketCommentDto? Comment, string? Error)> AddCommentAsync(
        Guid ticketId,
        Guid providerUserId,
        AddCommentDto dto,
        CancellationToken ct = default);
}
