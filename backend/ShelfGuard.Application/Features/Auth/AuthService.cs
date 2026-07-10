using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Features.Auth.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Auth;

public sealed class AuthService : IAuthService
{
    // TASK-329: 5 failures → 15 min lockout. Wrong 2FA codes share the same counter.
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const string GenericLoginError = "Invalid email or password.";
    private const int RecoveryCodeCount = 8;

    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwt;
    private readonly IActivityLogRepository _activityLogs;
    private readonly ITotpService _totp;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        IJwtService jwt,
        IActivityLogRepository activityLogs,
        ITotpService totp,
        ILogger<AuthService> logger)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _activityLogs = activityLogs;
        _totp = totp;
        _logger = logger;
    }

    public async Task<LoginOutcome> LoginAsync(
        string email, string password, string? ipAddress = null, CancellationToken ct = default)
    {
        var user = await _users.GetByEmailAsync(email.ToLowerInvariant(), ct);

        if (user is null)
        {
            // Unknown email — no DB write, no user enumeration; keep an audit trail in app logs.
            _logger.LogWarning("Failed login for unknown email {Email} from {Ip}",
                email, ipAddress ?? "unknown");
            return new LoginOutcome(null, null, GenericLoginError);
        }

        if (!user.IsActive)
            return new LoginOutcome(null, null, GenericLoginError);

        if (user.IsLockedOut)
        {
            // Locked: reject without revealing the lockout. Still run the hash
            // verification so response timing matches the wrong-password path.
            _passwordHasher.Verify(password, user.PasswordHash);
            return new LoginOutcome(null, null, GenericLoginError);
        }

        if (!_passwordHasher.Verify(password, user.PasswordHash))
        {
            await RegisterFailedAttemptAsync(user, "user.login_failed", ipAddress, ct);
            return new LoginOutcome(null, null, GenericLoginError);
        }

        // TASK-330: password ok but TOTP enabled → no tokens yet, hand out a
        // short-lived challenge for POST /api/auth/2fa/verify.
        if (user.TotpEnabled)
            return new LoginOutcome(null, _jwt.GenerateTwoFactorChallengeToken(user.Id), null);

        var response = await IssueTokensAsync(user, ipAddress, ct);
        return new LoginOutcome(response, null, null);
    }

    public async Task<(LoginResponse? Response, string? Error)> RefreshAsync(
        string rawRefreshToken, CancellationToken ct = default)
    {
        const string genericError = "Invalid or expired refresh token.";

        var tokenHash = _jwt.HashToken(rawRefreshToken);
        var existing = await _refreshTokens.GetByHashAsync(tokenHash, ct);

        if (existing is null)
            return (null, genericError);

        // TASK-329: a known-but-revoked token presented again = the cookie was
        // stolen and replayed after rotation. Kill the whole token family.
        if (existing.RevokedAt is not null)
        {
            await _refreshTokens.RevokeAllForUserAsync(existing.UserId, ct);
            await _refreshTokens.SaveChangesAsync(ct);

            var owner = await _users.GetByIdAsync(existing.UserId, ct);
            await _activityLogs.LogAsync(new ActivityLog
            {
                TenantId   = owner?.TenantId,
                UserId     = existing.UserId,
                Action     = "auth.refresh_reuse_detected",
                EntityType = "user",
                EntityId   = existing.UserId,
                Meta       = "Revoked refresh token replayed — all active sessions revoked.",
            }, ct);
            await _activityLogs.SaveChangesAsync(ct);

            _logger.LogWarning("Refresh token reuse detected for user {UserId} — all sessions revoked.",
                existing.UserId);
            return (null, genericError);
        }

        if (!existing.IsActive) // not revoked, so simply expired
            return (null, genericError);

        var user = await _users.GetByIdAsync(existing.UserId, ct);
        if (user is null || !user.IsActive)
            return (null, "User not found or deactivated.");

        var (newRaw, newHash) = _jwt.GenerateRefreshToken();
        var newToken = RefreshToken.Create(user.Id, newHash, DateTime.UtcNow.AddDays(7));

        existing.Revoke(newHash);
        _refreshTokens.Update(existing);
        await _refreshTokens.AddAsync(newToken, ct);
        await _refreshTokens.SaveChangesAsync(ct);

        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Email, user.Role, user.TenantId, user.StoreId, user.FullName, user.Permissions);

        return (new LoginResponse(accessToken, newRaw, ToDto(user)), null);
    }

    public async Task RevokeAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var tokenHash = _jwt.HashToken(rawRefreshToken);
        var existing = await _refreshTokens.GetActiveByHashAsync(tokenHash, ct);
        if (existing is null) return;

        existing.Revoke();
        _refreshTokens.Update(existing);
        await _refreshTokens.SaveChangesAsync(ct);
    }

    public async Task<AuthUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        return user is null ? null : ToDto(user);
    }

    // ── 2FA TOTP (TASK-330) ────────────────────────────────────────────────

    public async Task<(LoginResponse? Response, string? Error)> VerifyTwoFactorAsync(
        string challengeToken, string code, string? ipAddress = null, CancellationToken ct = default)
    {
        var userId = _jwt.ValidateTwoFactorChallengeToken(challengeToken);
        if (userId is null)
            return (null, "Invalid or expired challenge token.");

        var user = await _users.GetByIdAsync(userId.Value, ct);
        if (user is null || !user.IsActive || !user.TotpEnabled || user.TotpSecret is null)
            return (null, "Invalid code.");

        if (user.IsLockedOut)
            return (null, "Invalid code."); // don't reveal the lockout

        if (!VerifySecondFactor(user, code))
        {
            await RegisterFailedAttemptAsync(user, "user.2fa_failed", ipAddress, ct);
            return (null, "Invalid code.");
        }

        // Persist anti-replay timestep / consumed recovery code before issuing tokens.
        _users.Update(user);
        var response = await IssueTokensAsync(user, ipAddress, ct);
        return (response, null);
    }

    public async Task<(TwoFactorSetupResponse? Response, string? Error)> SetupTwoFactorAsync(
        Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive)
            return (null, "User not found.");

        if (user.TotpEnabled)
            return (null, "Two-factor authentication is already enabled.");

        var secret = _totp.GenerateSecret();
        user.SetPendingTotpSecret(secret);
        _users.Update(user);
        await _users.SaveChangesAsync(ct);

        var otpauthUri =
            $"otpauth://totp/ShelfGuard:{Uri.EscapeDataString(user.Email)}?secret={secret}&issuer=ShelfGuard";

        return (new TwoFactorSetupResponse(secret, otpauthUri), null);
    }

    public async Task<(IReadOnlyList<string>? RecoveryCodes, string? Error)> EnableTwoFactorAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive)
            return (null, "User not found.");

        if (user.TotpEnabled)
            return (null, "Two-factor authentication is already enabled.");

        if (string.IsNullOrEmpty(user.TotpSecret))
            return (null, "No pending two-factor setup. Call setup first.");

        var timestep = _totp.VerifyCode(user.TotpSecret, code);
        if (timestep is null)
            return (null, "Invalid code.");

        user.MarkTotpTimestepUsed(timestep.Value);

        var recoveryCodes = GenerateRecoveryCodes(RecoveryCodeCount);
        var hashes = recoveryCodes
            .Select(c => _jwt.HashToken(NormalizeRecoveryCode(c)))
            .ToList();
        user.EnableTotp(hashes);
        _users.Update(user);

        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId   = user.TenantId,
            UserId     = user.Id,
            Action     = "user.2fa_enabled",
            EntityType = "user",
            EntityId   = user.Id,
            Meta       = user.Email,
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);
        await _users.SaveChangesAsync(ct);

        return (recoveryCodes, null);
    }

    public async Task<string?> DisableTwoFactorAsync(
        Guid userId, string password, string code, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive)
            return "User not found.";

        if (!user.TotpEnabled || user.TotpSecret is null)
            return "Two-factor authentication is not enabled.";

        if (!_passwordHasher.Verify(password, user.PasswordHash))
            return "Invalid password.";

        if (!VerifySecondFactor(user, code))
            return "Invalid code.";

        user.DisableTotp();
        _users.Update(user);

        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId   = user.TenantId,
            UserId     = user.Id,
            Action     = "user.2fa_disabled",
            EntityType = "user",
            EntityId   = user.Id,
            Meta       = user.Email,
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);
        await _users.SaveChangesAsync(ct);

        return null;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>Checks a TOTP code (±1 timestep, anti-replay) or consumes a recovery code.</summary>
    private bool VerifySecondFactor(User user, string code)
    {
        var timestep = _totp.VerifyCode(user.TotpSecret!, code);
        if (timestep is not null)
        {
            // Anti-replay: reject any timestep at or before the last accepted one.
            if (user.TotpLastTimestep is not null && timestep.Value <= user.TotpLastTimestep.Value)
                return false;

            user.MarkTotpTimestepUsed(timestep.Value);
            return true;
        }

        // Fallback: single-use recovery code.
        var hash = _jwt.HashToken(NormalizeRecoveryCode(code));
        return user.TryConsumeRecoveryCode(hash);
    }

    /// <summary>Resets lockout, rotates in a refresh token, logs and returns the response.</summary>
    private async Task<LoginResponse> IssueTokensAsync(User user, string? ipAddress, CancellationToken ct)
    {
        user.ResetLockout();

        var (rawToken, tokenHash) = _jwt.GenerateRefreshToken();
        var refreshToken = RefreshToken.Create(user.Id, tokenHash, DateTime.UtcNow.AddDays(7));

        await _refreshTokens.AddAsync(refreshToken, ct);
        await _refreshTokens.SaveChangesAsync(ct);

        user.UpdateLastActive();
        _users.Update(user);
        await _users.SaveChangesAsync(ct);

        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId   = user.TenantId,
            UserId     = user.Id,
            Action     = "user.login",
            EntityType = "user",
            EntityId   = user.Id,
            Meta       = $"{user.Email} ({user.Role})",
            IpAddress  = ipAddress,
        }, ct);
        await _activityLogs.SaveChangesAsync(ct);

        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Email, user.Role, user.TenantId, user.StoreId, user.FullName, user.Permissions);

        return new LoginResponse(accessToken, rawToken, ToDto(user));
    }

    /// <summary>Increments the shared failure counter, persists, and audits the attempt.</summary>
    private async Task RegisterFailedAttemptAsync(
        User user, string action, string? ipAddress, CancellationToken ct)
    {
        var lockedOut = user.RegisterFailedLogin(MaxFailedAttempts, LockoutDuration);
        _users.Update(user);
        await _users.SaveChangesAsync(ct);

        await _activityLogs.LogAsync(new ActivityLog
        {
            TenantId   = user.TenantId,
            UserId     = user.Id,
            Action     = action,
            EntityType = "user",
            EntityId   = user.Id,
            Meta       = user.Email,
            IpAddress  = ipAddress,
        }, ct);

        if (lockedOut)
        {
            await _activityLogs.LogAsync(new ActivityLog
            {
                TenantId   = user.TenantId,
                UserId     = user.Id,
                Action     = "user.locked_out",
                EntityType = "user",
                EntityId   = user.Id,
                Meta       = $"{user.Email} — locked for {LockoutDuration.TotalMinutes:0} min after {MaxFailedAttempts} failures",
                IpAddress  = ipAddress,
            }, ct);
            _logger.LogWarning("Account {Email} locked out after {Attempts} failed attempts (IP {Ip}).",
                user.Email, MaxFailedAttempts, ipAddress ?? "unknown");
        }

        await _activityLogs.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<string> GenerateRecoveryCodes(int count)
    {
        // No 0/O/1/I — avoids transcription mistakes.
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var codes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var chars = new char[8];
            for (var j = 0; j < chars.Length; j++)
                chars[j] = alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];
            codes.Add($"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}");
        }
        return codes;
    }

    /// <summary>Uppercases and strips separators so "ab2c-3def" == "AB2C3DEF".</summary>
    private static string NormalizeRecoveryCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static AuthUserDto ToDto(User u) =>
        new(u.Id, u.Email, u.FullName, u.Role, u.TenantId, u.Tenant?.Name, u.StoreId, u.Permissions, u.LegalEntityId, u.TotpEnabled);
}
