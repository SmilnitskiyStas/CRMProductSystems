using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShelfGuard.Application.Services;
using ShelfGuard.Domain.Constants;
using ShelfGuard.Infrastructure.Data;

namespace ShelfGuard.Infrastructure.Services;

public sealed class WorkAccessContext(AppDbContext db, IHttpContextAccessor http) : IWorkAccessContext
{
    public async Task<WorkAccessScope?> GetAsync(CancellationToken ct = default)
    {
        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenant) || tenant == Guid.Empty
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? "";
        if (role == AppRoles.Consumer || AppRoles.ProviderTeamRoles.Contains(role)) return null;
        var delegated = principal.HasClaim("impersonated", "true") && role == AppRoles.EnterpriseAdmin;
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        var permissions = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (!delegated)
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.TenantId == tenant && u.IsActive && u.Role == role, ct);
            if (user == null) return null;
            if (user.Permissions != null) permissions = new(user.Permissions, StringComparer.Ordinal);
            if (user.TenantRoleId != null)
            {
                var template = await db.TenantRoles.AsNoTracking().SingleOrDefaultAsync(t => t.Id == user.TenantRoleId && t.TenantId == tenant && t.IsActive, ct);
                if (template != null) capabilities.UnionWith(template.Capabilities);
            }
            var now = DateTime.UtcNow;
            var grants = await db.UserPermissionGrants.AsNoTracking().Where(g => g.TenantId == tenant && g.UserId == userId && g.RevokedAt == null && g.ExpiresAt > now)
                .Select(g => g.PermissionKey).ToListAsync(ct);
            foreach (var grant in grants) permissions[grant] = true;
        }
        if (!await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenant && t.IsActive, ct)) return null;
        var wide = role == AppRoles.EnterpriseAdmin;
        var locations = wide ? [] : await db.UserLocations.AsNoTracking()
            .Where(a => a.UserId == userId && a.TenantId == tenant && db.Locations.Any(l => l.Id == a.LocationId && l.TenantId == tenant))
            .Select(a => a.LocationId).Distinct().ToArrayAsync(ct);
        return new(tenant, userId, role, wide, locations, capabilities, permissions);
    }
}
