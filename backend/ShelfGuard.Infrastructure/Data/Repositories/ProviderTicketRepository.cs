using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Cross-tenant repository for Provider Service Desk.
/// Queries run without tenant-scoped RLS — same pattern as TenantAdminRepository.
/// Only used by ProviderTicketService (roles: provider, provider_admin, provider_agent).
/// </summary>
public sealed class ProviderTicketRepository : IProviderTicketRepository
{
    private readonly AppDbContext _db;

    public ProviderTicketRepository(AppDbContext db) => _db = db;

    public async Task<List<(SupportTicket Ticket, string TenantName)>> GetAllAsync(
        string? status,
        Guid? tenantId,
        CancellationToken ct)
    {
        var query = _db.SupportTickets
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .Include(t => t.Comments)
            .Join(
                _db.Tenants,
                ticket => ticket.TenantId,
                tenant => tenant.Id,
                (ticket, tenant) => new { Ticket = ticket, TenantName = tenant.Name })
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Ticket.Status == status);

        if (tenantId.HasValue)
            query = query.Where(r => r.Ticket.TenantId == tenantId.Value);

        var rows = await query
            .OrderByDescending(r => r.Ticket.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(r => (r.Ticket, r.TenantName)).ToList();
    }

    public async Task<(SupportTicket Ticket, string TenantName)?> GetByIdAsync(
        Guid id, CancellationToken ct)
    {
        var row = await _db.SupportTickets
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .Include(t => t.Comments)
            .Join(
                _db.Tenants,
                ticket => ticket.TenantId,
                tenant => tenant.Id,
                (ticket, tenant) => new { Ticket = ticket, TenantName = tenant.Name })
            .FirstOrDefaultAsync(r => r.Ticket.Id == id, ct);

        if (row is null) return null;
        return (row.Ticket, row.TenantName);
    }

    public async Task<(SupportTicket Ticket, string TenantName)?> GetByIdWithCommentsAsync(
        Guid id, CancellationToken ct)
    {
        var row = await _db.SupportTickets
            .AsNoTracking()
            .Include(t => t.CreatedByUser)
            .Include(t => t.Comments).ThenInclude(c => c.Author)
            .Join(
                _db.Tenants,
                ticket => ticket.TenantId,
                tenant => tenant.Id,
                (ticket, tenant) => new { Ticket = ticket, TenantName = tenant.Name })
            .FirstOrDefaultAsync(r => r.Ticket.Id == id, ct);

        if (row is null) return null;
        return (row.Ticket, row.TenantName);
    }

    public async Task<SupportTicket> CreateAsync(SupportTicket ticket, CancellationToken ct)
    {
        await _db.SupportTickets.AddAsync(ticket, ct);
        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task<TicketComment> AddCommentAsync(TicketComment comment, CancellationToken ct)
    {
        await _db.TicketComments.AddAsync(comment, ct);
        await _db.SaveChangesAsync(ct);

        var withAuthor = await _db.TicketComments
            .AsNoTracking()
            .Include(c => c.Author)
            .FirstAsync(c => c.Id == comment.Id, ct);
        return withAuthor;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
