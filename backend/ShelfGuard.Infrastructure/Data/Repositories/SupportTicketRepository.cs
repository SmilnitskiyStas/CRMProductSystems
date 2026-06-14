using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class SupportTicketRepository(AppDbContext db) : ISupportTicketRepository
{
    public async Task<IReadOnlyList<SupportTicket>> GetByTenantAsync(
        Guid tenantId, string? status, CancellationToken ct)
    {
        var q = db.SupportTickets
            .Include(t => t.Messages)
            .Where(t => t.TenantId == tenantId);
        if (status is not null)
            q = q.Where(t => t.Status == status);
        return await q.OrderByDescending(t => t.UpdatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SupportTicket>> GetAllAsync(
        string? status, Guid? assignedTo, CancellationToken ct)
    {
        var q = db.SupportTickets.Include(t => t.Messages).AsQueryable();
        if (status is not null)
            q = q.Where(t => t.Status == status);
        if (assignedTo is not null)
            q = q.Where(t => t.AssignedTo == assignedTo);
        return await q.OrderByDescending(t => t.UpdatedAt).ToListAsync(ct);
    }

    public async Task<SupportTicket?> GetByIdAsync(Guid id, CancellationToken ct)
        => await db.SupportTickets
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task AddAsync(SupportTicket ticket, CancellationToken ct)
        => await db.SupportTickets.AddAsync(ticket, ct);

    public void Update(SupportTicket ticket)
        => db.SupportTickets.Update(ticket);

    public async Task SaveChangesAsync(CancellationToken ct)
        => await db.SaveChangesAsync(ct);
}
