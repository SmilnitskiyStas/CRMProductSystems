namespace ShelfGuard.Application.Services;

public interface IJwtService
{
    string GenerateAccessToken(Guid userId, string email, string role, Guid? tenantId, Guid? storeId, string? fullName = null,
        Dictionary<string, bool>? permissions = null);

    /// <summary>
    /// Generates a short-lived (60 min) impersonation token so a provider user
    /// can browse a specific tenant's data as enterprise_admin.
    /// Includes an extra claim: impersonated=true.
    /// </summary>
    string GenerateImpersonationToken(Guid providerId, string providerEmail, Guid targetTenantId);

    (string RawToken, string TokenHash) GenerateRefreshToken();
    string HashToken(string token);

    /// <summary>
    /// Generates a short-lived (5 min) 2FA challenge token issued after a correct
    /// password when TOTP is enabled (TASK-330). Claims: sub=userId, purpose="2fa".
    /// Uses a dedicated audience so it can NEVER pass API bearer authentication.
    /// </summary>
    string GenerateTwoFactorChallengeToken(Guid userId);

    /// <summary>
    /// Validates a 2FA challenge token (signature, lifetime, purpose claim).
    /// Returns the user id, or null when the token is invalid/expired.
    /// </summary>
    Guid? ValidateTwoFactorChallengeToken(string token);
}
