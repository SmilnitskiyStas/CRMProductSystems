namespace ShelfGuard.Application.Services;

public interface IJwtService
{
    string GenerateAccessToken(Guid userId, string email, string role, Guid? tenantId, Guid? storeId, string? fullName = null);

    /// <summary>
    /// Generates a short-lived (60 min) impersonation token so a provider user
    /// can browse a specific tenant's data as enterprise_admin.
    /// Includes an extra claim: impersonated=true.
    /// </summary>
    string GenerateImpersonationToken(Guid providerId, string providerEmail, Guid targetTenantId);

    (string RawToken, string TokenHash) GenerateRefreshToken();
    string HashToken(string token);
}
