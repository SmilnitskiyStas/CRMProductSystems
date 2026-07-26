namespace ShelfGuard.Application.Services;

public interface IJwtService
{
    /// <param name="capabilities">
    /// Effective TenantRole capabilities (ADR-020, TASK-346) — serialized into a comma-joined
    /// "capabilities" claim, same shape as <paramref name="permissions"/>. Empty/null → claim omitted.
    /// </param>
    /// <param name="tabs">
    /// Effective TenantRole sidebar-tab visibility (TASK-391b) — serialized into a comma-joined
    /// "tabs" claim, same shape as <paramref name="capabilities"/>. A separate axis (UI visibility,
    /// not backend authorization — see ShelfGuard.Domain.Constants.TenantRoleTabs). Empty/null →
    /// claim omitted.
    /// </param>
    string GenerateAccessToken(Guid userId, string email, string role, Guid? tenantId, Guid? storeId, string? fullName = null,
        Dictionary<string, bool>? permissions = null, List<string>? capabilities = null, List<string>? tabs = null);

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

    /// <summary>
    /// TASK-405 (Loyalty Фаза 0): generates an access token for a
    /// <see cref="ShelfGuard.Domain.Entities.ConsumerAccount"/> session — a wholly separate
    /// identity space from staff <see cref="ShelfGuard.Domain.Entities.User"/> sessions.
    /// Claims: sub=consumerAccountId, role="consumer", consumer_account_id=consumerAccountId.
    /// Deliberately carries NO tenant_id claim — a consumer session is cross-tenant by
    /// design (reads every LoyaltyMembership it holds via the consumer_self_access RLS
    /// policy, keyed off app.consumer_account_id, never app.tenant_id). Uses the SAME
    /// audience as a staff access token (unlike the dedicated 2FA-challenge audience) so it
    /// passes ordinary [Authorize] on consumer-facing endpoints. Longer-lived than the staff
    /// access token by design: ConsumerAccount has no refresh-token table (schema frozen for
    /// this task — RefreshToken is keyed to User, not ConsumerAccount), so there is no
    /// silent-refresh path; default 30 days, configurable via Jwt:ConsumerAccessTokenDays.
    /// </summary>
    string GenerateConsumerAccessToken(Guid consumerAccountId, string? fullName = null);
}
