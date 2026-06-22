using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Provider;

public sealed class ProviderTeamService(
    IUserRepository users,
    IPasswordHasher hasher) : IProviderTeamService
{
    private static readonly string[] TeamRoles =
        [AppRoles.Provider, AppRoles.ProviderAdmin, AppRoles.ProviderAgent];

    public async Task<IReadOnlyList<ProviderTeamMemberDto>> GetTeamAsync(CancellationToken ct)
    {
        var members = await users.GetByRolesAsync(TeamRoles, ct);
        return members.Select(Map).ToList();
    }

    public async Task<(ProviderTeamMemberDto? Member, string? Error)> InviteMemberAsync(
        InviteProviderMemberRequest req, CancellationToken ct)
    {
        if (!AppRoles.ProviderTeamRoles.Contains(req.Role))
            return (null, $"Role '{req.Role}' is not a valid provider role.");

        var existing = await users.GetByEmailAsync(req.Email.ToLowerInvariant(), ct);
        if (existing is not null && existing.IsActive)
            return (null, $"Email '{req.Email}' is already registered.");

        var password = !string.IsNullOrWhiteSpace(req.Password)
            ? req.Password
            : Guid.NewGuid().ToString("N")[..12];
        var hash = hasher.Hash(password);

        var user = User.Create(
            tenantId:      null,
            email:         req.Email.Trim().ToLowerInvariant(),
            fullName:      req.FullName.Trim(),
            passwordHash:  hash,
            role:          req.Role,
            storeId:       null,
            invitedByName: "Provider");

        if (req.ProviderRoleId.HasValue)
            user.SetProviderRole(req.ProviderRoleId);

        if (req.PermissionsOverride is { Count: > 0 })
            user.SetPermissions(req.PermissionsOverride);

        await users.AddAsync(user, ct);
        await users.SaveChangesAsync(ct);

        return (Map(user), null);
    }

    public async Task<(ProviderTeamMemberDto? Member, string? Error)> UpdateMemberAsync(
        Guid memberId, UpdateProviderMemberRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FullName))
            return (null, "Full name is required.");

        if (!AppRoles.ProviderTeamRoles.Contains(req.Role))
            return (null, $"Role '{req.Role}' is not a valid provider role.");

        var user = await users.GetByIdAsync(memberId, ct);
        if (user is null || !AppRoles.ProviderTeamRoles.Contains(user.Role))
            return (null, "Member not found.");

        if (user.Role == AppRoles.Provider && req.Role != AppRoles.Provider)
            return (null, "Cannot change the role of the owner account.");

        user.UpdateProfile(req.FullName.Trim(), null);
        user.SetRole(req.Role);
        user.SetProviderRole(req.ProviderRoleId);
        user.SetPermissions(req.PermissionsOverride is { Count: > 0 } ? req.PermissionsOverride : null);

        users.Update(user);
        await users.SaveChangesAsync(ct);
        return (Map(user), null);
    }

    public async Task<bool> DeactivateMemberAsync(Guid memberId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(memberId, ct);
        if (user is null || !AppRoles.ProviderTeamRoles.Contains(user.Role))
            return false;
        user.Deactivate();
        users.Update(user);
        await users.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReactivateMemberAsync(Guid memberId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(memberId, ct);
        if (user is null || !AppRoles.ProviderTeamRoles.Contains(user.Role))
            return false;
        user.Activate();
        users.Update(user);
        await users.SaveChangesAsync(ct);
        return true;
    }

    private static ProviderTeamMemberDto Map(User u) =>
        new(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt,
            u.ProviderRoleId, u.Permissions);
}
