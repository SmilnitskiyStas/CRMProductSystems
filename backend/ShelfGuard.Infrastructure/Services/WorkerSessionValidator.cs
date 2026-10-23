using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShelfGuard.Application.Features.Auth;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

public sealed class WorkerSessionValidator(IServiceScopeFactory scopes, WorkerAuthOptions options) : IWorkerSessionValidator
{
    public async Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.Email) || !principal.HasClaim("purpose", "worker-job")
            || principal.HasClaim(c => c.Type is "sid" or "session_id" or "mfa_at" or "impersonated" or "consumer_account_id")
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub"), out var id)
            || !Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenant)
            || !int.TryParse(principal.FindFirstValue("auth_ver"), out var version) || version < 0) return false;
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? principal.FindFirstValue("role");
        if (role == null || !WorkerAuthService.AllowedRole(role)) return false;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = options.Email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        return await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.TenantId == tenant && u.Role == role && u.AuthVersion == version
            && u.Email == email && u.IsActive && u.Tenant != null && u.Tenant.IsActive
            && (u.LockoutUntil == null || u.LockoutUntil <= now), ct);
    }
}
