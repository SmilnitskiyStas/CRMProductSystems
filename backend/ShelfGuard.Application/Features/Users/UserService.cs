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
    private readonly IUserPermissionGrantRepository _permissionGrants;

    /// <summary>ADR-019: temporary grants may not extend more than this far into the future.</summary>
    private const int MaxGrantDurationDays = 90;

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
        IRefreshTokenRepository refreshTokens,
        IUserPermissionGrantRepository permissionGrants)
    {
        _users             = users;
        _activityLogs      = activityLogs;
        _hasher            = hasher;
        _legalEntities     = legalEntities;
        _refreshTokens     = refreshTokens;
        _permissionGrants  = permissionGrants;
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

    // ── Temporary permission grants (ADR-019, TASK-342) ────────────────────────

    /// <summary>
    /// Grants a temporary, self-expiring page-access override to <paramref name="targetUserId"/>.
    /// Mirrors the <see cref="UpdatePermissionsAsync"/> role-hierarchy rule (editor rank
    /// must exceed target rank) — no self-grant, same tenant required for both users,
    /// <paramref name="permissionKey"/> validated against <see cref="ValidPages"/>,
    /// <paramref name="expiresAt"/> must be strictly in the future and no more than
    /// <see cref="MaxGrantDurationDays"/> (90) days out.
    /// </summary>
    public async Task<(PermissionGrantDto? Grant, string? Error)> GrantTemporaryPermissionAsync(
        Guid tenantId, Guid actingUserId, Guid targetUserId,
        string permissionKey, DateTime expiresAt,
        CancellationToken ct = default)
    {
        if (!ValidPages.Contains(permissionKey))
            return (null, $"Unknown page '{permissionKey}'. Valid pages: {string.Join(", ", ValidPages)}.");

        var expiresAtUtc = expiresAt.Kind == DateTimeKind.Utc ? expiresAt : expiresAt.ToUniversalTime();
        var now = DateTime.UtcNow;

        if (expiresAtUtc <= now)
            return (null, "expiresAt must be in the future.");

        if (expiresAtUtc > now.AddDays(MaxGrantDurationDays))
            return (null, $"expiresAt cannot be more than {MaxGrantDurationDays} days from now.");

        if (targetUserId == actingUserId)
            return (null, "You cannot grant yourself temporary access.");

        var actingUser = await _users.GetByIdAsync(actingUserId, ct);
        if (actingUser is null || actingUser.TenantId != tenantId)
            return (null, "Acting user not found.");

        var target = await _users.GetByIdAsync(targetUserId, ct);
        if (target is null || target.TenantId != tenantId)
            return (null, "User not found.");

        // Role hierarchy check — acting user must outrank target (same rule as UpdatePermissionsAsync).
        var actingRank = RoleRank.GetValueOrDefault(actingUser.Role, 0);
        var targetRank = RoleRank.GetValueOrDefault(target.Role, 0);

        if (actingRank <= targetRank)
            return (null, $"You do not have permission to grant access to a '{target.Role}' user.");

        var grant = UserPermissionGrant.Create(tenantId, targetUserId, permissionKey, expiresAtUtc, actingUserId);
        await _permissionGrants.AddAsync(grant, ct);

        await LogAsync(tenantId, actingUserId, "user.permission_granted",
            entityType: "User", entityId: targetUserId,
            meta: $"{{\"target\":\"{target.Email}\",\"permissionKey\":\"{permissionKey}\",\"expiresAt\":\"{expiresAtUtc:o}\"}}",
            ct: ct);

        await _permissionGrants.SaveChangesAsync(ct);

        return (ToGrantDto(grant, actingUser.FullName), null);
    }

    /// <summary>
    /// Early-revokes a temporary grant before its natural expiry.
    /// Judgment call (ADR-019 left this unspecified): allowed for (a) the user who
    /// originally created the grant (<see cref="UserPermissionGrant.GrantedByUserId"/>),
    /// revoking their own decision, OR (b) any acting user whose RoleRank exceeds the
    /// grant recipient's RoleRank — same hierarchy rule as granting. Self-revoke by the
    /// recipient is NOT allowed (a user must not be able to extend/mutate their own access).
    /// </summary>
    public async Task<string?> RevokeTemporaryPermissionAsync(
        Guid tenantId, Guid actingUserId, Guid grantId, CancellationToken ct = default)
    {
        var grant = await _permissionGrants.GetByIdAsync(tenantId, grantId, ct);
        if (grant is null)
            return "Grant not found.";

        if (grant.RevokedAt is not null)
            return "Grant is already revoked.";

        if (grant.UserId == actingUserId)
            return "You cannot revoke your own temporary access.";

        var isOriginalGranter = grant.GrantedByUserId == actingUserId;

        if (!isOriginalGranter)
        {
            var actingUser = await _users.GetByIdAsync(actingUserId, ct);
            if (actingUser is null || actingUser.TenantId != tenantId)
                return "Acting user not found.";

            var recipient = await _users.GetByIdAsync(grant.UserId, ct);
            if (recipient is null || recipient.TenantId != tenantId)
                return "User not found.";

            var actingRank    = RoleRank.GetValueOrDefault(actingUser.Role, 0);
            var recipientRank = RoleRank.GetValueOrDefault(recipient.Role, 0);

            if (actingRank <= recipientRank)
                return "You do not have permission to revoke this grant.";
        }

        var revoked = await _permissionGrants.RevokeAsync(tenantId, grantId, actingUserId, ct);
        if (!revoked)
            return "Grant not found or already revoked.";

        await LogAsync(tenantId, actingUserId, "user.permission_revoked",
            entityType: "User", entityId: grant.UserId,
            meta: $"{{\"grantId\":\"{grantId}\",\"permissionKey\":\"{grant.PermissionKey}\"}}",
            ct: ct);

        await _permissionGrants.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Active (non-revoked, non-expired) temporary grants for a user, for the UI grant list.</summary>
    public async Task<(IReadOnlyList<PermissionGrantDto>? Grants, string? Error)> GetActivePermissionGrantsAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var target = await _users.GetByIdAsync(userId, ct);
        if (target is null || target.TenantId != tenantId)
            return (null, "User not found.");

        var grants = await _permissionGrants.GetActiveGrantsForUserAsync(tenantId, userId, ct);

        // Repository queries are AsNoTracking without Include — resolve granter display
        // names with a small, deduped batch of lookups (active-grant lists are short).
        var granterNames = new Dictionary<Guid, string>();
        foreach (var granterId in grants.Select(g => g.GrantedByUserId).Distinct())
        {
            var granter = await _users.GetByIdAsync(granterId, ct);
            if (granter is not null)
                granterNames[granterId] = granter.FullName;
        }

        return (grants.Select(g => ToGrantDto(g, granterNames.GetValueOrDefault(g.GrantedByUserId))).ToList(), null);
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

    private static PermissionGrantDto ToGrantDto(UserPermissionGrant g, string? grantedByName = null) => new(
        g.Id, g.UserId, g.PermissionKey, g.ExpiresAt,
        g.GrantedByUserId, grantedByName, g.GrantedAt, g.RevokedAt
    );
}
