using Microsoft.EntityFrameworkCore;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Infrastructure.Data.Repositories;

/// <summary>
/// Cross-tenant repository for provider employee statistics.
/// Same RLS-bypass pattern as ProviderTicketRepository.
/// </summary>
public sealed class ProviderStatsRepository(AppDbContext db) : IProviderStatsRepository
{
    private static readonly string[] TeamRoles =
        [AppRoles.Provider, AppRoles.ProviderAdmin, AppRoles.ProviderAgent];

    public async Task<IReadOnlyList<User>> GetProviderTeamMembersAsync(CancellationToken ct) =>
        await db.Users
            .AsNoTracking()
            .Where(u => TeamRoles.Contains(u.Role))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<(Guid AssignedTo, string Status, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt)>> GetAssignedTicketsAsync(
        IEnumerable<Guid> memberIds, CancellationToken ct)
    {
        var ids = memberIds.ToList();
        var rows = await db.SupportTickets
            .AsNoTracking()
            .Where(t => t.AssignedTo.HasValue && ids.Contains(t.AssignedTo.Value))
            .Select(t => new { t.AssignedTo, t.Status, t.CreatedAt, t.ResolvedAt })
            .ToListAsync(ct);

        return rows.Select(r => (r.AssignedTo!.Value, r.Status, r.CreatedAt, r.ResolvedAt)).ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetCreatedByProviderTicketAuthorsAsync(
        IEnumerable<Guid> memberIds, CancellationToken ct)
    {
        var ids = memberIds.ToList();
        return await db.SupportTickets
            .AsNoTracking()
            .Where(t => t.CreatedByProvider && ids.Contains(t.CreatedBy))
            .Select(t => t.CreatedBy)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetTicketCommentAuthorsAsync(
        IEnumerable<Guid> memberIds, CancellationToken ct)
    {
        var ids = memberIds.ToList();
        return await db.TicketComments
            .AsNoTracking()
            .Where(c => ids.Contains(c.AuthorId))
            .Select(c => c.AuthorId)
            .ToListAsync(ct);
    }
}
