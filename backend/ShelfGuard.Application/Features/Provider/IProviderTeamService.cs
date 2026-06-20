using ShelfGuard.Application.Features.Provider.Dtos;

namespace ShelfGuard.Application.Features.Provider;

public interface IProviderTeamService
{
    Task<IReadOnlyList<ProviderTeamMemberDto>> GetTeamAsync(CancellationToken ct);
    Task<(ProviderTeamMemberDto? Member, string? Error)> InviteMemberAsync(InviteProviderMemberRequest req, CancellationToken ct);
    Task<(ProviderTeamMemberDto? Member, string? Error)> UpdateMemberAsync(Guid memberId, UpdateProviderMemberRequest req, CancellationToken ct);
    Task<bool> DeactivateMemberAsync(Guid memberId, CancellationToken ct);
    Task<bool> ReactivateMemberAsync(Guid memberId, CancellationToken ct);
}
