using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.LegalEntities;
using ShelfGuard.Application.Features.Locations;
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
    private readonly ILocationService        _locations;
    private readonly IUserLocationRepository _userLocations;

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

    /// <summary>
    /// Ranks that get exactly one user_locations row (mirroring User.StoreId) auto-synced by
    /// plain Invite/Update (TASK-392b, project-architect's store-scope design). Deliberately
    /// excludes: enterprise_admin (unconditional bypass, never gets rows at all — "не пиши
    /// нічого"); network_manager (multi-location even before TASK-397, always went through
    /// SetLocationsAsync's dedicated full-replace endpoint instead — "ОКРЕМИЙ метод/ендпоінт");
    /// supplier_admin (ADR-016 flat cabinet domain, entirely outside the tenant-staff
    /// store-scope model — same reasoning as IsExemptFromOutrankGate/RoleRank omitting it).
    ///
    /// TASK-397: the frontend no longer gives these ranks a single-store picker at all — every
    /// <see cref="LocationScopedRoles"/> member (this set plus network_manager) now gets the
    /// SAME multi-select UI backed by SetLocationsAsync directly. This set/its auto-sync in
    /// <see cref="SyncSingleLocationAsync"/> only still exists for the legacy "0 or 1 row"
    /// shape (old clients, or a target never touched via the new multi-select) — see that
    /// method's guard against collapsing an already-multi-location assignment.
    /// </summary>
    private static readonly HashSet<string> SingleLocationRoles =
    [
        "store_manager", "merchandiser", "storekeeper", "cashier", "staff",
    ];

    /// <summary>
    /// Roles UserDto.NeedsLocationAssignment (TASK-395) gates on — the same six roles the
    /// store-scope-rollout-checklist.md coverage-gap SQL report keys off. A superset of
    /// <see cref="SingleLocationRoles"/>: it additionally includes network_manager, whose
    /// assignment goes through <see cref="SetLocationsAsync"/>'s full-replace endpoint rather
    /// than the single-row Invite/Update sync, but which still needs at least one
    /// user_locations row before Stage 3 RLS enforcement can safely go live for that user.
    /// Never enterprise_admin/provider/provider_admin/worker/supplier_admin — unconditional
    /// bypass regardless of what (if anything) is in user_locations for them.
    /// </summary>
    private static readonly HashSet<string> LocationScopedRoles =
    [
        "network_manager", "store_manager", "merchandiser", "storekeeper", "cashier", "staff",
    ];

    private static readonly HashSet<string> ValidPages =
    [
        "dashboard", "inventory", "stock", "receipts",
        "transfers", "write-offs", "analytics", "users", "settings",
    ];

    /// <summary>UI locales the platform ships message catalogs for (i18n Block 1, TASK-375).</summary>
    private static readonly HashSet<string> SupportedLocales = ["uk", "en"];

    public UserService(
        IUserRepository users,
        IActivityLogRepository activityLogs,
        IPasswordHasher hasher,
        ILegalEntityService legalEntities,
        IRefreshTokenRepository refreshTokens,
        IUserPermissionGrantRepository permissionGrants,
        ITenantRoleRepository tenantRoles,
        ILocationService locations,
        IUserLocationRepository userLocations)
    {
        _users             = users;
        _activityLogs      = activityLogs;
        _hasher            = hasher;
        _legalEntities     = legalEntities;
        _refreshTokens     = refreshTokens;
        _permissionGrants  = permissionGrants;
        _tenantRoles       = tenantRoles;
        _locations         = locations;
        _userLocations     = userLocations;
    }

    // ── List ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        var users = await _users.GetAllByTenantAsync(tenantId, ct);

        // TASK-395: one batched existence query for the whole list (scoped further to just the
        // users whose role could possibly need it) instead of one query per user — this is the
        // list endpoint, called far more often and with far more rows than the single-user
        // paths below, so N+1 here would actually matter.
        var candidateIds = users
            .Where(u => LocationScopedRoles.Contains(u.Role))
            .Select(u => u.Id)
            .ToList();

        var withLocation = candidateIds.Count == 0
            ? new HashSet<Guid>()
            : (await _userLocations.GetUserIdsWithAnyLocationAsync(tenantId, candidateIds, ct)).ToHashSet();

        return users
            .Select(u => ToDto(u, LocationScopedRoles.Contains(u.Role) && !withLocation.Contains(u.Id)))
            .ToList();
    }

    // ── Get by id ─────────────────────────────────────────────────────────────

    public async Task<(UserDto? User, string? Error)> GetByIdAsync(
        Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || user.TenantId != tenantId)
            return (null, "User not found.");

        var needsLocation = await NeedsLocationAssignmentAsync(user.TenantId, user.Id, user.Role, ct);
        return (ToDto(user, needsLocation), null);
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

        // TASK-392b: closes the pre-existing gap where StoreId accepted any GUID with no
        // tenant-ownership check at all — same pattern as LegalEntityId immediately above.
        if (request.StoreId.HasValue &&
            !await _locations.BelongsToTenantAsync(tenantId, request.StoreId.Value, ct))
            return (null, "Вказана локація не належить цьому тенанту.");

        var hash = _hasher.Hash(request.Password);
        var user = User.Create(tenantId, request.Email, request.FullName, hash, request.Role,
            request.StoreId, invitedByName: string.IsNullOrWhiteSpace(inviterName) ? null : inviterName);

        if (request.LegalEntityId.HasValue)
            user.SetLegalEntity(request.LegalEntityId);

        await _users.AddAsync(user, ct);

        // TASK-392b: store_manager-and-below get exactly one user_locations row mirroring
        // User.StoreId (network_manager/enterprise_admin/supplier_admin are untouched here —
        // see SingleLocationRoles). Safe to reference user.Id already — User.Create assigns
        // it synchronously, no DB round-trip needed before this write.
        await SyncSingleLocationAsync(tenantId, user.Id, request.Role, request.StoreId, actingUserId, ct);

        await LogAsync(tenantId, user.Id, "user.invited",
            entityType: "User", entityId: user.Id,
            meta: $"{{\"email\":\"{request.Email}\",\"role\":\"{request.Role}\"}}",
            ct: ct);

        await _users.SaveChangesAsync(ct);

        // Query fresh, post-SaveChanges (TASK-395): SyncSingleLocationAsync just wrote the
        // user_locations row(s) above but hasn't been committed until the SaveChangesAsync
        // right before this line, so this must run after it, not before.
        var needsLocation = await NeedsLocationAssignmentAsync(tenantId, user.Id, user.Role, ct);
        return (ToDto(user, needsLocation), null);
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

        // TASK-392b: same pre-existing gap fix as InviteAsync above.
        if (request.StoreId.HasValue &&
            !await _locations.BelongsToTenantAsync(tenantId, request.StoreId.Value, ct))
            return (null, "Вказана локація не належить цьому тенанту.");

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

        var normalizedPhone = string.IsNullOrWhiteSpace(request.Phone)
            ? null
            : PhoneNormalizer.Normalize(request.Phone);
        if (!string.IsNullOrWhiteSpace(request.Phone) && normalizedPhone is null)
            return (null, "Invalid phone number. Expected a Ukrainian mobile number.");
        if (normalizedPhone is not null)
        {
            var phoneOwner = await _users.GetByPhoneAsync(normalizedPhone, ct);
            if (phoneOwner is not null && phoneOwner.Id != target.Id)
                return (null, "This phone number is already linked to another user.");
        }

        target.UpdateProfile(request.FullName, normalizedPhone);
        target.SetRole(request.Role);
        target.SetStore(request.StoreId);
        target.SetLegalEntity(request.LegalEntityId);

        _users.Update(target);

        // TASK-392b: keep the single user_locations row in sync with request.Role/StoreId —
        // covers a plain store transfer, a role change into/out of SingleLocationRoles, and a
        // store being cleared. Same transaction as the User row update (shared AppDbContext,
        // one _users.SaveChangesAsync below).
        await SyncSingleLocationAsync(tenantId, userId, request.Role, request.StoreId, actingUserId, ct);

        await LogAsync(tenantId, userId, "user.updated",
            entityType: "User", entityId: userId,
            meta: $"{{\"role\":\"{request.Role}\"}}",
            ct: ct);

        await _users.SaveChangesAsync(ct);

        // Post-SaveChanges, same reasoning as InviteAsync above (TASK-395).
        var needsLocation = await NeedsLocationAssignmentAsync(tenantId, target.Id, target.Role, ct);
        return (ToDto(target, needsLocation), null);
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

        // i18n Block 1 (TASK-375): only "uk"/"en" are supported; null = leave unchanged.
        if (request.PreferredLocale is not null && !SupportedLocales.Contains(request.PreferredLocale))
            return (null, $"Unsupported locale '{request.PreferredLocale}'. Supported: {string.Join(", ", SupportedLocales)}.");

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return (null, "User not found.");

        var normalizedPhone = string.IsNullOrWhiteSpace(request.Phone)
            ? null
            : PhoneNormalizer.Normalize(request.Phone);
        if (!string.IsNullOrWhiteSpace(request.Phone) && normalizedPhone is null)
            return (null, "Invalid phone number. Expected a Ukrainian mobile number.");
        if (normalizedPhone is not null)
        {
            var phoneOwner = await _users.GetByPhoneAsync(normalizedPhone, ct);
            if (phoneOwner is not null && phoneOwner.Id != user.Id)
                return (null, "This phone number is already linked to another user.");
        }

        user.UpdateProfile(request.FullName.Trim(), normalizedPhone);
        if (request.PreferredLocale is not null)
            user.SetPreferredLocale(request.PreferredLocale);
        _users.Update(user);

        if (user.TenantId.HasValue)
            await LogAsync(user.TenantId.Value, userId, "user.profile_updated",
                entityType: "User", entityId: userId, ct: ct);

        await _users.SaveChangesAsync(ct);

        var needsLocation = await NeedsLocationAssignmentAsync(user.TenantId, user.Id, user.Role, ct);
        return (ToDto(user, needsLocation), null);
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
        // TASK-465: this is the one place a user "takes control" of a temporary password
        // issued by the forgot-password flow — clears the marker so it stops being treated as
        // temporary/expiring. No-op (ClearTempPasswordExpiry sets null → null) when the
        // account's password wasn't temporary to begin with.
        user.ClearTempPasswordExpiry();
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

        var needsLocation = await NeedsLocationAssignmentAsync(tenantId, target.Id, target.Role, ct);
        return (ToDto(target, needsLocation), null);
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

    // ── Store-scoped location assignment (TASK-392b, Feature 2 Stage 1) ────────

    public async Task<(bool Success, string? Error)> SetLocationsAsync(
        Guid tenantId, Guid targetUserId, List<Guid> locationIds, Guid actingUserId, CancellationToken ct = default)
    {
        var target = await _users.GetByIdAsync(targetUserId, ct);
        if (target is null || target.TenantId != tenantId)
            return (false, "User not found.");

        var distinctIds = locationIds.Distinct().ToList();

        // Same anti-probing posture as AssignTenantRoleAsync's TenantRole check: a
        // foreign-tenant location id just fails validation here, never a 403/enumeration hint.
        foreach (var locationId in distinctIds)
        {
            if (!await _locations.BelongsToTenantAsync(tenantId, locationId, ct))
                return (false, "Вказана локація не належить цьому тенанту.");
        }

        await _userLocations.ReplaceForUserAsync(tenantId, targetUserId, distinctIds, actingUserId, ct);

        await LogAsync(tenantId, actingUserId, "user.locations_updated",
            entityType: "User", entityId: targetUserId,
            meta: $"{{\"locationIds\":{System.Text.Json.JsonSerializer.Serialize(distinctIds)}}}",
            ct: ct);

        await _userLocations.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(List<Guid>? LocationIds, string? Error)> GetLocationsAsync(
        Guid tenantId, Guid targetUserId, CancellationToken ct = default)
    {
        var target = await _users.GetByIdAsync(targetUserId, ct);
        if (target is null || target.TenantId != tenantId)
            return (null, "User not found.");

        var ids = await _userLocations.GetLocationIdsForUserAsync(tenantId, targetUserId, ct);
        return (ids.ToList(), null);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Keeps the single legacy user_locations row for store_manager-and-below in sync with
    /// (role, storeId) — called from InviteAsync/UpdateAsync only. No-op for any role
    /// outside <see cref="SingleLocationRoles"/> (network_manager/enterprise_admin/
    /// supplier_admin/anything else): existing rows for those ranks, if any, are left
    /// untouched here — network_manager's list is only ever managed via
    /// <see cref="SetLocationsAsync"/>, and enterprise_admin is never supposed to have rows
    /// at all (its bypass is unconditional regardless of what this leaves behind).
    ///
    /// TASK-397 guard: a SingleLocationRoles member can now ALSO be given 2+ locations
    /// directly via SetLocationsAsync (frontend's UserLocationsEditor is no longer
    /// network_manager-only — every LocationScopedRoles member gets the same multi-select
    /// dropdown). Once that has happened, this method must not collapse the assignment back
    /// down to one row (or wipe it) just because an unrelated profile field (name/phone/role/
    /// legal entity) was saved through the plain Invite/Update endpoint — previously this ran
    /// unconditionally on every such save, which would silently destroy a multi-location
    /// assignment on the very next unrelated edit. Only auto-sync while the target is still in
    /// the legacy "0 or 1 row" shape; once it has 2+, SetLocationsAsync alone owns it from then
    /// on (the extra existence read is one cheap query on an already low-frequency admin path,
    /// same cost class as the pre-existing NeedsLocationAssignmentAsync check on this route).
    /// </summary>
    private async Task SyncSingleLocationAsync(
        Guid tenantId, Guid userId, string role, Guid? storeId, Guid actingUserId, CancellationToken ct)
    {
        if (!SingleLocationRoles.Contains(role))
            return;

        var existingIds = await _userLocations.GetLocationIdsForUserAsync(tenantId, userId, ct);
        if ((existingIds?.Count ?? 0) > 1)
            return;

        var locationIds = storeId.HasValue ? new[] { storeId.Value } : Array.Empty<Guid>();
        await _userLocations.ReplaceForUserAsync(tenantId, userId, locationIds, actingUserId, ct);
    }

    /// <summary>
    /// Computes UserDto.NeedsLocationAssignment (TASK-395) for one user. Short-circuits to
    /// false without a query whenever the role alone already settles the answer — everything
    /// outside <see cref="LocationScopedRoles"/>, or a null tenantId (no LocationScopedRoles
    /// member should ever have one in practice, but this guards the self-service
    /// UpdateMyProfileAsync call site regardless, since it has no separate tenantId parameter
    /// to fall back on). Callers that just wrote a user_locations row in the same request
    /// (Invite/Update via SyncSingleLocationAsync) MUST call this only after their own
    /// SaveChangesAsync — this method queries the database fresh, so it cannot see
    /// still-pending, not-yet-committed changes on the shared AppDbContext.
    /// </summary>
    private async Task<bool> NeedsLocationAssignmentAsync(
        Guid? tenantId, Guid userId, string role, CancellationToken ct)
    {
        if (tenantId is null || !LocationScopedRoles.Contains(role))
            return false;

        return !await _userLocations.HasAnyLocationAsync(tenantId.Value, userId, ct);
    }

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

    // needsLocationAssignment has no default on purpose (TASK-395): every call site must decide
    // it explicitly via NeedsLocationAssignmentAsync (or GetAllAsync's batched equivalent)
    // rather than silently getting "false" from a forgotten call site.
    private static UserDto ToDto(User u, bool needsLocationAssignment) => new(
        u.Id, u.Email, u.FullName, u.Phone, u.Role,
        u.StoreId, u.IsActive,
        HasTelegram: u.TelegramChatId is not null,
        u.CreatedAt, u.LastActiveAt,
        Permissions: u.Permissions,
        InvitedByName: u.InvitedByName,
        LegalEntityId: u.LegalEntityId,
        TenantRoleId: u.TenantRoleId,
        PreferredLocale: u.PreferredLocale,
        NeedsLocationAssignment: needsLocationAssignment
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
