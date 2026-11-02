using System.ComponentModel.DataAnnotations;

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
    string? Error,
    string? EnrollmentToken = null
);

public sealed record MfaEnrollmentRequest([Required, StringLength(4096)] string EnrollmentToken);
public sealed record MfaEnrollmentEnableRequest([Required, StringLength(4096)] string EnrollmentToken, [Required, StringLength(32)] string Code);
public sealed record MfaStepUpRequest([Required, StringLength(32)] string Code);
public sealed record MfaStepUpResponse(string AccessToken, AuthUserDto User);

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
    IReadOnlyList<string>? Capabilities = null,
    /// <summary>
    /// Set once the user has completed the one-time-code Telegram link flow
    /// (TelegramLinkService.CreateLinkCodeAsync + the worker's /start &lt;code&gt; listener writes
    /// this column directly). Was missing from this DTO entirely until the 2026-07-15 security
    /// fix (TASK-368) — frontend "Telegram: Підключено" status previously only reflected a
    /// client-side optimistic cache patch from the now-removed unverified link endpoint, never
    /// real server state, and silently reverted to "not linked" on any cache invalidation/reload.
    /// </summary>
    string? TelegramChatId = null,
    /// <summary>
    /// UI locale the user picked ("uk"/"en"); null = client falls back to the browser
    /// language (i18n rollout Block 1, TASK-375).
    /// </summary>
    string? PreferredLocale = null,
    /// <summary>
    /// Effective TenantRole sidebar-tab visibility (TASK-391b) — empty when the user has no
    /// TenantRoleId or the referenced template is archived. Mirrors the JWT "tabs" claim, same
    /// "server is the real gate, client mirrors for its own UI logic" relationship as
    /// <see cref="Capabilities"/> — but a separate axis (page/route visibility, not backend
    /// actions). See ShelfGuard.Domain.Constants.TenantRoleTabs for the full rationale.
    /// </summary>
    IReadOnlyList<string>? Tabs = null,
    /// <summary>
    /// True when the account's current password is a temporary one issued by the
    /// legacy forgot-password flow (TASK-465) and still within its validity window. Mirrors
    /// <see cref="ShelfGuard.Domain.Entities.User.HasActiveTempPassword"/>, recomputed fresh
    /// at every mint site (login, 2FA verify, refresh) and on GetCurrentUserAsync — the client
    /// uses it to show a reminder banner prompting the user to set a permanent password via
    /// the existing change-password endpoint, which clears this on its own.
    /// </summary>
    bool PasswordIsTemporary = false,
    /// <summary>
    /// When <see cref="PasswordIsTemporary"/> is true, the UTC instant the temporary password
    /// stops working. Null otherwise.
    /// </summary>
    DateTime? TemporaryPasswordExpiresAt = null
);

// ── 2FA (TASK-330) ──────────────────────────────────────────────────────────

public sealed record TwoFactorVerifyRequest(string ChallengeToken, string Code);

public sealed record TwoFactorSetupResponse(string Secret, string OtpauthUri);

public sealed record TwoFactorEnableRequest(string Code);

public sealed record TwoFactorDisableRequest(string Password, string Code);

// ── Proof-based password recovery ─────────────────────────────────────────

public sealed record ForgotPasswordRequest([Required, EmailAddress, StringLength(254)] string Email);

public sealed record ResetPasswordRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(43, MinimumLength = 43)] string Token,
    [Required, StringLength(256, MinimumLength = 12)] string NewPassword);
