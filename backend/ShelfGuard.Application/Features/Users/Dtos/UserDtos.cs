namespace ShelfGuard.Application.Features.Users.Dtos;

public sealed record UserDto(
    Guid    Id,
    string  Email,
    string  FullName,
    string? Phone,
    string  Role,
    Guid?   StoreId,
    bool    IsActive,
    bool    HasTelegram,
    DateTime CreatedAt,
    DateTime? LastActiveAt,
    /// <summary>Per-user overrides. null = all defaults apply.</summary>
    Dictionary<string, bool>? Permissions = null,
    /// <summary>Display name of the user who invited this account. Null for seed/provider-created users.</summary>
    string? InvitedByName = null,
    /// <summary>Optional legal entity this user is registered under (TASK-322).</summary>
    Guid? LegalEntityId = null,
    /// <summary>Assigned custom capability-template role (ADR-020, TASK-346). Null = no template
    /// beyond whatever the base Role already grants.</summary>
    Guid? TenantRoleId = null,
    /// <summary>UI locale ("uk"/"en"); null = browser fallback (i18n Block 1, TASK-375).</summary>
    string? PreferredLocale = null
);

public sealed record InviteUserRequest(
    string  Email,
    string  FullName,
    string  Role,
    string  Password,
    Guid?   StoreId = null,
    Guid?   LegalEntityId = null
);

public sealed record UpdateUserRequest(
    string  FullName,
    string? Phone,
    string  Role,
    Guid?   StoreId,
    Guid?   LegalEntityId = null
);

public sealed record UpdateMyProfileRequest(
    string  FullName,
    string? Phone,
    /// <summary>
    /// Optional UI locale ("uk"/"en") — i18n Block 1, TASK-375. Null/omitted = leave the
    /// stored value unchanged (NOT "clear"), so older clients that don't send the field
    /// can't silently reset a chosen locale on a name/phone edit.
    /// </summary>
    string? PreferredLocale = null
);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword
);

/// <summary>
/// Assigns (or clears, when TenantRoleId is null) a custom capability-template role to a user
/// (ADR-020, TASK-346). AtLeastEnterpriseAdmin-only, no capability bypass — see
/// UsersController.AssignTenantRole.
/// </summary>
public sealed record AssignTenantRoleRequest(Guid? TenantRoleId);

/// <summary>
/// Replaces all per-user page-access overrides for a target user.
/// Send null value for a page to remove its override (revert to role default).
/// </summary>
public sealed record UpdatePermissionsRequest(
    /// <summary>
    /// Map of page slug → access override.
    /// Valid pages: dashboard, inventory, stock, receipts, transfers,
    ///              write-offs, analytics, users, settings.
    /// true = grant access; false = deny access; missing key = role default.
    /// Pass empty dict {} to clear ALL overrides.
    /// </summary>
    Dictionary<string, bool> Overrides
);

public sealed record ActivityLogDto(
    Guid      Id,
    string    Action,
    string?   EntityType,
    Guid?     EntityId,
    string?   Meta,
    string?   IpAddress,
    bool      IsImpersonated,
    DateTime  CreatedAt
);

// ── Temporary permission grants (ADR-019, TASK-342) ────────────────────────

/// <summary>
/// Grants the target user a temporary page-access override that expires on its own.
/// Same role-rank check as <see cref="UpdatePermissionsRequest"/>'s editor/target rule —
/// see <c>UserService.GrantTemporaryPermissionAsync</c>.
/// </summary>
public sealed record GrantTemporaryPermissionRequest(
    /// <summary>Page slug. Valid pages: dashboard, inventory, stock, receipts, transfers, write-offs, analytics, users, settings.</summary>
    string PermissionKey,
    /// <summary>Must be in the future and no more than 90 days out.</summary>
    DateTime ExpiresAt
);

public sealed record PermissionGrantDto(
    Guid      Id,
    Guid      UserId,
    string    PermissionKey,
    DateTime  ExpiresAt,
    Guid      GrantedByUserId,
    string?   GrantedByName,
    DateTime  GrantedAt,
    DateTime? RevokedAt
);
