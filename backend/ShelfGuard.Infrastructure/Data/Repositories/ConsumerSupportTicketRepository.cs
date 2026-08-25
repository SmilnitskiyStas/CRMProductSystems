using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Consumer → tenant support tickets (TASK-616). Mirrors
/// <see cref="SupplierSupportTicketRepository"/>: canonical tenant_isolation RLS on tickets
/// (plus a direct-column consumer_self_access policy — see the AddConsumerSupportTickets
/// migration); ticket messages inherit visibility via an EXISTS subquery on the parent ticket.
/// </summary>
public sealed class ConsumerSupportTicketRepository : IConsumerSupportTicketRepository
{
    private readonly AppDbContext _db;

    public ConsumerSupportTicketRepository(AppDbContext db) => _db = db;

    public Task<ConsumerSupportTicket?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.ConsumerSupportTickets
            .Include(t => t.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<(List<ConsumerSupportTicket> Items, int Total)> GetPagedForConsumerAsync(
        Guid consumerAccountId, Guid tenantId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.ConsumerSupportTickets.AsNoTracking()
            .Where(t => t.ConsumerAccountId == consumerAccountId && t.TenantId == tenantId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(List<ConsumerSupportTicket> Items, int Total)> GetPagedForTenantAsync(
        Guid tenantId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.ConsumerSupportTickets.AsNoTracking()
            .Where(t => t.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<int> CountOpenByCustomerIdAsync(Guid customerId, Guid tenantId, CancellationToken ct = default) =>
        _db.ConsumerSupportTickets.AsNoTracking()
            .Where(t => t.CustomerId == customerId && t.TenantId == tenantId
                && (t.Status == ConsumerSupportTicketStatus.Open || t.Status == ConsumerSupportTicketStatus.InProgress))
            .CountAsync(ct);

    public async Task AddAsync(ConsumerSupportTicket ticket, CancellationToken ct = default) =>
        await _db.ConsumerSupportTickets.AddAsync(ticket, ct);

    public void Update(ConsumerSupportTicket ticket) =>
        _db.ConsumerSupportTickets.Update(ticket);

    public async Task AddMessageAsync(ConsumerSupportTicketMessage message, CancellationToken ct = default) =>
        await _db.ConsumerSupportTicketMessages.AddAsync(message, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
