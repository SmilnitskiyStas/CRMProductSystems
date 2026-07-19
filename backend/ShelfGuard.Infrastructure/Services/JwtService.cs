using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.Services;

public sealed class JwtService : IJwtService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessTokenMinutes;

    public JwtService(IConfiguration config)
    {
        _secret   = config["Jwt:Secret"]   ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        _issuer   = config["Jwt:Issuer"]   ?? "shelfguard";
        _audience = config["Jwt:Audience"] ?? "shelfguard";
        _accessTokenMinutes = int.Parse(config["Jwt:AccessTokenMinutes"] ?? "15");
    }

    public string GenerateAccessToken(Guid userId, string email, string role, Guid? tenantId, Guid? storeId, string? fullName = null,
        Dictionary<string, bool>? permissions = null, List<string>? capabilities = null, List<string>? tabs = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(ClaimTypes.Role, role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (!string.IsNullOrWhiteSpace(fullName))
            claims.Add(new Claim("full_name", fullName));
        if (tenantId.HasValue)
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        if (storeId.HasValue)
            claims.Add(new Claim("store_id", storeId.Value.ToString()));

        if (permissions is { Count: > 0 })
        {
            var granted = string.Join(',', permissions.Where(p => p.Value).Select(p => p.Key));
            if (!string.IsNullOrEmpty(granted))
                claims.Add(new Claim("permissions", granted));
        }

        // ADR-020 (TASK-346): TenantRole capabilities, same comma-joined shape as "permissions".
        if (capabilities is { Count: > 0 })
            claims.Add(new Claim("capabilities", string.Join(',', capabilities)));

        // TASK-391b: TenantRole.AllowedTabs (sidebar-tab visibility), same comma-joined shape.
        if (tabs is { Count: > 0 })
            claims.Add(new Claim("tabs", string.Join(',', tabs)));

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_accessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc/>
    public string GenerateImpersonationToken(Guid providerId, string providerEmail, Guid targetTenantId)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   providerId.ToString()),
            new(JwtRegisteredClaimNames.Email, providerEmail),
            new(ClaimTypes.Role,               "enterprise_admin"),   // scoped-down from provider
            new("tenant_id",                   targetTenantId.ToString()),
            new("impersonated",                "true"),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:             _issuer,
            audience:           _audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(60),  // short-lived impersonation window
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string RawToken, string TokenHash) GenerateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (raw, HashToken(raw));
    }

    // ── 2FA challenge tokens (TASK-330) ─────────────────────────────────────
    // Dedicated audience: the API's JwtBearer validates ValidAudience=_audience,
    // so a challenge token can never be replayed as an access token.
    private string TwoFactorAudience => _audience + ":2fa";

    /// <inheritdoc/>
    public string GenerateTwoFactorChallengeToken(Guid userId)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("purpose",                   "2fa"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:             _issuer,
            audience:           TwoFactorAudience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc/>
    public Guid? ValidateTwoFactorChallengeToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        try
        {
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = _issuer,
                ValidAudience            = TwoFactorAudience,
                IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret)),
                ClockSkew                = TimeSpan.Zero,
            }, out _);

            if (principal.FindFirst("purpose")?.Value != "2fa")
                return null;

            var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            return Guid.TryParse(sub, out var userId) && userId != Guid.Empty ? userId : null;
        }
        catch
        {
            return null; // invalid signature / expired / malformed
        }
    }

    public string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
