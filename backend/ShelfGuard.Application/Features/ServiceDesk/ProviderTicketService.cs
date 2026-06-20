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

    private static ProviderTicketListItemDto ToListItemDto(
        SupportTicket t, string tenantName) => new(
            Id:                t.Id,
            Number:            t.Number,
            TenantId:          t.TenantId,
            TenantName:        tenantName,
            Title:             t.Title,
            Category:          t.Category,
            Priority:          t.Priority,
            Status:            t.Status,
            CreatedBy:         t.CreatedBy,
            CreatedByName:     t.CreatedByUser?.FullName ?? string.Empty,
            CreatedByProvider: t.CreatedByProvider,
            CreatedAt:         t.CreatedAt,
            CommentCount:      t.Comments.Count);
}
