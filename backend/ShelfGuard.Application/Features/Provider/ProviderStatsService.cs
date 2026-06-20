using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Provider;

public sealed class ProviderStatsService(IProviderStatsRepository repo) : IProviderStatsService
{
    public async Task<IReadOnlyList<ProviderMemberStatsDto>> GetTeamStatsAsync(CancellationToken ct)
    {
        var members = await repo.GetProviderTeamMembersAsync(ct);
        var memberIds = members.Select(m => m.Id).ToList();

        var assignedTickets = await repo.GetAssignedTicketsAsync(memberIds, ct);
        var createdAuthors  = await repo.GetCreatedByProviderTicketAuthorsAsync(memberIds, ct);
        var commentAuthors  = await repo.GetTicketCommentAuthorsAsync(memberIds, ct);

        return members.Select(m =>
        {
            var assigned = assignedTickets.Where(t => t.AssignedTo == m.Id).ToList();
            var resolved = assigned.Where(t =>
                t.Status == SupportTicketStatus.Resolved ||
                t.Status == SupportTicketStatus.Closed).ToList();

            double? avgHours = null;
            if (resolved.Count > 0)
            {
                var durations = resolved
                    .Where(t => t.ResolvedAt.HasValue)
                    .Select(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalHours)
                    .ToList();
                if (durations.Count > 0)
                    avgHours = durations.Average();
            }

            return new ProviderMemberStatsDto(
                MemberId:                m.Id,
                FullName:                m.FullName,
                Role:                    m.Role,
                IsActive:                m.IsActive,
                TicketsAssigned:         assigned.Count,
                TicketsResolved:         resolved.Count,
                TicketsCreatedByProvider: createdAuthors.Count(id => id == m.Id),
                CommentsWritten:         commentAuthors.Count(id => id == m.Id),
                AvgResolutionHours:      avgHours.HasValue ? Math.Round(avgHours.Value, 1) : null
            );
        }).ToList();
    }
}
