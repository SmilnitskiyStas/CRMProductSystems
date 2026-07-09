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
    Guid? LegalEntityId = null
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
    string? Phone
);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword
);

public sealed record LinkTelegramRequest(string ChatId);

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
