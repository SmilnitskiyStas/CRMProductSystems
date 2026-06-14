using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

public sealed class SupportMessageRepository(AppDbContext db) : ISupportMessageRepository
{
    public async Task<IReadOnlyList<SupportMessage>> GetByTicketAsync(Guid ticketId, CancellationToken ct)
        => await db.SupportMessages
            .Where(m => m.TicketId == ticketId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(SupportMessage message, CancellationToken ct)
        => await db.SupportMessages.AddAsync(message, ct);

    public async Task SaveChangesAsync(CancellationToken ct)
        => await db.SaveChangesAsync(ct);
}
