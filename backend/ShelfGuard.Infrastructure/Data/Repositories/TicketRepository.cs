using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class TicketRepository : ITicketRepository
{
    private readonly AppDbContext _db;

    public TicketRepository(AppDbContext db) => _db = db;

    public async Task<(List<SupportTicket> Items, int Total)> GetPagedAsync(
        Guid tenantId,
        string? status,
        string? priority,
        Guid? assignedTo,
        Guid? createdBy,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var q = _db.SupportTickets
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.Location)
            .Include(t => t.Comments)
            .Where(t => t.TenantId == tenantId);

        if (status is not null)
            q = q.Where(t => t.Status == status);

        if (priority is not null)
            q = q.Where(t => t.Priority == priority);

        if (assignedTo.HasValue)
            q = q.Where(t => t.AssignedTo == assignedTo.Value);

        if (createdBy.HasValue)
            q = q.Where(t => t.CreatedBy == createdBy.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<SupportTicket?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        await _db.SupportTickets
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.Location)
            .Include(t => t.Comments)
                .ThenInclude(c => c.Author)
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

    public async Task<SupportTicket?> GetByIdWithCommentsAsync(Guid id, Guid tenantId, CancellationToken ct) =>
        await _db.SupportTickets
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.Location)
            .Include(t => t.Comments)
                .ThenInclude(c => c.Author)
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

    public async Task<SupportTicket> CreateAsync(SupportTicket ticket, CancellationToken ct)
    {
        _db.SupportTickets.Add(ticket);
        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task UpdateAsync(SupportTicket ticket, CancellationToken ct)
    {
        _db.SupportTickets.Update(ticket);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<TicketComment> AddCommentAsync(TicketComment comment, CancellationToken ct)
    {
        _db.TicketComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        // Reload with Author navigation
        await _db.Entry(comment).Reference(c => c.Author).LoadAsync(ct);
        return comment;
    }

    public async Task<List<SupportTicket>> GetMyTicketsAsync(
        Guid userId, Guid tenantId, CancellationToken ct) =>
        await _db.SupportTickets
            .Include(t => t.CreatedByUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.Location)
            .Include(t => t.Comments)
            .Where(t => t.TenantId == tenantId && t.CreatedBy == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
}
