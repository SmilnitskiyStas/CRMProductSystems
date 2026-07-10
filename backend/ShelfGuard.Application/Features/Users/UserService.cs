using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.Users.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Users;

public sealed class UserService : IUserService
{
    private readonly IUserRepository         _users;
    private readonly IActivityLogRepository  _activityLogs;
    private readonly IPasswordHasher         _hasher;
    private readonly ILegalEntityService     _legalEntities;
    private readonly IRefreshTokenRepository _refreshTokens;

    private static readonly HashSet<string> ValidRoles =
    [
        "enterprise_admin", "network_manager", "store_manager",
        "merchandiser", "storekeeper", "cashier",
        "supplier_admin",
    ];

    /// <summary>
    /// Role rank — higher number = higher authority.
    /// Used to ensure only higher-ranked users can edit lower-ranked ones.
    /// </summary>
    private static readonly Dictionary<string, int> RoleRank = new()
    {
        ["enterprise_admin"] = 4,
        ["network_manager"]  = 3,
        ["store_manager"]    = 2,
        ["storekeeper"]      = 1,
        ["merchandiser"]     = 1,
        ["cashier"]          = 1,
    };

    private static readonly HashSet<string> ValidPages =
    [
        "dashboard", "inventory", "stock", "receipts",
        "transfers", "write-offs", "analytics", "users", "settings",
    ];

    public UserService(
        IUserRepository users,
        IActivityLogRepository activityLogs,
        IPasswordHasher hasher,
        ILegalEntityService legalEntities,
        IRefreshTokenRepository refreshTokens)
    {
        _users         = users;
        _activityLogs  = activityLogs;
        _hasher        = hasher;
        _legalEntities = legalEntities;
        _refreshTokens = refreshTokens;
    }

    // ── List ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        var users = await _users.GetAllByTenantAsync(tenantId, ct);
        return users.Select(ToDto).ToList();
    }

    // ── Get by id ─────────────────────────────────────────────────────────────

    public async Task<(UserDto? User, string? Error)> GetByIdAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.TenantId != tenantId)
            return (null, "User not found.");
        return (ToDto(user), null);
    }

    // ── Invite (create) ───────────────────────────────────────────────────────

    public async Task<(UserDto? User, string? Error)> InviteAsync(
        Guid tenantId, InviteUserRequest request, string inviterName, CancellationToken ct = default)
    {
        if (!ValidRoles.Contains(request.Role))
            return (null, $"Invalid role '{request.Role}'.");

        var passwordError = PasswordValidator.Validate(request.Password, request.Email);
        if (passwordError is not null)
            return (null, passwordError);

        var existing = await _users.GetByEmailAsync(request.Email.ToLowerInvariant(), ct);
        if (existing is not null)
            return (null, $"Email '{request.Email}' is already registered.");

        if (request.LegalEntityId.HasValue &&
            !await _legalEntities.BelongsToTenantAsync(tenantId, request.LegalEntityId.Value, ct))
            return (null, "Вказана юридична особа не належить цьому тенанту.");

        var hash = _hasher.Hash(request.Password);
        var user = User.Create(tenantId, request.Email, request.FullName, hash, request.Role,
            request.StoreId, invitedByName: string.IsNullOrWhiteSpace(inviterName) ? null : inviterName);

        if (request.LegalEntityId.HasValue)
            user.SetLegalEntity(request.LegalEntityId);

        await _users.AddAsync(user, ct);

        await LogAsync(tenantId, user.Id, "user.invited",
            entityType: "User", entityId: user.Id,
            meta: $"{{\"email\":\"{request.Email}\",\"role\":\"{request.Role}\"}}",
            ct: ct);

        await _users.SaveChangesAsync(ct);
        return (ToDto(user), null);
    }

    // ── Update (by manager) ───────────────────────────────────────────────────

    public async Task<(UserDto? User, string? Error)> UpdateAsync(
        Guid tenantId, Guid userId, UpdateUserRequest request, CancellationToken ct = default)
    {
        if (!ValidRoles.Contains(request.Role))
            return (null, $"Invalid role '{request.Role}'.");

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.TenantId != tenantId)
            return (null, "User not found.");

        if (request.LegalEntityId.HasValue &&
            !await _legalEntities.BelongsToTenantAsync(tenantId, request.LegalEntityId.Value, ct))
            return (null, "Вказана юридична особа не належить цьому тенанту.");

        user.UpdateProfile(request.FullName, request.Phone);
        user.SetRole(request.Role);
        user.SetStore(request.StoreId);
        user.SetLegalEntity(request.LegalEntityId);

        _users.Update(user);

        await LogAsync(tenantId, userId, "user.updated",
            entityType: "User", entityId: userId,
            meta: $"{{\"role\":\"{request.Role}\"}}",
            ct: ct);

        await _users.SaveChangesAsync(ct);
        return (ToDto(user), null);
    }

    // ── Deactivate (soft delete) ──────────────────────────────────────────────

    public async Task<string?> DeactivateAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.TenantId != tenantId)
            return "User not found.";

        user.Deactivate();
        _users.Update(user);

        await LogAsync(tenantId, userId, "user.deactivated",
            entityType: "User", entityId: userId,
            ct: ct);

        await _users.SaveChangesAsync(ct);
        return null;
    }

    // ── Self-service ──────────────────────────────────────────────────────────

    public async Task<(UserDto? User, string? Error)> UpdateMyProfileAsync(
        Guid userId, UpdateMyProfileRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
            return (null, "Full name is required.");

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return (null, "User not found.");

        user.UpdateProfile(request.FullName.Trim(), request.Phone?.Trim());
        _users.Update(user);

        if (user.TenantId.HasValue)
            await LogAsync(user.TenantId.Value, userId, "user.profile_updated",
                entityType: "User", entityId: userId, ct: ct);

        await _users.SaveChangesAsync(ct);
        return (ToDto(user), null);
    }

    public async Task<string?> ChangePasswordAsync(
        Guid userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return "User not found.";

        var passwordError = PasswordValidator.Validate(request.NewPassword, user.Email);
        if (passwordError is not null)
            return passwordError;

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            return "Current password is incorrect.";

        user.ChangePassword(_hasher.Hash(request.NewPassword));
        _users.Update(user);

        // TASK-329: a stolen session must not survive a password change.
        await _refreshTokens.RevokeAllForUserAsync(userId, ct);
        await _refreshTokens.SaveChangesAsync(ct);

        if (user.TenantId.HasValue)
            await LogAsync(user.TenantId.Value, userId, "user.password_changed",
                entityType: "User", entityId: userId, ct: ct);

        await _users.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> LinkTelegramAsync(Guid userId, string chatId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chatId))
            return "Chat ID is required.";

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return "User not found.";

        user.LinkTelegram(chatId.Trim());
        _users.Update(user);

        if (user.TenantId.HasValue)
            await LogAsync(user.TenantId.Value, userId, "user.telegram_linked",
                entityType: "User", entityId: userId, ct: ct);

        await _users.SaveChangesAsync(ct);
        return null;
    }

    // ── Activity log ──────────────────────────────────────────────────────────

    public async Task<(IReadOnlyList<ActivityLogDto> Logs, string? Error)> GetActivityAsync(
        Guid tenantId, Guid userId, int limit = 50, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.TenantId != tenantId)
            return ([], "User not found.");

        var logs = await _activityLogs.GetByUserAsync(tenantId, userId, limit, ct);
        return (logs.Select(ToActivityDto).ToList(), null);
    }

    // ── Permissions ───────────────────────────────────────────────────────────

    public async Task<(UserDto? User, string? Error)> UpdatePermissionsAsync(
        Guid tenantId, Guid editorUserId, string editorRole,
        Guid targetUserId, UpdatePermissionsRequest request,
        CancellationToken ct = default)
    {
        // Validate page slugs
        var invalidPages = request.Overrides.Keys
            .Where(k => !ValidPages.Contains(k))
            .ToList();

        if (invalidPages.Count > 0)
            return (null, $"Unknown page(s): {string.Join(", ", invalidPages)}. " +
                          $"Valid pages: {string.Join(", ", ValidPages)}.");

        // Load target user
        var target = await _users.GetByIdAsync(targetUserId, ct);
        if (target is null || target.TenantId != tenantId)
            return (null, "User not found.");

        // Prevent editing own permissions
        if (targetUserId == editorUserId)
            return (null, "You cannot modify your own permissions.");

        // Role hierarchy check — editor must outrank target
        var editorRank = RoleRank.GetValueOrDefault(editorRole, 0);
        var targetRank = RoleRank.GetValueOrDefault(target.Role, 0);

        if (editorRank <= targetRank)
            return (null, $"You do not have permission to edit a '{target.Role}' user's access.");

        // Apply overrides (empty dict = clear all)
        var newPerms = request.Overrides.Count == 0
            ? null
            : new Dictionary<string, bool>(request.Overrides);

        target.SetPermissions(newPerms);
        _users.Update(target);

        await LogAsync(tenantId, editorUserId, "user.permissions_updated",
            entityType: "User", entityId: targetUserId,
            meta: $"{{\"target\":\"{target.Email}\",\"overrides\":{System.Text.Json.JsonSerializer.Serialize(newPerms)}}}",
            ct: ct);

        await _users.SaveChangesAsync(ct);
        return (ToDto(target), null);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task LogAsync(
        Guid tenantId, Guid userId, string action,
        string? entityType = null, Guid? entityId = null,
        string? meta = null, CancellationToken ct = default)
    {
        var entry = new ActivityLog
        {
            Id         = Guid.NewGuid(),
            TenantId   = tenantId,
            UserId     = userId,
            Action     = action,
            EntityType = entityType,
            EntityId   = entityId,
            Meta       = meta,
            CreatedAt  = DateTime.UtcNow,
        };
        await _activityLogs.LogAsync(entry, ct);
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static UserDto ToDto(User u) => new(
        u.Id, u.Email, u.FullName, u.Phone, u.Role,
        u.StoreId, u.IsActive,
        HasTelegram: u.TelegramChatId is not null,
        u.CreatedAt, u.LastActiveAt,
        Permissions: u.Permissions,
        InvitedByName: u.InvitedByName,
        LegalEntityId: u.LegalEntityId
    );

    private static ActivityLogDto ToActivityDto(ActivityLog a) => new(
        a.Id, a.Action, a.EntityType, a.EntityId,
        a.Meta, a.IpAddress, a.IsImpersonated, a.CreatedAt
    );
}
