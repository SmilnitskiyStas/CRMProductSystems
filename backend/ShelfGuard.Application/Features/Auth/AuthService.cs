using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Common;
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
    private static readonly TimeSpan RefreshRotationGrace = TimeSpan.FromSeconds(10);
    public const string RotationRetry = "Refresh already rotated; retry with the current proof.";

    private const string TempPasswordExpiredError = "Temporary password has expired. Please request a new one.";

    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwt;
    private readonly IActivityLogRepository _activityLogs;
    private readonly ITotpService _totp;
    private readonly IUserPermissionGrantRepository _permissionGrants;
    private readonly ITenantRoleRepository _tenantRoles;
    private readonly ILogger<AuthService> _logger;
    private readonly IMfaPolicy _mfaPolicy;
    private readonly ITenantSessionOverride _tenantScope;

    public AuthService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        IJwtService jwt,
        IActivityLogRepository activityLogs,
        ITotpService totp,
        IUserPermissionGrantRepository permissionGrants,
        ITenantRoleRepository tenantRoles,
        ILogger<AuthService> logger, IMfaPolicy mfaPolicy, ITenantSessionOverride tenantScope)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _activityLogs = activityLogs;
        _totp = totp;
        _permissionGrants = permissionGrants;
        _tenantRoles = tenantRoles;
        _logger = logger;
        _mfaPolicy = mfaPolicy;
        _tenantScope = tenantScope;
    }

    private async Task<User?> EnrollmentUserAsync(string token, CancellationToken ct)
    {
        var proof = _jwt.ValidateMfaEnrollmentToken(token);
        if (proof == null) return null;
        var user = await _users.GetByIdAsync(proof.UserId, ct);
        return user is { IsActive: true, TotpEnabled: false } && !user.IsLockedOut && user.AuthVersion == proof.AuthVersion ? user : null;
    }

    public async Task<(TwoFactorSetupResponse? Response, string? Error)> SetupEnrollmentAsync(string proof, CancellationToken ct = default)
    {
        var user = await EnrollmentUserAsync(proof, ct);
        return user == null ? (null, "Invalid or expired enrollment proof.") : await SetupTwoFactorAsync(user.Id, ct);
    }

    public async Task<(IReadOnlyList<string>? RecoveryCodes, string? Error, LoginResponse? Session)> EnableEnrollmentAsync(string proof, string code, CancellationToken ct = default)
    {
        var user = await EnrollmentUserAsync(proof, ct);
        return user == null ? (null, "Invalid or expired enrollment proof.", null) : await EnableTwoFactorAsync(user.Id, code, ct);
    }

    public async Task<(MfaStepUpResponse? Response, string? Error)> StepUpAsync(Guid userId, Guid sessionId, string code, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        var session = await _refreshTokens.GetSessionAsync(userId, sessionId, ct);
        if (user is not { IsActive: true, TotpEnabled: true } || user.TotpSecret == null || user.IsLockedOut
            || session == null || session.AuthVersion != user.AuthVersion) return (null, "MFA verification unavailable. Sign in again.");
        if (!VerifySecondFactor(user, code))
        {
            await RegisterFailedAttemptAsync(user, "user.mfa_step_up_failed", null, ct);
            return (null, "Invalid code.");
        }
        user.ResetLockout();
        var now = DateTime.UtcNow;
        if (!await _refreshTokens.ConfirmMfaAsync(user, sessionId, now, ct)) return (null, "Session changed. Sign in again.");
        var (permissions, capabilities, tabs) = await BuildEffectiveAccessAsync(user, ct);
        return (new MfaStepUpResponse(_jwt.GenerateAccessToken(user.Id, user.Email, user.Role, user.TenantId, user.StoreId, user.FullName,
            permissions, capabilities, tabs, sessionId, user.AuthVersion, now), ToDto(user, permissions, capabilities, tabs)), null);
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

        // TASK-465: the hash matched — but if it matched a temporary password (forgot-password
        // flow) that has since expired, reject with a specific error instead of proceeding.
        // Only reachable on a hash MATCH (never on a mismatch above), so this can't be used to
        // distinguish "wrong password" from "right password, expired" by timing/branch alone —
        // whoever typed the correct temp password already proved possession of it, so a
        // specific message here discloses nothing an attacker doesn't already know. Ordered
        // before the TOTP branch: an expired credential shouldn't earn a 2FA challenge.
        if (user.TempPasswordExpiresAt.HasValue && !user.HasActiveTempPassword)
            return new LoginOutcome(null, null, TempPasswordExpiredError);

        // TASK-330: password ok but TOTP enabled → no tokens yet, hand out a
        // short-lived challenge for POST /api/auth/2fa/verify.
        if (user.TotpEnabled)
            return new LoginOutcome(null, _jwt.GenerateTwoFactorChallengeToken(user.Id, user.AuthVersion), null);
        if (await _mfaPolicy.IsRequiredAsync(user, ct))
            return new LoginOutcome(null, null, null, _jwt.GenerateMfaEnrollmentToken(user.Id, user.AuthVersion));

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
        // Grace window: a token rotated moments ago and presented again is almost always a benign
        // race (two tabs / parallel requests refreshing at once), not theft. Reject this call but
        // keep the family alive; the client retries with the already-rotated cookie.
        if (existing.RevokedAt is not null
            && existing.ReplacedByTokenHash is not null
            && DateTime.UtcNow - existing.RevokedAt.Value < RefreshRotationGrace)
            return (null, RotationRetry);

        if (existing.RevokedAt is not null)
        {
            await _refreshTokens.RevokeAllSessionsAsync(existing.UserId, ct);

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
            await SaveInTenantAsync(owner?.TenantId, () => _activityLogs.SaveChangesAsync(ct), ct);

            _logger.LogWarning("Refresh token reuse detected for user {UserId} — all sessions revoked.",
                existing.UserId);
            return (null, genericError);
        }

        if (!existing.IsActive) // not revoked, so simply expired
            return (null, genericError);

        var user = await _users.GetByIdAsync(existing.UserId, ct);
        if (user is null || !user.IsActive || user.AuthVersion != existing.AuthVersion)
            return (null, "User not found or deactivated.");
        if ((user.TotpEnabled || await _mfaPolicy.IsRequiredAsync(user, ct)) && (!user.TotpEnabled || existing.MfaVerifiedAt == null))
            return (null, "MFA enrollment or verification required. Sign in again.");

        var (newRaw, newHash) = _jwt.GenerateRefreshToken();
        var newToken = RefreshToken.Create(user.Id, newHash, existing.ExpiresAt, existing.SessionId, user.AuthVersion, existing.MfaVerifiedAt);
        if (!await _refreshTokens.TryRotateAsync(existing, newToken, ct)) return (null, RotationRetry);

        var (effectivePermissions, effectiveCapabilities, effectiveTabs) = await BuildEffectiveAccessAsync(user, ct);
        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Email, user.Role, user.TenantId, user.StoreId, user.FullName,
            effectivePermissions, effectiveCapabilities, effectiveTabs, existing.SessionId, user.AuthVersion, existing.MfaVerifiedAt);

        return (new LoginResponse(accessToken, newRaw, ToDto(user, effectivePermissions, effectiveCapabilities, effectiveTabs)), null);
    }

    public async Task RevokeAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var tokenHash = _jwt.HashToken(rawRefreshToken);
        var existing = await _refreshTokens.GetByHashAsync(tokenHash, ct);
        if (existing is null) return;

        await _refreshTokens.RevokeSessionAsync(existing.UserId, existing.SessionId, ct);
    }

    public Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct = default) => _refreshTokens.RevokeSessionAsync(userId, sessionId, ct);
    public Task RevokeAllSessionsAsync(Guid userId, CancellationToken ct = default) => _refreshTokens.RevokeAllSessionsAsync(userId, ct);

    public async Task<AuthUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return null;

        var (effectivePermissions, effectiveCapabilities, effectiveTabs) = await BuildEffectiveAccessAsync(user, ct);
        return ToDto(user, effectivePermissions, effectiveCapabilities, effectiveTabs);
    }

    // ── 2FA TOTP (TASK-330) ────────────────────────────────────────────────

    public async Task<(LoginResponse? Response, string? Error)> VerifyTwoFactorAsync(
        string challengeToken, string code, string? ipAddress = null, CancellationToken ct = default)
    {
        var proof = _jwt.ValidateTwoFactorChallengeToken(challengeToken);
        if (proof is null)
            return (null, "Invalid or expired challenge token.");

        var user = await _users.GetByIdAsync(proof.UserId, ct);
        if (user is null || !user.IsActive || user.AuthVersion != proof.AuthVersion || !user.TotpEnabled || user.TotpSecret is null)
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
        var response = await IssueTokensAsync(user, ipAddress, ct, DateTime.UtcNow);
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

    public async Task<(IReadOnlyList<string>? RecoveryCodes, string? Error, LoginResponse? Session)> EnableTwoFactorAsync(
        Guid userId, string code, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive)
            return (null, "User not found.", null);

        if (user.TotpEnabled)
            return (null, "Two-factor authentication is already enabled.", null);

        if (string.IsNullOrEmpty(user.TotpSecret))
            return (null, "No pending two-factor setup. Call setup first.", null);

        var timestep = _totp.VerifyCode(user.TotpSecret, code);
        if (user.IsLockedOut) return (null, "Account is temporarily locked.", null);
        if (timestep is null) {
            await RegisterFailedAttemptAsync(user, "user.2fa_enrollment_failed", null, ct);
            return (null, "Invalid code.", null);
        }

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
        // The verified enrollment upgrades only this client; all old-version sessions fail
        // the live validator. Commit MFA state, codes and the new refresh session together.
        return (recoveryCodes, null, await IssueTokensAsync(user, null, ct, DateTime.UtcNow));
    }

    public async Task<string?> DisableTwoFactorAsync(
        Guid userId, string password, string code, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null || !user.IsActive)
            return "User not found.";

        if (!user.TotpEnabled || user.TotpSecret is null)
            return "Two-factor authentication is not enabled.";
        if (await _mfaPolicy.IsRequiredAsync(user, ct)) return "MFA is required for your current role or permissions.";

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

    // The tenant comes exclusively from the persisted user resolved by credential/session
    // verification. Anonymous login/enrollment/refresh requests have no tenant claim yet.
    // Keep overrides transaction-local; do not wrap repositories that own a rotation transaction.
    private Task<T> InTenantAsync<T>(Guid? tenantId, Func<Task<T>> action, CancellationToken ct) =>
        tenantId is Guid id ? _tenantScope.ExecuteAsync(id, action, ct) : action();

    private Task SaveInTenantAsync(Guid? tenantId, Func<Task> action, CancellationToken ct) =>
        InTenantAsync(tenantId, async () => { await action(); return true; }, ct);

    private Task<(Dictionary<string, bool>, List<string>, List<string>)> BuildEffectiveAccessAsync(User user, CancellationToken ct) =>
        InTenantAsync(user.TenantId, () => BuildEffectiveAccessCoreAsync(user, ct), ct);

    private async Task<(Dictionary<string, bool>, List<string>, List<string>)> BuildEffectiveAccessCoreAsync(User user, CancellationToken ct) =>
        (await BuildEffectivePermissionsAsync(user, ct), await BuildEffectiveCapabilitiesAsync(user, ct), await BuildEffectiveTabsAsync(user, ct));

    /// <summary>Resets lockout, rotates in a refresh token, logs and returns the response.</summary>
    private Task<LoginResponse> IssueTokensAsync(User user, string? ipAddress, CancellationToken ct, DateTime? mfaVerifiedAt = null) =>
        InTenantAsync(user.TenantId, () => IssueTokensCoreAsync(user, ipAddress, ct, mfaVerifiedAt), ct);

    private async Task<LoginResponse> IssueTokensCoreAsync(User user, string? ipAddress, CancellationToken ct, DateTime? mfaVerifiedAt)
    {
        user.ResetLockout();
        user.UpdateLastActive();
        _users.Update(user);

        var (rawToken, tokenHash) = _jwt.GenerateRefreshToken();
        var refreshToken = RefreshToken.Create(user.Id, tokenHash, DateTime.UtcNow.AddDays(7), authVersion: user.AuthVersion, mfaVerifiedAt: mfaVerifiedAt);
        await _refreshTokens.AddAsync(refreshToken, ct);

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

        // TASK-370 (Block 17 load-test finding): the three writes above (refresh
        // token insert, user lockout-reset/last-active update, login activity log)
        // used to be three separate SaveChangesAsync calls — each its own DB round
        // trip + WAL-fsync commit, even though _users/_refreshTokens/_activityLogs
        // all share the same scoped AppDbContext. Under k6 login-storm concurrency
        // this measured p95=2.28s/p99=2.56s for a single login; batching into one
        // commit dropped it to p95=<see task log>. Any one repo's SaveChangesAsync
        // flushes all three — using _users' here is arbitrary, not load-bearing.
        await _users.SaveChangesAsync(ct);

        var (effectivePermissions, effectiveCapabilities, effectiveTabs) = await BuildEffectiveAccessCoreAsync(user, ct);
        var accessToken = _jwt.GenerateAccessToken(user.Id, user.Email, user.Role, user.TenantId, user.StoreId, user.FullName,
            effectivePermissions, effectiveCapabilities, effectiveTabs, refreshToken.SessionId, user.AuthVersion, mfaVerifiedAt);

        return new LoginResponse(accessToken, rawToken, ToDto(user, effectivePermissions, effectiveCapabilities, effectiveTabs));
    }

    /// <summary>
    /// ADR-019 / TASK-342: effective permissions baked into the JWT and returned to the
    /// client = <see cref="User.Permissions"/> (permanent per-key true/false override)
    /// with every currently-active temporary grant (<c>user_permission_grants</c>,
    /// <c>ExpiresAt</c> in the future and <c>RevokedAt</c> null) forced to
    /// <c>true</c> — a live temporary grant always wins, even over an explicit permanent
    /// <c>false</c>, since it is the more specific and more recently issued authorization.
    /// Merge happens once, here, at JWT-mint time — not per request (accepted 15-min
    /// propagation delay, same as the existing permanent-override path).
    /// </summary>
    private async Task<Dictionary<string, bool>> BuildEffectivePermissionsAsync(User user, CancellationToken ct)
    {
        var effective = user.Permissions is null
            ? new Dictionary<string, bool>()
            : new Dictionary<string, bool>(user.Permissions);

        if (user.TenantId is null)
            return effective; // provider/no-tenant users have no tenant-scoped grants

        var activeGrants = await _permissionGrants.GetActiveGrantsForUserAsync(
            user.TenantId.Value, user.Id, ct) ?? Array.Empty<UserPermissionGrant>();

        foreach (var grant in activeGrants)
            effective[grant.PermissionKey] = true;

        return effective;
    }

    /// <summary>
    /// ADR-020 (TASK-346): effective capabilities baked into the JWT and returned to the
    /// client — empty when <see cref="User.TenantRoleId"/> is null, the user has no tenant
    /// (provider), or the referenced <see cref="TenantRole"/> is archived (IsActive=false),
    /// otherwise the template's live <see cref="TenantRole.Capabilities"/> list. Merge happens
    /// once, here, at JWT-mint time — same ~15-min propagation delay already accepted for
    /// <see cref="BuildEffectivePermissionsAsync"/> (ADR-019). Editing a template propagates to
    /// every assignee on their next login/refresh, no per-user snapshot.
    /// </summary>
    private async Task<List<string>> BuildEffectiveCapabilitiesAsync(User user, CancellationToken ct)
    {
        if (user.TenantRoleId is null || user.TenantId is null)
            return [];

        var role = await _tenantRoles.GetByIdAsync(user.TenantId.Value, user.TenantRoleId.Value, ct);
        if (role is null || !role.IsActive)
            return [];

        return role.Capabilities;
    }

    /// <summary>
    /// TASK-391b: effective sidebar-tab visibility baked into the JWT "tabs" claim and
    /// AuthUserDto.Tabs — same empty/archived short-circuit as
    /// <see cref="BuildEffectiveCapabilitiesAsync"/>, reading <see cref="TenantRole.AllowedTabs"/>
    /// instead of <see cref="TenantRole.Capabilities"/>. Kept as a fully independent method
    /// (its own TenantRole fetch) rather than sharing one lookup with capabilities, mirroring the
    /// deliberate Capabilities/AllowedTabs separation on
    /// <see cref="ShelfGuard.Domain.Constants.TenantRoleTabs"/> — actions vs. UI visibility are
    /// different axes with different consumers, so their resolution paths stay independent too.
    /// </summary>
    private async Task<List<string>> BuildEffectiveTabsAsync(User user, CancellationToken ct)
    {
        if (user.TenantRoleId is null || user.TenantId is null)
            return [];

        var role = await _tenantRoles.GetByIdAsync(user.TenantId.Value, user.TenantRoleId.Value, ct);
        if (role is null || !role.IsActive)
            return [];

        return role.AllowedTabs;
    }

    /// <summary>Increments the shared failure counter, persists, and audits the attempt.</summary>
    private Task RegisterFailedAttemptAsync(User user, string action, string? ipAddress, CancellationToken ct) =>
        SaveInTenantAsync(user.TenantId, () => RegisterFailedAttemptCoreAsync(user, action, ipAddress, ct), ct);

    private async Task RegisterFailedAttemptCoreAsync(User user, string action, string? ipAddress, CancellationToken ct)
    {
        var lockedOut = user.RegisterFailedLogin(MaxFailedAttempts, LockoutDuration);
        _users.Update(user);

        // TASK-370: user update below used to commit separately from the activity
        // log write that follows (two round trips); now batched into the single
        // _activityLogs.SaveChangesAsync(ct) call at the end of this method — same
        // shared AppDbContext reasoning as IssueTokensAsync above.
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

    private static AuthUserDto ToDto(
        User u,
        IReadOnlyDictionary<string, bool>? effectivePermissions = null,
        IReadOnlyList<string>? effectiveCapabilities = null,
        IReadOnlyList<string>? effectiveTabs = null) =>
        new(u.Id, u.Email, u.FullName, u.Role, u.TenantId, u.Tenant?.Name, u.StoreId,
            effectivePermissions is null ? u.Permissions : new Dictionary<string, bool>(effectivePermissions),
            u.LegalEntityId, u.TotpEnabled, effectiveCapabilities, u.TelegramChatId,
            u.PreferredLocale, effectiveTabs,
            u.HasActiveTempPassword, u.HasActiveTempPassword ? u.TempPasswordExpiresAt : null);
}
