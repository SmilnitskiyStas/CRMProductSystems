using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.ServiceDesk;

public sealed class ProviderTicketService : IProviderTicketService
{
    private readonly IProviderTicketRepository _repo;

    public ProviderTicketService(IProviderTicketRepository repo) => _repo = repo;

    public async Task<List<ProviderTicketListItemDto>> GetAllAsync(
        string? status,
        Guid? tenantId,
        CancellationToken ct = default)
    {
        var rows = await _repo.GetAllAsync(status, tenantId, ct);
        return rows.Select(r => ToListItemDto(r.Ticket, r.TenantName)).ToList();
    }

    public async Task<(ProviderTicketListItemDto? Ticket, string? Error)> CreateAsync(
        Guid providerUserId,
        CreateProviderTicketDto dto,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            return (null, "Title is required.");

        if (string.IsNullOrWhiteSpace(dto.Description))
            return (null, "Description is required.");

        var validCategories = new[]
        {
            SupportTicketCategory.General,
            SupportTicketCategory.Technical,
            SupportTicketCategory.Billing,
            SupportTicketCategory.FeatureRequest,
            SupportTicketCategory.Bug,
        };
        if (!string.IsNullOrWhiteSpace(dto.Category) && !validCategories.Contains(dto.Category))
            return (null, $"Invalid category '{dto.Category}'.");

        var validPriorities = new[]
        {
            SupportTicketPriority.Low,
            SupportTicketPriority.Medium,
            SupportTicketPriority.High,
            SupportTicketPriority.Critical,
        };
        if (!string.IsNullOrWhiteSpace(dto.Priority) && !validPriorities.Contains(dto.Priority))
            return (null, $"Invalid priority '{dto.Priority}'.");

        var ticket = new SupportTicket
        {
            TenantId          = dto.TargetTenantId,
            Title             = dto.Title.Trim(),
            Description       = dto.Description.Trim(),
            Category          = dto.Category,
            Priority          = dto.Priority,
            Status            = SupportTicketStatus.Open,
            CreatedBy         = providerUserId,
            CreatedByProvider = true,
        };

        await _repo.CreateAsync(ticket, ct);

        var row = await _repo.GetByIdAsync(ticket.Id, ct);
        if (row is null) return (null, "Unexpected error: ticket not found after creation.");

        return (ToListItemDto(row.Value.Ticket, row.Value.TenantName), null);
    }

    public async Task<ProviderTicketDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _repo.GetByIdWithCommentsAsync(id, ct);
        if (row is null) return null;

        return ToDetailDto(row.Value.Ticket, row.Value.TenantName);
    }

    public async Task<(TicketCommentDto? Comment, string? Error)> AddCommentAsync(
        Guid ticketId,
        Guid providerUserId,
        AddCommentDto dto,
        CancellationToken ct = default)
    {
        var row = await _repo.GetByIdAsync(ticketId, ct);
        if (row is null)
            return (null, "Ticket not found.");

        if (string.IsNullOrWhiteSpace(dto.Body))
            return (null, "Comment body is required.");

        var comment = new TicketComment
        {
            TenantId   = row.Value.Ticket.TenantId,
            TicketId   = ticketId,
            AuthorId   = providerUserId,
            Body       = dto.Body.Trim(),
            IsInternal = false,
        };

        var created = await _repo.AddCommentAsync(comment, ct);

        return (new TicketCommentDto(
            created.Id,
            created.AuthorId,
            created.Author?.FullName ?? string.Empty,
            created.Body,
            created.IsInternal,
            created.CreatedAt), null);
    }

    private static ProviderTicketDetailDto ToDetailDto(
        SupportTicket t, string tenantName) => new(
            Id:                t.Id,
            Number:            t.Number,
            TenantId:          t.TenantId,
            TenantName:        tenantName,
            Title:             t.Title,
            Description:       t.Description,
            Category:          t.Category,
            Priority:          t.Priority,
            Status:            t.Status,
            CreatedBy:         t.CreatedBy,
            CreatedByName:     t.CreatedByUser?.FullName ?? string.Empty,
            CreatedByProvider: t.CreatedByProvider,
            CreatedAt:         t.CreatedAt,
            Comments:          t.Comments
                .OrderBy(c => c.CreatedAt)
                .Select(c => new TicketCommentDto(
                    c.Id,
                    c.AuthorId,
                    c.Author?.FullName ?? string.Empty,
                    c.Body,
                    c.IsInternal,
                    c.CreatedAt))
                .ToList());

    private static ProviderTicketListItemDto ToListItemDto(
        SupportTicket t, string tenantName) => new(
            Id:                t.Id,
            Number:            t.Number,
            TenantId:          t.TenantId,
            TenantName:        tenantName,
            Title:             t.Title,
            Description:       t.Description,
            Category:          t.Category,
            Priority:          t.Priority,
            Status:            t.Status,
            CreatedBy:         t.CreatedBy,
            CreatedByName:     t.CreatedByUser?.FullName ?? string.Empty,
            CreatedByProvider: t.CreatedByProvider,
            CreatedAt:         t.CreatedAt,
            CommentCount:      t.Comments.Count);
}
