using ShelfGuard.Application.Common;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.ServiceDesk;

public sealed class TicketService : ITicketService
{
    private readonly ITicketRepository _repo;

    public TicketService(ITicketRepository repo) => _repo = repo;

    // ── List (managers only) ─────────────────────────────────────────────────

    public async Task<PagedResult<TicketDto>> GetPagedAsync(
        Guid tenantId,
        string? status,
        string? priority,
        Guid? assignedTo,
        Guid? createdBy,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var clampedPage     = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 200);

        var (items, total) = await _repo.GetPagedAsync(
            tenantId, status, priority, assignedTo, createdBy,
            clampedPage, clampedPageSize, ct);

        return new PagedResult<TicketDto>
        {
            Items      = items.Select(ToDto).ToList(),
            TotalCount = total,
            Page       = clampedPage,
            PageSize   = clampedPageSize,
        };
    }

    // ── My tickets ───────────────────────────────────────────────────────────

    public async Task<List<TicketDto>> GetMyTicketsAsync(
        Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var tickets = await _repo.GetMyTicketsAsync(userId, tenantId, ct);
        return tickets.Select(ToDto).ToList();
    }

    // ── Get by id ────────────────────────────────────────────────────────────

    public async Task<TicketDetailDto?> GetByIdAsync(
        Guid id,
        Guid tenantId,
        Guid requestUserId,
        bool isManager,
        CancellationToken ct = default)
    {
        var ticket = await _repo.GetByIdWithCommentsAsync(id, tenantId, ct);
        if (ticket is null) return null;

        // Regular user can only view their own tickets
        if (!isManager && ticket.CreatedBy != requestUserId)
            return null;

        return ToDetailDto(ticket, isManager);
    }

    // ── Create ───────────────────────────────────────────────────────────────

    public async Task<(TicketDto? Ticket, string? Error)> CreateAsync(
        Guid tenantId,
        Guid currentUserId,
        CreateTicketDto dto,
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
            return (null, $"Invalid category '{dto.Category}'. Allowed: {string.Join(", ", validCategories)}.");

        var validPriorities = new[]
        {
            SupportTicketPriority.Low,
            SupportTicketPriority.Medium,
            SupportTicketPriority.High,
            SupportTicketPriority.Critical,
        };
        if (!string.IsNullOrWhiteSpace(dto.Priority) && !validPriorities.Contains(dto.Priority))
            return (null, $"Invalid priority '{dto.Priority}'. Allowed: {string.Join(", ", validPriorities)}.");

        var ticket = new SupportTicket
        {
            TenantId    = tenantId,
            Title       = dto.Title.Trim(),
            Description = dto.Description.Trim(),
            Category    = dto.Category,
            Priority    = dto.Priority,
            Status      = SupportTicketStatus.Open,
            CreatedBy   = currentUserId,
            LocationId  = dto.LocationId,
        };

        var created = await _repo.CreateAsync(ticket, ct);

        // Reload with navigation for response
        var loaded = await _repo.GetByIdAsync(created.Id, tenantId, ct);
        return (ToDto(loaded!), null);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    public async Task<(TicketDto? Ticket, string? Error)> UpdateAsync(
        Guid id,
        Guid tenantId,
        Guid currentUserId,
        bool isManager,
        UpdateTicketDto dto,
        CancellationToken ct = default)
    {
        var ticket = await _repo.GetByIdAsync(id, tenantId, ct);
        if (ticket is null)
            return (null, "Ticket not found.");

        // Permission check
        if (!isManager)
        {
            if (ticket.CreatedBy != currentUserId)
                return (null, "Access denied.");

            // Regular user can only update title/description/category while status is open
            if (ticket.Status != SupportTicketStatus.Open)
                return (null, "Cannot update a ticket that is not in 'open' status.");

            // Apply only allowed fields
            if (dto.Title is not null)
                ticket.Title = dto.Title.Trim();
            if (dto.Description is not null)
                ticket.Description = dto.Description.Trim();
            if (dto.Category is not null)
                ticket.Category = dto.Category;

            ticket.UpdatedAt = DateTimeOffset.UtcNow;
            await _repo.UpdateAsync(ticket, ct);
            return (ToDto(ticket), null);
        }

        // Manager: can update all fields
        if (dto.Title is not null)
            ticket.Title = dto.Title.Trim();
        if (dto.Description is not null)
            ticket.Description = dto.Description.Trim();
        if (dto.Category is not null)
            ticket.Category = dto.Category;
        if (dto.Priority is not null)
            ticket.Priority = dto.Priority;
        if (dto.AssignedTo.HasValue)
            ticket.AssignedTo = dto.AssignedTo;

        if (dto.Status is not null && ticket.Status != dto.Status)
        {
            var validStatuses = new[]
            {
                SupportTicketStatus.Open,
                SupportTicketStatus.InProgress,
                SupportTicketStatus.Waiting,
                SupportTicketStatus.Resolved,
                SupportTicketStatus.Closed,
            };
            if (!validStatuses.Contains(dto.Status))
                return (null, $"Invalid status '{dto.Status}'.");

            ticket.Status = dto.Status;

            if (dto.Status == SupportTicketStatus.Resolved)
                ticket.ResolvedAt = DateTimeOffset.UtcNow;
        }

        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await _repo.UpdateAsync(ticket, ct);

        // Reload with navigation for fresh names
        var reloaded = await _repo.GetByIdAsync(ticket.Id, tenantId, ct);
        return (ToDto(reloaded!), null);
    }

    // ── Add comment ──────────────────────────────────────────────────────────

    public async Task<(TicketCommentDto? Comment, string? Error)> AddCommentAsync(
        Guid ticketId,
        Guid tenantId,
        Guid currentUserId,
        bool isManager,
        AddCommentDto dto,
        CancellationToken ct = default)
    {
        var ticket = await _repo.GetByIdAsync(ticketId, tenantId, ct);
        if (ticket is null)
            return (null, "Ticket not found.");

        // Regular user can only comment on their own tickets
        if (!isManager && ticket.CreatedBy != currentUserId)
            return (null, "Access denied.");

        // IsInternal is manager-only
        if (dto.IsInternal && !isManager)
            return (null, "Internal notes are only available to managers.");

        if (string.IsNullOrWhiteSpace(dto.Body))
            return (null, "Comment body is required.");

        var comment = new TicketComment
        {
            TenantId   = tenantId,
            TicketId   = ticketId,
            AuthorId   = currentUserId,
            Body       = dto.Body.Trim(),
            IsInternal = dto.IsInternal,
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

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static TicketDto ToDto(SupportTicket t) => new(
        t.Id,
        t.Number,
        t.Title,
        t.Description,
        t.Category,
        t.Priority,
        t.Status,
        t.CreatedBy,
        t.CreatedByUser?.FullName ?? string.Empty,
        t.AssignedTo,
        t.AssignedToUser?.FullName,
        t.LocationId,
        t.Location?.Name,
        t.ResolvedAt,
        t.CreatedAt,
        t.Comments.Count);

    private static TicketDetailDto ToDetailDto(SupportTicket t, bool isManager) => new(
        t.Id,
        t.Number,
        t.Title,
        t.Description,
        t.Category,
        t.Priority,
        t.Status,
        t.CreatedBy,
        t.CreatedByUser?.FullName ?? string.Empty,
        t.AssignedTo,
        t.AssignedToUser?.FullName,
        t.LocationId,
        t.Location?.Name,
        t.ResolvedAt,
        t.CreatedAt,
        t.Comments
            .Where(c => isManager || !c.IsInternal)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new TicketCommentDto(
                c.Id,
                c.AuthorId,
                c.Author?.FullName ?? string.Empty,
                c.Body,
                c.IsInternal,
                c.CreatedAt))
            .ToList());
}
