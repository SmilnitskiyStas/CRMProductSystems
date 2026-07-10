using ShelfGuard.Application.Features.Auth.Dtos;

namespace ShelfGuard.Application.Features.Auth;

public interface IAuthService
{
    /// <summary>
    /// First login step. On valid credentials returns tokens, or — when the user
    /// has TOTP 2FA enabled — a short-lived challenge token instead (TASK-330).
    /// <paramref name="ipAddress"/> is used for failed-login auditing (TASK-329).
    /// </summary>
    Task<LoginOutcome> LoginAsync(string email, string password, string? ipAddress = null, CancellationToken ct = default);

    Task<(LoginResponse? Response, string? Error)> RefreshAsync(string rawRefreshToken, CancellationToken ct = default);
    Task RevokeAsync(string rawRefreshToken, CancellationToken ct = default);
    Task<AuthUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);

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
}
