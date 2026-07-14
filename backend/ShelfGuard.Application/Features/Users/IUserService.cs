using ShelfGuard.Application.Features.Users.Dtos;

namespace ShelfGuard.Application.Features.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<(UserDto? User, string? Error)> GetByIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Invites (creates) a new user. Server-side role-rank check (TASK-347): the acting user
    /// cannot invite a role ranked higher than their own — closes the ADR-020 "users.manage"
    /// capability escalation path (a staff-rank capability holder could otherwise invite a
    /// brand-new enterprise_admin with no rank check at all).
    /// </summary>
    Task<(UserDto? User, string? Error)> InviteAsync(Guid tenantId, Guid actingUserId, InviteUserRequest request, string inviterName, CancellationToken ct = default);

    /// <summary>
    /// Updates a user's profile, role, and store assignment. Server-side role-rank checks
    /// (TASK-347): the acting user must have a strictly higher rank than the target's CURRENT
    /// role, and (when the role is actually changing) cannot assign a role ranked higher than
    /// their own. Self-update (<paramref name="actingUserId"/> == <paramref name="userId"/>) is
    /// allowed for profile fields but never for a role change, even a demotion — see
    /// UserService.UpdateAsync for the exact rule. Exception: two "supplier_admin" peers
    /// (ADR-016 flat cabinet domain, no rank hierarchy) skip the outrank check — see
    /// UserService.IsExemptFromOutrankGate.
    /// </summary>
    Task<(UserDto? User, string? Error)> UpdateAsync(Guid tenantId, Guid actingUserId, Guid userId, UpdateUserRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deactivates (soft-deletes) a user. Server-side role-rank check (TASK-347): the acting
    /// user must have a strictly higher rank than the target's role — this also rules out
    /// self-deactivation (equal rank never satisfies "strictly higher"). Exception: two
    /// "supplier_admin" peers skip the outrank check — see UserService.IsExemptFromOutrankGate
    /// (without it, SupplierCabinetService's staff deactivation would always fail, since every
    /// supplier-tenant user shares that one flat role).
    /// </summary>
    Task<string?> DeactivateAsync(Guid tenantId, Guid actingUserId, Guid userId, CancellationToken ct = default);

    // Self-service (any authenticated user)
    Task<(UserDto? User, string? Error)> UpdateMyProfileAsync(Guid userId, UpdateMyProfileRequest request, CancellationToken ct = default);
    Task<string?> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
    Task<string?> LinkTelegramAsync(Guid userId, string chatId, CancellationToken ct = default);

    // Activity log
    Task<(IReadOnlyList<ActivityLogDto> Logs, string? Error)> GetActivityAsync(
        Guid tenantId, Guid userId, int limit = 50, CancellationToken ct = default);

    // Permissions (page-access overrides)
    /// <summary>
    /// Updates per-user page-access overrides.
    /// Only users with a higher role rank can edit permissions of lower-ranked users.
    /// </summary>
    Task<(UserDto? User, string? Error)> UpdatePermissionsAsync(
        Guid tenantId, Guid editorUserId, string editorRole,
        Guid targetUserId, UpdatePermissionsRequest request,
        CancellationToken ct = default);

    // Temporary permission grants (ADR-019, TASK-342)
    /// <summary>
    /// Grants the target user a temporary page-access override that expires on its own.
    /// Server-side role-rank check: actingUser's RoleRank must exceed the target's.
    /// </summary>
    Task<(PermissionGrantDto? Grant, string? Error)> GrantTemporaryPermissionAsync(
        Guid tenantId, Guid actingUserId, Guid targetUserId,
        string permissionKey, DateTime expiresAt,
        CancellationToken ct = default);

    /// <summary>
    /// Early-revokes a temporary grant. Allowed for the original granter (own grant) or
    /// any user whose RoleRank exceeds the grant recipient's RoleRank.
    /// </summary>
    Task<string?> RevokeTemporaryPermissionAsync(
        Guid tenantId, Guid actingUserId, Guid grantId, CancellationToken ct = default);

    /// <summary>Active (non-revoked, non-expired) temporary grants for a user.</summary>
    Task<(IReadOnlyList<PermissionGrantDto>? Grants, string? Error)> GetActivePermissionGrantsAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default);

    // Tenant-role (capability template) assignment (ADR-020, TASK-346)
    /// <summary>
    /// Assigns or clears (tenantRoleId = null) the target user's TenantRole template.
    /// When tenantRoleId is not null it must belong to the SAME tenant as the target user and
    /// be active — otherwise "not found"/"archived" is returned (never a 403, so a caller
    /// cannot use this to probe for another tenant's template ids).
    /// </summary>
    Task<(bool Success, string? Error)> AssignTenantRoleAsync(
        Guid tenantId, Guid targetUserId, Guid? tenantRoleId, CancellationToken ct = default);
}
