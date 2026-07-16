using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Provider.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Provider;

public sealed class ProviderTeamService(
    IUserRepository users,
    IPasswordHasher hasher,
    IRefreshTokenRepository refreshTokens) : IProviderTeamService
{
    private static readonly string[] TeamRoles =
        [AppRoles.Provider, AppRoles.ProviderAdmin, AppRoles.ProviderAgent];

    public async Task<IReadOnlyList<ProviderTeamMemberDto>> GetTeamAsync(CancellationToken ct)
    {
        var members = await users.GetByRolesAsync(TeamRoles, ct);
        return members.Select(Map).ToList();
    }

    public async Task<(ProviderTeamMemberDto? Member, string? Error)> InviteMemberAsync(
        InviteProviderMemberRequest req, string actingRole, CancellationToken ct)
    {
        if (!AppRoles.ProviderTeamRoles.Contains(req.Role))
            return (null, $"Role '{req.Role}' is not a valid provider role.");

        // TASK-363 (Block 12 audit): only an existing owner (role == provider) may create
        // another owner-level account. Without this, any provider_admin — who already passes
        // the ProviderCanInvite policy — could mint a brand-new "provider" user and thereby
        // reach ProviderController's ProviderOnly-gated endpoints (tenant CRUD, impersonation,
        // platform logs) that are deliberately restricted to the literal owner role.
        if (req.Role == AppRoles.Provider && actingRole != AppRoles.Provider)
            return (null, "Only the owner account can create another owner-level account.");

        var existing = await users.GetByEmailAsync(req.Email.ToLowerInvariant(), ct);
        if (existing is not null && existing.IsActive)
            return (null, $"Email '{req.Email}' is already registered.");

        // TASK-329: user-supplied passwords must meet the shared policy;
        // auto-generated fallbacks are crypto-random and policy-compliant.
        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            var passwordError = PasswordValidator.Validate(req.Password, req.Email);
            if (passwordError is not null)
                return (null, passwordError);
        }

        var password = !string.IsNullOrWhiteSpace(req.Password)
            ? req.Password
            : GenerateRandomPassword();
        var hash = hasher.Hash(password);

        // Reactivate deactivated account instead of creating a duplicate
        if (existing is not null && !existing.IsActive)
        {
            existing.UpdateProfile(req.FullName.Trim(), null);
            existing.SetRole(req.Role);
            existing.SetProviderRole(req.ProviderRoleId);
            existing.SetPermissions(req.PermissionsOverride is { Count: > 0 } ? req.PermissionsOverride : null);
            existing.ChangePassword(hash);
            existing.Activate();

            // TASK-329: password was reset — old sessions must not survive.
            await refreshTokens.RevokeAllForUserAsync(existing.Id, ct);
            await refreshTokens.SaveChangesAsync(ct);

            users.Update(existing);
            await users.SaveChangesAsync(ct);
            return (Map(existing), null);
        }

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
        Guid memberId, UpdateProviderMemberRequest req, string actingRole, CancellationToken ct)
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

        // TASK-363 (Block 12 audit): symmetric to the guard above — only an existing owner may
        // *promote* someone else to the owner role, not just be blocked from demoting one.
        // Previously a provider_admin could PUT role="provider" on any teammate (or themselves)
        // and instantly unlock ProviderController's owner-only endpoints.
        if (req.Role == AppRoles.Provider && user.Role != AppRoles.Provider && actingRole != AppRoles.Provider)
            return (null, "Only the owner account can promote a member to the owner role.");

        user.UpdateProfile(req.FullName.Trim(), null);
        user.SetRole(req.Role);
        user.SetProviderRole(req.ProviderRoleId);
        user.SetPermissions(req.PermissionsOverride is { Count: > 0 } ? req.PermissionsOverride : null);

        users.Update(user);
        await users.SaveChangesAsync(ct);
        return (Map(user), null);
    }

    public async Task<(bool Success, string? Error)> DeactivateMemberAsync(Guid memberId, string actingRole, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(memberId, ct);
        if (user is null || !AppRoles.ProviderTeamRoles.Contains(user.Role))
            return (false, "Member not found.");

        // TASK-363 (Block 12 audit): a provider_admin must not be able to deactivate the owner
        // account — that would lock the real owner out of ProviderOnly-gated endpoints.
        if (user.Role == AppRoles.Provider && actingRole != AppRoles.Provider)
            return (false, "Only the owner account can deactivate the owner account.");

        user.Deactivate();
        users.Update(user);
        await users.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ReactivateMemberAsync(Guid memberId, string actingRole, CancellationToken ct)
    {
        // No owner-only guard here: reactivating restores the *target's* own access, it does
        // not grant the actor anything — unlike Deactivate (denial of service against the
        // owner) or the role-escalation paths above. actingRole is accepted for interface
        // symmetry / future use, not currently restrictive.
        _ = actingRole;

        var user = await users.GetByIdAsync(memberId, ct);
        if (user is null || !AppRoles.ProviderTeamRoles.Contains(user.Role))
            return (false, "Member not found.");
        user.Activate();
        users.Update(user);
        await users.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Crypto-random 16-char password guaranteed to satisfy PasswordValidator.</summary>
    private static string GenerateRandomPassword()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        while (true)
        {
            var chars = new char[16];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];
            var candidate = new string(chars);
            if (PasswordValidator.Validate(candidate) is null)
                return candidate;
        }
    }

    private static ProviderTeamMemberDto Map(User u) =>
        new(u.Id, u.Email, u.FullName, u.Role, u.IsActive, u.CreatedAt,
            u.ProviderRoleId, u.Permissions);
}
