using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.ConsumerAuth.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.ConsumerAuth;

/// <summary>
/// Register/login for <see cref="ConsumerAccount"/> — deliberately a separate implementation
/// from staff AuthService rather than shoehorned into it (TASK-405 brief): the claim shape
/// differs (no tenant_id, a "consumer_account_id" claim instead), rotating personal refresh
/// sessions are separate, and the identity space (ConsumerAccount vs. User) is different.
///
/// Mirrors AuthService's account-lockout pattern (TASK-329: 5 failures -> 15 min lockout,
/// generic error on every failure path, timing-equalized lockout branch) applied directly to
/// ConsumerAccount's plain mutable FailedLoginAttempts/LockoutUntil properties (this entity has
/// no domain methods — see ConsumerAccount.cs class doc: same "public get/set" style as
/// Customer/PosTransaction, not User's private-setter/domain-method style).
/// </summary>
public sealed class ConsumerAuthService : IConsumerAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const string GenericLoginError = "Invalid phone number or password.";

    private readonly IConsumerAccountRepository _accounts;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtService _jwt;
    private readonly ILogger<ConsumerAuthService> _logger;
    private readonly IConsumerSessionStore _sessions;
    public const string RotationRetry = "Refresh already rotated; retry with the current proof.";

    public ConsumerAuthService(
        IConsumerAccountRepository accounts,
        IPasswordHasher hasher,
        IJwtService jwt,
        ILogger<ConsumerAuthService> logger, IConsumerSessionStore sessions)
    {
        _accounts = accounts;
        _hasher = hasher;
        _jwt = jwt;
        _logger = logger;
        _sessions = sessions;
    }

    public async Task<(ConsumerAuthResponse? Response, string? Error)> RegisterAsync(
        ConsumerRegisterRequest request, CancellationToken ct = default)
    {
        var phone = PhoneNormalizer.Normalize(request.Phone);
        if (phone is null)
            return (null, "Invalid phone number. Expected a Ukrainian mobile number.");

        if (string.IsNullOrWhiteSpace(request.FullName))
            return (null, "Full name is required.");

        var passwordError = PasswordValidator.Validate(request.Password, request.Email);
        if (passwordError is not null)
            return (null, passwordError);

        var existing = await _accounts.GetByPhoneAsync(phone, ct);
        if (existing is not null)
            return (null, "An account with this phone number already exists.");

        var email = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : request.Email.Trim().ToLowerInvariant();
        if (email is not null && await _accounts.GetByEmailAsync(email, ct) is not null)
            return (null, "An account with this email already exists.");

        var account = new ConsumerAccount
        {
            Phone = phone,
            PasswordHash = _hasher.Hash(request.Password),
            FullName = request.FullName.Trim(),
            Email = email,
        };

        await _accounts.AddAsync(account, ct);
        await _accounts.SaveChangesAsync(ct);

        _logger.LogInformation("Consumer account registered ({Phone}).", phone);

        return await IssueAsync(account, ct);
    }

    public async Task<(ConsumerAuthResponse? Response, string? Error)> LoginAsync(
        ConsumerLoginRequest request, CancellationToken ct = default)
    {
        var phone = PhoneNormalizer.Normalize(request.Phone);
        if (phone is null)
            return (null, GenericLoginError);

        var account = await _accounts.GetByPhoneAsync(phone, ct);
        if (account is null)
        {
            _logger.LogWarning("Failed consumer login for unknown phone {Phone}.", phone);
            return (null, GenericLoginError);
        }

        if (!account.IsActive)
            return (null, GenericLoginError);

        if (account.LockoutUntil.HasValue && account.LockoutUntil.Value > DateTimeOffset.UtcNow)
        {
            // Locked: reject without revealing the lockout, but still run the hash
            // verification so response timing matches the wrong-password path (TASK-329).
            _hasher.Verify(request.Password, account.PasswordHash);
            return (null, GenericLoginError);
        }

        if (!_hasher.Verify(request.Password, account.PasswordHash))
        {
            account.FailedLoginAttempts++;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
            {
                account.LockoutUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                account.FailedLoginAttempts = 0;
                _logger.LogWarning(
                    "Consumer account {Phone} locked out after {N} failed attempts.",
                    phone, MaxFailedAttempts);
            }

            _accounts.Update(account);
            await _accounts.SaveChangesAsync(ct);
            return (null, GenericLoginError);
        }

        account.FailedLoginAttempts = 0;
        account.LockoutUntil = null;
        account.LastLoginAt = DateTimeOffset.UtcNow;
        _accounts.Update(account);
        await _accounts.SaveChangesAsync(ct);

        return await IssueAsync(account, ct);
    }

    private async Task<(ConsumerAuthResponse? Response, string? Error)> IssueAsync(ConsumerAccount account, CancellationToken ct)
    {
        var (raw, hash) = _jwt.GenerateRefreshToken();
        var proof = new ConsumerRefreshToken { ConsumerAccountId = account.Id, TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddDays(30), AuthVersion = account.AuthVersion };
        if (!await _sessions.IssueAsync(account, proof, ct)) return (null, GenericLoginError);
        return (Response(account, proof, raw), null);
    }

    public async Task<(ConsumerAuthResponse? Response, string? Error)> RefreshAsync(string rawToken, CancellationToken ct = default)
    {
        const string error = "Invalid or expired refresh token.";
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 256) return (null, error);
        var existing = await _sessions.FindAsync(_jwt.HashToken(rawToken), ct);
        if (existing is null) return (null, error);
        if (existing.RevokedAt != null)
        {
            if (existing.ReplacedByTokenHash != null && DateTime.UtcNow - existing.RevokedAt.Value < TimeSpan.FromSeconds(10))
                return (null, RotationRetry);
            await _sessions.RevokeAsync(existing.ConsumerAccountId, existing.SessionId, ct);
            return (null, error);
        }
        var account = existing.ConsumerAccount;
        if (existing.ExpiresAt <= DateTime.UtcNow || !account.IsActive || account.AuthVersion != existing.AuthVersion) return (null, error);
        var (raw, hash) = _jwt.GenerateRefreshToken();
        var next = new ConsumerRefreshToken { ConsumerAccountId = account.Id, SessionId = existing.SessionId,
            TokenHash = hash, ExpiresAt = existing.ExpiresAt, AuthVersion = account.AuthVersion };
        if (!await _sessions.RotateAsync(existing, next, ct)) return (null, RotationRetry);
        return (Response(account, next, raw), null);
    }

    private ConsumerAuthResponse Response(ConsumerAccount account, ConsumerRefreshToken proof, string raw) =>
        new(_jwt.GenerateConsumerAccessToken(account.Id, account.FullName, proof.SessionId, proof.AuthVersion),
            account.Id, account.FullName, account.Phone, raw);

    public Task RevokeAsync(Guid accountId, Guid? sessionId, CancellationToken ct = default) => _sessions.RevokeAsync(accountId, sessionId, ct);
}
