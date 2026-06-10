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
}
