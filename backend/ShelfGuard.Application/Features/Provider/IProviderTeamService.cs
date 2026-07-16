using ShelfGuard.Application.Features.Provider.Dtos;

namespace ShelfGuard.Application.Features.Provider;

public interface IProviderTeamService
{
    Task<IReadOnlyList<ProviderTeamMemberDto>> GetTeamAsync(CancellationToken ct);

    /// <param name="actingRole">
    /// Caller's own role (from the JWT ClaimTypes.Role claim) — used to enforce that only an
    /// existing owner (role == "provider") can grant or protect the owner-level role. See
    /// TASK-363: previously any provider_admin could self-escalate to "provider" here.
    /// </param>
    Task<(ProviderTeamMemberDto? Member, string? Error)> InviteMemberAsync(InviteProviderMemberRequest req, string actingRole, CancellationToken ct);
    Task<(ProviderTeamMemberDto? Member, string? Error)> UpdateMemberAsync(Guid memberId, UpdateProviderMemberRequest req, string actingRole, CancellationToken ct);
    Task<(bool Success, string? Error)> DeactivateMemberAsync(Guid memberId, string actingRole, CancellationToken ct);
    Task<(bool Success, string? Error)> ReactivateMemberAsync(Guid memberId, string actingRole, CancellationToken ct);
}
