using ShelfGuard.Domain.Entities;

namespace ShelfGuard.Domain.Interfaces;

public interface IProviderStatsRepository
{
    Task<IReadOnlyList<User>> GetProviderTeamMembersAsync(CancellationToken ct);
    Task<IReadOnlyList<(Guid AssignedTo, string Status, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt)>> GetAssignedTicketsAsync(IEnumerable<Guid> memberIds, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetCreatedByProviderTicketAuthorsAsync(IEnumerable<Guid> memberIds, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetTicketCommentAuthorsAsync(IEnumerable<Guid> memberIds, CancellationToken ct);
}
