using ShelfGuard.Application.Features.Users.Dtos;

namespace ShelfGuard.Application.Features.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default);
    Task<(UserDto? User, string? Error)> GetByIdAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
    Task<(UserDto? User, string? Error)> InviteAsync(Guid tenantId, InviteUserRequest request, string inviterName, CancellationToken ct = default);
    Task<(UserDto? User, string? Error)> UpdateAsync(Guid tenantId, Guid userId, UpdateUserRequest request, CancellationToken ct = default);
    Task<string?> DeactivateAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

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
}
