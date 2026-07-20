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
    // NOTE (2026-07-15): LinkTelegramAsync (raw client-supplied chat_id, no ownership proof)
    // removed — see AuthController.cs for the security writeup. Telegram linking now goes
    // exclusively through ITelegramLinkService.CreateLinkCodeAsync + the worker's /start <code>
    // listener.

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

    // Store-scoped location assignment (TASK-392b, Feature 2 Stage 1 — schema/plumbing only,
    // enforcement RLS is Stage 3). Originally a SEPARATE method/endpoint from
    // InviteAsync/UpdateAsync because those two only ever wrote exactly one user_locations row
    // (mirroring User.StoreId) for ranks store_manager-and-below, with network_manager's
    // multi-location assignment as the only caller of this full-replace path. TASK-397 removed
    // that split at the UI layer: every LocationScopedRoles member (network_manager AND
    // store_manager-and-below) now gets the same multi-select dropdown backed by THIS method
    // directly. InviteAsync/UpdateAsync still exist and still auto-sync a single legacy row for
    // store_manager-and-below, but only while a target is still in the "0 or 1 row" shape —
    // once SetLocationsAsync has given someone 2+ rows, that auto-sync steps aside and this
    // method alone owns their assignment (see UserService.SyncSingleLocationAsync's guard).
    /// <summary>
    /// Full-replace of a user's store-scoped location assignments: the user's user_locations
    /// rows become exactly <paramref name="locationIds"/> (existing rows outside that set are
    /// deleted, missing ones inserted). Every id must belong to the same tenant as the target
    /// user (validated via ILocationService.BelongsToTenantAsync, same anti-probing posture as
    /// AssignTenantRoleAsync's TenantRole check — a foreign-tenant id is indistinguishable from
    /// a bad request, never a 403/enumeration hint). Gated AtLeastEnterpriseAdmin-only with no
    /// capability bypass at the controller — this decides which locations' real business data
    /// a whole role will see once Stage 3 lands. Callable for ANY target role — this method
    /// itself has never had a role restriction; TASK-397 confirmed the "network_manager-only"
    /// framing was purely a frontend UI decision.
    /// </summary>
    Task<(bool Success, string? Error)> SetLocationsAsync(
        Guid tenantId, Guid targetUserId, List<Guid> locationIds, Guid actingUserId, CancellationToken ct = default);

    /// <summary>Current store-scoped location assignment list for a user.</summary>
    Task<(List<Guid>? LocationIds, string? Error)> GetLocationsAsync(
        Guid tenantId, Guid targetUserId, CancellationToken ct = default);
}
