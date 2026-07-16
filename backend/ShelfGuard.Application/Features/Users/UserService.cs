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
    private readonly ITenantRoleRepository   _tenantRoles;

    /// <summary>ADR-019: temporary grants may not extend more than this far into the future.</summary>
    private const int MaxGrantDurationDays = 90;

    private static readonly HashSet<string> ValidRoles =
    [
        "enterprise_admin", "network_manager", "store_manager",
        "merchandiser", "storekeeper", "cashier", "staff",
        "supplier_admin",
    ];

    /// <summary>
    /// Role rank — higher number = higher authority.
    /// Used to ensure only higher-ranked users can edit lower-ranked ones.
    /// "staff" (ADR-020, TASK-345) is intentionally the lowest rank — a capability-only
    /// user (TenantRoleId) never outranks anyone via base Role alone.
    /// </summary>
    private static readonly Dictionary<string, int> RoleRank = new()
    {
        ["enterprise_admin"] = 4,
        ["network_manager"]  = 3,
        ["store_manager"]    = 2,
        ["storekeeper"]      = 1,
        ["merchandiser"]     = 1,
        ["cashier"]          = 1,
        ["staff"]            = 0,
    };

    /// <summary>
    /// True when the RoleRank outrank gate below (TASK-347) must be skipped for this
    /// actor/other-role pair. Supplier cabinet (ADR-016) is a flat, single-role domain —
    /// every supplier tenant user, from the first onboarded admin to every teammate they
    /// invite, is role="supplier_admin" (see AppRoles.cs, TenantAdminService,
    /// ProviderService). RoleRank has no entry for it, so <c>GetValueOrDefault(role, 0)</c>
    /// silently gives it rank 0 — identical to "staff" — and two supplier_admin peers always
    /// compare as equal rank. Applying "strictly higher rank required" there would
    /// permanently block SupplierCabinetService's Invite/Deactivate-staff flow (100% of
    /// calls, since caller and target always share this role) even though nothing is being
    /// escalated: supplier_admin cannot reach UsersController's Invite/Update/Deactivate at
    /// all (absent from every AppPolicies role array, including the "users.manage"
    /// capability-OR policies — AppPolicies.cs), so exempting it here does not reopen the
    /// ADR-020 escalation path this gate exists to close.
    /// </summary>
    private static bool IsExemptFromOutrankGate(string actingRole, string otherRole) =>
        actingRole == "supplier_admin" && otherRole == "supplier_admin";

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
        IUserPermissionGrantRepository permissionGrants,
        ITenantRoleRepository tenantRoles)
    {
        _users             = users;
        _activityLogs      = activityLogs;
        _hasher            = hasher;
        _legalEntities     = legalEntities;
        _refreshTokens     = refreshTokens;
        _permissionGrants  = permissionGrants;
        _tenantRoles       = tenantRoles;
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
        Guid tenantId, Guid actingUserId, InviteUserRequest request, string inviterName, CancellationToken ct = default)
    {
        if (!ValidRoles.Contains(request.Role))
            return (null, $"Invalid role '{request.Role}'.");

        var passwordError = PasswordValidator.Validate(request.Password, request.Email);
        if (passwordError is not null)
            return (null, passwordError);

        var actingUser = await _users.GetByIdAsync(actingUserId, ct);
        if (actingUser is null || actingUser.TenantId != tenantId)
            return (null, "Acting user not found.");

        var actingRank    = RoleRank.GetValueOrDefault(actingUser.Role, 0);
        var requestedRank = RoleRank.GetValueOrDefault(request.Role, 0);

        // Cannot invite a user with a role ranked higher than your own (TASK-347) — closes the
        // ADR-020 "users.manage" capability escalation path: a staff-rank capability holder
        // could otherwise invite a brand-new enterprise_admin with zero rank check.
        // (supplier_admin peers already pass unaided here — both default to rank 0 via
        // GetValueOrDefault, so 0 > 0 is false; no IsExemptFromOutrankGate call needed. That
        // helper only guards the "<=" gates in UpdateAsync/DeactivateAsync below, where equal
        // rank is otherwise rejected.)
        if (requestedRank > actingRank)
            return (null, $"You do not have permission to invite a role higher than your own ('{actingUser.Role}').");

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
        Guid tenantId, Guid actingUserId, Guid userId, UpdateUserRequest request, CancellationToken ct = default)
    {
        if (!ValidRoles.Contains(request.Role))
            return (null, $"Invalid role '{request.Role}'.");

        var target = await _users.GetByIdAsync(userId, ct);
        if (target is null || target.TenantId != tenantId)
            return (null, "User not found.");

        if (request.LegalEntityId.HasValue &&
            !await _legalEntities.BelongsToTenantAsync(tenantId, request.LegalEntityId.Value, ct))
            return (null, "Вказана юридична особа не належить цьому тенанту.");

        var roleChanging = !string.Equals(request.Role, target.Role, StringComparison.Ordinal);

        if (actingUserId == userId)
        {
            // Self-update (TASK-347): RoleRank[actor] can never be *strictly* greater than
            // RoleRank[self], so the outrank gate below is meaningless for this case — a role
            // change is simply never allowed through this endpoint when acting on yourself,
            // even a demotion (simplest/safest of the two options considered, see task log).
            // Non-role fields (name, phone, store, legal entity) still go through.
            if (roleChanging)
                return (null, "You do not have permission to change your own role.");
        }
        else
        {
            var actingUser = await _users.GetByIdAsync(actingUserId, ct);
            if (actingUser is null || actingUser.TenantId != tenantId)
                return (null, "Acting user not found.");

            var actingRank = RoleRank.GetValueOrDefault(actingUser.Role, 0);
            var targetRank = RoleRank.GetValueOrDefault(target.Role, 0);

            // Outrank gate — same rule as UpdatePermissionsAsync: acting user must have a
            // STRICTLY higher rank than the target's CURRENT role. Exempt supplier_admin
            // peers — see IsExemptFromOutrankGate.
            if (!IsExemptFromOutrankGate(actingUser.Role, target.Role) && actingRank <= targetRank)
                return (null, $"You do not have permission to edit a '{target.Role}' user.");

            if (roleChanging)
            {
                var requestedRank = RoleRank.GetValueOrDefault(request.Role, 0);

                // Cannot assign a role ranked higher than the actor's own (TASK-347) — closes
                // the ADR-020 "users.manage" capability escalation path (self-escalation via
                // this same check, and also a pre-existing gap: store_manager could already
                // promote anyone to enterprise_admin here before this fix).
                if (requestedRank > actingRank)
                    return (null, $"You do not have permission to assign a role higher than your own ('{actingUser.Role}').");
            }
        }

        target.UpdateProfile(request.FullName, request.Phone);
        target.SetRole(request.Role);
        target.SetStore(request.StoreId);
        target.SetLegalEntity(request.LegalEntityId);

        _users.Update(target);

        await LogAsync(tenantId, userId, "user.updated",
            entityType: "User", entityId: userId,
            meta: $"{{\"role\":\"{request.Role}\"}}",
            ct: ct);

        await _users.SaveChangesAsync(ct);
        return (ToDto(target), null);
    }

    // ── Deactivate (soft delete) ──────────────────────────────────────────────

    public async Task<string?> DeactivateAsync(Guid tenantId, Guid actingUserId, Guid userId, CancellationToken ct = default)
    {
        // Explicit, friendlier message for the self case — otherwise covered by the generic
        // rank check below anyway (equal rank never satisfies "strictly higher").
        if (actingUserId == userId)
            return "You do not have permission to deactivate your own account.";

        var target = await _users.GetByIdAsync(userId, ct);
        if (target is null || target.TenantId != tenantId)
            return "User not found.";

        var actingUser = await _users.GetByIdAsync(actingUserId, ct);
        if (actingUser is null || actingUser.TenantId != tenantId)
            return "Acting user not found.";

        var actingRank = RoleRank.GetValueOrDefault(actingUser.Role, 0);
        var targetRank = RoleRank.GetValueOrDefault(target.Role, 0);

        // Outrank gate (TASK-347) — same rule as UpdatePermissionsAsync/UpdateAsync: cannot
        // deactivate someone whose rank is >= your own. Exempt supplier_admin peers — see
        // IsExemptFromOutrankGate.
        if (!IsExemptFromOutrankGate(actingUser.Role, target.Role) && actingRank <= targetRank)
            return $"You do not have permission to deactivate a '{target.Role}' user.";

        target.Deactivate();
        _users.Update(target);

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

    // LinkTelegramAsync removed (2026-07-15 security fix) — see AuthController.cs. The only
    // remaining path that ever calls User.LinkTelegram(...) now is the worker's telegram-listener
    // (raw SQL UPDATE after validating a one-time code), which doesn't go through this service.

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

        var expiresAtUtc = expiresAt.Kind == DateTimeKind.Utc
            ? expiresAt
            : DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc);
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

    // ── Tenant-role (capability template) assignment (ADR-020, TASK-346) ───────

    public async Task<(bool Success, string? Error)> AssignTenantRoleAsync(
        Guid tenantId, Guid targetUserId, Guid? tenantRoleId, CancellationToken ct = default)
    {
        var target = await _users.GetByIdAsync(targetUserId, ct);
        if (target is null || target.TenantId != tenantId)
            return (false, "User not found.");

        if (tenantRoleId.HasValue)
        {
            // Tenant-scoped lookup: a template belonging to a DIFFERENT tenant is
            // indistinguishable from a non-existent one here — 404, never 403, so this
            // cannot be used to confirm another tenant's template ids exist.
            var role = await _tenantRoles.GetByIdAsync(tenantId, tenantRoleId.Value, ct);
            if (role is null)
                return (false, "TenantRole not found.");

            if (!role.IsActive)
                return (false, "Cannot assign an archived TenantRole.");
        }

        target.SetTenantRole(tenantRoleId);
        _users.Update(target);

        await LogAsync(tenantId, targetUserId, "user.tenant_role_assigned",
            entityType: "User", entityId: targetUserId,
            meta: $"{{\"tenantRoleId\":{(tenantRoleId.HasValue ? $"\"{tenantRoleId}\"" : "null")}}}",
            ct: ct);

        await _users.SaveChangesAsync(ct);
        return (true, null);
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
        LegalEntityId: u.LegalEntityId,
        TenantRoleId: u.TenantRoleId
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
