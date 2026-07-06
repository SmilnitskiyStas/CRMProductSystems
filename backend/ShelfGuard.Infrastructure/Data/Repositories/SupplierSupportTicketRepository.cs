using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Client → supplier support tickets (TASK-316). Two-tenant tenant_isolation
/// RLS on tickets; ticket messages inherit visibility via an EXISTS subquery
/// on the parent ticket (mirrors supplier_chat_messages).
/// </summary>
public sealed class SupplierSupportTicketRepository : ISupplierSupportTicketRepository
{
    private readonly AppDbContext _db;

    public SupplierSupportTicketRepository(AppDbContext db) => _db = db;

    public Task<SupplierSupportTicket?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.SupplierSupportTickets
            .Include(t => t.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<SupplierSupportTicket>> ListForSupplierAsync(
        Guid supplierTenantId, CancellationToken ct = default) =>
        await _db.SupplierSupportTickets.AsNoTracking()
            .Where(t => t.SupplierTenantId == supplierTenantId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SupplierSupportTicket>> ListForClientAsync(
        Guid clientTenantId, CancellationToken ct = default) =>
        await _db.SupplierSupportTickets.AsNoTracking()
            .Where(t => t.ClientTenantId == clientTenantId)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(SupplierSupportTicket ticket, CancellationToken ct = default) =>
        await _db.SupplierSupportTickets.AddAsync(ticket, ct);

    public void Update(SupplierSupportTicket ticket) =>
        _db.SupplierSupportTickets.Update(ticket);

    public async Task AddMessageAsync(SupplierSupportTicketMessage message, CancellationToken ct = default) =>
        await _db.SupplierSupportTicketMessages.AddAsync(message, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
