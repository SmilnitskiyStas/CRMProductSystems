using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShelfGuard.Application.Common;
using ShelfGuard.Application.Features.Auth.Dtos;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Domain.Interfaces;

namespace ShelfGuard.Application.Features.Auth;

public sealed class PasswordRecoveryService(
    IUserRepository users, IPasswordRecoveryStore store, IPasswordHasher hasher,
    PasswordRecoveryOptions options, TimeProvider clock, ILogger<PasswordRecoveryService> logger)
    : IPasswordRecoveryService
{
    public const string EventType = "auth.password_reset_link_requested";
    public const string InvalidGrant = "Invalid or expired password reset link.";
    public const int GrantMinutes = 15;

    public async Task RequestAsync(string email, string? ipAddress = null, CancellationToken ct = default)
    {
        // Deliberately do not use Request.Host or a user-provided return URL.
        if (!Uri.TryCreate(options.WebBaseUrl, UriKind.Absolute, out var origin)
            || origin.Scheme != "https" || origin.UserInfo.Length != 0
            || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("Password recovery requires an approved HTTPS web base URL.");

        try
        {
            var user = await users.GetByEmailAsync(email.Trim().ToLowerInvariant(), ct);
            if (user is null || !user.IsActive) return;
            var now = clock.GetUtcNow().UtcDateTime;
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            // Fragment credentials never reach access logs or the browser's Referer header.
            var url = options.WebBaseUrl.TrimEnd('/') + "/reset-password#email="
                + Uri.EscapeDataString(user.Email) + "&token=" + token;
            await store.IssueAsync(user, Hash(token), now, now.AddMinutes(GrantMinutes), new NotificationQueue
            {
                TenantId = user.TenantId, UserId = user.Id, Channel = "system", EventType = EventType,
                Title = "Відновлення пароля", Status = "pending",
                Payload = JsonSerializer.Serialize(new { resetUrl = url, expiresInMinutes = GrantMinutes, expiresAt = now.AddMinutes(GrantMinutes) }),
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Same public response on a delivery/storage fault. No credential/session was changed.
            // Do not log the exception payload, email, reset URL, or token.
            logger.LogError("Password recovery could not be queued ({ErrorType}).", ex.GetType().Name);
        }
    }

    public async Task<string?> ConfirmAsync(ResetPasswordRequest request, string? ipAddress = null, CancellationToken ct = default)
    {
        if (request.Token is not { Length: 43 } || request.Token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            return InvalidGrant;
        var user = await users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = Hash(request.Token);
        if (user is null || !user.IsActive || user.PasswordResetExpiresAt is null || user.PasswordResetExpiresAt <= now
            || user.PasswordResetTokenHash is null || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(user.PasswordResetTokenHash), Encoding.UTF8.GetBytes(hash)))
            return InvalidGrant;
        var validationError = PasswordValidator.Validate(request.NewPassword, user.Email);
        if (validationError is not null) return validationError;

        // A guarded UPDATE rechecks proof/expiry/active state/password on the DB row. One winner only.
        // Reset never disables MFA, issues a session, or bypasses the normal password+MFA login.
        return await store.ConfirmAsync(user.Id, hash, user.PasswordHash,
            hasher.Hash(request.NewPassword), now, ipAddress, ct) ? null : InvalidGrant;
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
