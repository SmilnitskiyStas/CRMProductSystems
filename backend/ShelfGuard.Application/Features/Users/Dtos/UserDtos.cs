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
    string? PreferredLocale = null,
    /// <summary>
    /// TASK-395: true when this user's role is one of the six store-scoped roles the (not yet
    /// deployed) Stage 3 RLS enforcement will gate — network_manager, store_manager,
    /// merchandiser, storekeeper, cashier, staff — AND the user currently has zero rows in
    /// user_locations. Lets the product owner spot the store-scope-rollout-checklist.md
    /// coverage gap from the Users list UI instead of running the raw SQL report. Always false
    /// for enterprise_admin/provider/provider_admin/worker/supplier_admin, and false for any
    /// store-scoped-role user who already has at least one location assigned.
    /// </summary>
    bool NeedsLocationAssignment = false
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
/// Full-replace of a user's store-scoped location assignments (TASK-392b, Feature 2 Stage 1).
/// AtLeastEnterpriseAdmin-only, no capability bypass — see UsersController.UpdateLocations.
/// Empty list clears all assignments for the user.
/// </summary>
public sealed record UpdateUserLocationsRequest(List<Guid> LocationIds);

/// <summary>Current store-scoped location assignment list for a user (TASK-392b).</summary>
public sealed record UserLocationsDto(List<Guid> LocationIds);

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
