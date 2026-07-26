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
/// differs (no tenant_id, a "consumer_account_id" claim instead), there is no 2FA/refresh-token
/// flow, and the identity space (ConsumerAccount vs. User) is entirely different.
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

    public ConsumerAuthService(
        IConsumerAccountRepository accounts,
        IPasswordHasher hasher,
        IJwtService jwt,
        ILogger<ConsumerAuthService> logger)
    {
        _accounts = accounts;
        _hasher = hasher;
        _jwt = jwt;
        _logger = logger;
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

        var account = new ConsumerAccount
        {
            Phone = phone,
            PasswordHash = _hasher.Hash(request.Password),
            FullName = request.FullName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
        };

        await _accounts.AddAsync(account, ct);
        await _accounts.SaveChangesAsync(ct);

        _logger.LogInformation("Consumer account registered ({Phone}).", phone);

        var token = _jwt.GenerateConsumerAccessToken(account.Id, account.FullName);
        return (new ConsumerAuthResponse(token, account.Id, account.FullName, account.Phone), null);
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

        var token = _jwt.GenerateConsumerAccessToken(account.Id, account.FullName);
        return (new ConsumerAuthResponse(token, account.Id, account.FullName, account.Phone), null);
    }
}
