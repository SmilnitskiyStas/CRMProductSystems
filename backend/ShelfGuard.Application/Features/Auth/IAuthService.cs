using ShelfGuard.Application.Features.Auth.Dtos;

namespace ShelfGuard.Application.Features.Auth;

public interface IAuthService
{
    /// <summary>
    /// First login step. On valid credentials returns tokens, or — when the user
    /// has TOTP 2FA enabled — a short-lived challenge token instead (TASK-330).
    /// <paramref name="ipAddress"/> is used for failed-login auditing (TASK-329).
    /// TASK-465: when the password hash matches but it was a temporary password
    /// (forgot-password flow) that has since expired, returns a specific
    /// "temporary password expired" error instead of issuing a challenge/tokens.
    /// </summary>
    Task<LoginOutcome> LoginAsync(string email, string password, string? ipAddress = null, CancellationToken ct = default);

    Task<(LoginResponse? Response, string? Error)> RefreshAsync(string rawRefreshToken, CancellationToken ct = default);
    Task RevokeAsync(string rawRefreshToken, CancellationToken ct = default);
    Task<AuthUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Issues a staff session after a separate trusted identity flow has authenticated an
    /// explicitly linked account. Callers must establish the contact link before invoking it.
    /// </summary>
    Task<LoginOutcome> IssueLinkedMobileSessionAsync(
        Guid userId, string? ipAddress = null, CancellationToken ct = default);

    // ── 2FA TOTP (TASK-330) ────────────────────────────────────────────────

    /// <summary>
    /// Second login step: validates the challenge token plus a TOTP code
    /// (±1 timestep, anti-replay) OR an unused recovery code, then issues tokens.
    /// Wrong codes count toward the account lockout counter.
    /// </summary>
    Task<(LoginResponse? Response, string? Error)> VerifyTwoFactorAsync(
        string challengeToken, string code, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>Generates a new pending TOTP secret (2FA not enabled until verified).</summary>
    Task<(TwoFactorSetupResponse? Response, string? Error)> SetupTwoFactorAsync(
        Guid userId, CancellationToken ct = default);

    /// <summary>Verifies a code against the pending secret and activates 2FA.
    /// Returns the plaintext recovery codes — shown to the user exactly once.</summary>
    Task<(IReadOnlyList<string>? RecoveryCodes, string? Error)> EnableTwoFactorAsync(
        Guid userId, string code, CancellationToken ct = default);

    /// <summary>Disables 2FA after verifying both the password and a valid code.</summary>
    Task<string?> DisableTwoFactorAsync(
        Guid userId, string password, string code, CancellationToken ct = default);

    // ── Forgot password / temporary password (TASK-465) ─────────────────────

    /// <summary>
    /// First (and only) step of the forgot-password flow (TASK-465 — supersedes the TASK-456
    /// link/token design, see <see cref="ShelfGuard.Domain.Entities.User"/>'s TASK-464/465 doc
    /// comments). Never throws / never signals whether the email exists — the controller
    /// responds identically regardless of outcome, same no-enumeration posture as
    /// <see cref="LoginAsync"/>. Unknown or inactive email → a warning log only, no DB write.
    /// Otherwise → generates a cryptographically random temporary password, makes it the
    /// account's real password immediately (valid 3 hours — see
    /// <see cref="ShelfGuard.Domain.Entities.User.HasActiveTempPassword"/>), and enqueues a
    /// targeted outbox notification (email + Telegram) carrying it. There is no separate
    /// "enter new password" step — the user logs in directly with the temporary password.
    /// Setting a permanent password goes through the existing authenticated change-password
    /// endpoint, which also clears the temporary-password marker.
    /// </summary>
    Task ForgotPasswordAsync(string email, string? ipAddress = null, CancellationToken ct = default);
}
