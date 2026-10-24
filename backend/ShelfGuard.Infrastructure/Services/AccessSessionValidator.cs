using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Services;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

/// <summary>Fresh database check for every bearer-authenticated HTTP request. No positive cache.</summary>
public sealed class AccessSessionValidator(IServiceScopeFactory scopes) : IAccessSessionValidator
{
    public async Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub"), out var id)
            || !Guid.TryParse(principal.FindFirstValue("sid"), out var sid) || sid == Guid.Empty
            || !int.TryParse(principal.FindFirstValue("auth_ver"), out var version) || version < 0) return false;
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? principal.FindFirstValue("role");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        if (role == "consumer")
        {
            if (principal.FindFirstValue("consumer_account_id") != id.ToString() || principal.HasClaim(c => c.Type == "tenant_id")) return false;
            return await db.ConsumerRefreshTokens.AsNoTracking().AnyAsync(t => t.ConsumerAccountId == id
                && t.SessionId == sid && t.AuthVersion == version && t.RevokedAt == null && t.ExpiresAt > now
                && t.ConsumerAccount.IsActive && t.ConsumerAccount.AuthVersion == version, ct);
        }
        if (principal.HasClaim(c => c.Type == "consumer_account_id")) return false;
        var impersonated = principal.FindFirstValue("impersonated") == "true";
        var tenant = principal.FindFirstValue("tenant_id");
        Guid? tenantId = Guid.TryParse(tenant, out var parsed) ? parsed : null;
        if (impersonated && (tenantId == null || !await db.Tenants.AnyAsync(t => t.Id == tenantId && t.IsActive, ct))) return false;
        var session = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == id && t.SessionId == sid
            && t.AuthVersion == version && t.RevokedAt == null && t.ExpiresAt > now && t.User.IsActive
            && t.User.AuthVersion == version && (t.User.Tenant == null || t.User.Tenant.IsActive)
            && (impersonated ? (t.User.Role == "provider" || t.User.Role == "provider_admin" || t.User.Role == "provider_agent")
                : t.User.Role == role && t.User.TenantId == tenantId)).Select(t => new { t.User, t.MfaVerifiedAt }).SingleOrDefaultAsync(ct);
        if (session == null) return false;
        if (session.MfaVerifiedAt != null && (!long.TryParse(principal.FindFirstValue("mfa_at"), out var at)
            || at != new DateTimeOffset(DateTime.SpecifyKind(session.MfaVerifiedAt.Value, DateTimeKind.Utc)).ToUnixTimeSeconds())) return false;
        // Enrollment is voluntary, including for privileged roles. Once enabled,
        // MFA must have been verified by every session that opens the workspace.
        return !session.User.TotpEnabled || session.MfaVerifiedAt != null;
    }
}
