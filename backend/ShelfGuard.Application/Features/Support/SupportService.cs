using ShelfGuard.Application.Features.Support.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Support;

public sealed class SupportService(
    ISupportTicketRepository  tickets,
    ISupportMessageRepository messages) : ISupportService
{
    // ── Client ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SupportTicketDto>> GetTenantTicketsAsync(
        Guid tenantId, string? status, CancellationToken ct)
    {
        var list = await tickets.GetByTenantAsync(tenantId, status, ct);
        return list.Select(Map).ToList();
    }

    public async Task<SupportTicketDto?> GetTicketAsync(
        Guid ticketId, Guid tenantId, CancellationToken ct)
    {
        var t = await tickets.GetByIdAsync(ticketId, ct);
        if (t is null || t.TenantId != tenantId) return null;
        return Map(t);
    }

    public async Task<SupportTicketDto> CreateTicketAsync(
        Guid tenantId, Guid userId, CreateTicketRequest req, CancellationToken ct)
    {
        var ticket = new SupportTicket
        {
            TenantId  = tenantId,
            CreatedBy = userId,
            Subject   = req.Subject,
            Priority  = req.Priority,
            Status    = SupportTicketStatus.Open
        };
        var firstMsg = new SupportMessage
        {
            TicketId          = ticket.Id,
            SenderId          = userId,
            IsProviderMessage = false,
            Body              = req.Body
        };
        ticket.Messages.Add(firstMsg);
        await tickets.AddAsync(ticket, ct);
        await tickets.SaveChangesAsync(ct);
        return Map(ticket);
    }

    public async Task<SupportMessageDto> AddClientMessageAsync(
        Guid ticketId, Guid tenantId, Guid userId, AddMessageRequest req, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(ticketId, ct)
            ?? throw new KeyNotFoundException("Ticket not found");
        if (ticket.TenantId != tenantId)
            throw new UnauthorizedAccessException();

        var msg = new SupportMessage
        {
            TicketId          = ticketId,
            SenderId          = userId,
            IsProviderMessage = false,
            Body              = req.Body
        };
        await messages.AddAsync(msg, ct);
        ticket.UpdatedAt = DateTime.UtcNow;
        tickets.Update(ticket);
        await messages.SaveChangesAsync(ct);
        return MapMsg(msg);
    }

    public async Task MarkReadByTenantAsync(Guid ticketId, Guid tenantId, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(ticketId, ct)
            ?? throw new KeyNotFoundException("Ticket not found");
        if (ticket.TenantId != tenantId) throw new UnauthorizedAccessException();
        ticket.LastReadByTenantAt = DateTime.UtcNow;
        tickets.Update(ticket);
        await tickets.SaveChangesAsync(ct);
    }

    // ── Provider ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SupportTicketDto>> GetAllTicketsAsync(
        string? status, Guid? assignedTo, CancellationToken ct)
    {
        var list = await tickets.GetAllAsync(status, assignedTo, ct);
        return list.Select(MapForProvider).ToList();
    }

    public async Task<SupportTicketDto?> GetTicketByIdAsync(Guid ticketId, CancellationToken ct)
    {
        var t = await tickets.GetByIdAsync(ticketId, ct);
        return t is null ? null : MapForProvider(t);
    }

    public async Task AssignTicketAsync(Guid ticketId, Guid agentId, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(ticketId, ct)
            ?? throw new KeyNotFoundException("Ticket not found");
        ticket.AssignedTo = agentId;
        ticket.UpdatedAt  = DateTime.UtcNow;
        if (ticket.Status == SupportTicketStatus.Open)
            ticket.Status = SupportTicketStatus.InProgress;
        tickets.Update(ticket);
        await tickets.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid ticketId, string status, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(ticketId, ct)
            ?? throw new KeyNotFoundException("Ticket not found");
        ticket.Status    = status;
        ticket.UpdatedAt = DateTime.UtcNow;
        tickets.Update(ticket);
        await tickets.SaveChangesAsync(ct);
    }

    public async Task<SupportMessageDto> AddProviderMessageAsync(
        Guid ticketId, Guid senderId, AddMessageRequest req, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(ticketId, ct)
            ?? throw new KeyNotFoundException("Ticket not found");
        var msg = new SupportMessage
        {
            TicketId          = ticketId,
            SenderId          = senderId,
            IsProviderMessage = true,
            Body              = req.Body
        };
        await messages.AddAsync(msg, ct);
        ticket.UpdatedAt = DateTime.UtcNow;
        if (ticket.Status == SupportTicketStatus.Open)
            ticket.Status = SupportTicketStatus.InProgress;
        tickets.Update(ticket);
        await messages.SaveChangesAsync(ct);
        return MapMsg(msg);
    }

    public async Task MarkReadByProviderAsync(Guid ticketId, CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(ticketId, ct)
            ?? throw new KeyNotFoundException("Ticket not found");
        ticket.LastReadByProviderAt = DateTime.UtcNow;
        tickets.Update(ticket);
        await tickets.SaveChangesAsync(ct);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static SupportTicketDto Map(SupportTicket t) => new(
        t.Id, t.TenantId, t.CreatedBy, t.AssignedTo,
        t.Subject, t.Status, t.Priority,
        t.CreatedAt, t.UpdatedAt,
        IsUnreadForTenant(t),
        t.Messages.OrderBy(m => m.CreatedAt).Select(MapMsg).ToList());

    private static bool IsUnreadForTenant(SupportTicket t)
    {
        var lastProviderMsg = t.Messages.Where(m => m.IsProviderMessage).MaxBy(m => m.CreatedAt);
        if (lastProviderMsg is null) return false;
        return lastProviderMsg.CreatedAt > (t.LastReadByTenantAt ?? DateTime.MinValue);
    }

    private static SupportTicketDto MapForProvider(SupportTicket t) => new(
        t.Id, t.TenantId, t.CreatedBy, t.AssignedTo,
        t.Subject, t.Status, t.Priority,
        t.CreatedAt, t.UpdatedAt,
        IsUnreadForProvider(t),
        t.Messages.OrderBy(m => m.CreatedAt).Select(MapMsg).ToList());

    private static bool IsUnreadForProvider(SupportTicket t)
    {
        var lastClientMsg = t.Messages.Where(m => !m.IsProviderMessage).MaxBy(m => m.CreatedAt);
        if (lastClientMsg is null) return false;
        return lastClientMsg.CreatedAt > (t.LastReadByProviderAt ?? DateTime.MinValue);
    }

    private static SupportMessageDto MapMsg(SupportMessage m) => new(
        m.Id, m.SenderId, m.IsProviderMessage, m.Body, m.CreatedAt);
}
