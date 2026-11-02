using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Domain.Entities;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

public sealed class LiveMfaPolicy(AppDbContext db, ITenantSessionOverride tenantContext) : IMfaPolicy
{
    public async Task<bool> IsRequiredAsync(User user, CancellationToken ct = default)
    {
        if (AccessMatrixPolicy.MandatoryMfaRoles.Contains(user.Role)
            || user.Permissions?.Any(p => p.Value && AccessMatrixPolicy.PrivilegedPermissions.Contains(p.Key)) == true) return true;
        if (user.TenantId is not Guid tenantId) return false;
        // Also works during password login / bearer validation before HttpContext.User is set.
        return await tenantContext.ExecuteAsync(tenantId, async () =>
        {
            if (user.TenantRoleId != null)
            {
                var role = await db.TenantRoles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == user.TenantRoleId && r.TenantId == tenantId && r.IsActive, ct);
                if (role?.Capabilities.Any(AccessMatrixPolicy.PrivilegedPermissions.Contains) == true) return true;
            }
            var now = DateTime.UtcNow;
            var keys = AccessMatrixPolicy.PrivilegedPermissions.ToArray();
            return await db.UserPermissionGrants.AsNoTracking().AnyAsync(g => g.TenantId == tenantId && g.UserId == user.Id
                && g.RevokedAt == null && g.ExpiresAt > now && keys.Contains(g.PermissionKey), ct);
        }, ct);
    }
}
