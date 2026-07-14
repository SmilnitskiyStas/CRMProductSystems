namespace ShelfGuard.Application.Features.Auth.Dtos;

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    AuthUserDto User
);

/// <summary>
/// Result of the first login step (TASK-330).
/// Exactly one of Response / ChallengeToken / Error is non-null:
/// - Response       → credentials ok, 2FA not enabled → tokens issued
/// - ChallengeToken → credentials ok, 2FA enabled → client must call /auth/2fa/verify
/// - Error          → authentication failed
/// </summary>
public sealed record LoginOutcome(
    LoginResponse? Response,
    string? ChallengeToken,
    string? Error
);

public record AuthUserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    Guid? TenantId,
    string? TenantName,
    Guid? StoreId,
    Dictionary<string, bool>? Permissions,
    /// <summary>Optional legal entity this user is registered under (TASK-322).</summary>
    Guid? LegalEntityId = null,
    /// <summary>Whether TOTP 2FA is enabled for this account (TASK-330).</summary>
    bool TwoFactorEnabled = false,
    /// <summary>
    /// Effective TenantRole capabilities (ADR-020, TASK-346) — empty when the user has no
    /// TenantRoleId or the referenced template is archived. Mirrors the JWT "capabilities"
    /// claim so the client's own UI logic doesn't disagree with what the server issued —
    /// UI-only signal, real enforcement is server-side (RoleOrCapabilityRequirement).
    /// </summary>
    IReadOnlyList<string>? Capabilities = null
);

// ── 2FA (TASK-330) ──────────────────────────────────────────────────────────

public sealed record TwoFactorVerifyRequest(string ChallengeToken, string Code);

public sealed record TwoFactorSetupResponse(string Secret, string OtpauthUri);

public sealed record TwoFactorEnableRequest(string Code);

public sealed record TwoFactorDisableRequest(string Password, string Code);
