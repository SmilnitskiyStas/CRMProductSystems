using System.Security.Claims;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Reads the JWT "capabilities" claim (comma-joined, minted by
/// <c>JwtService.GenerateAccessToken</c> from the caller's assigned
/// <see cref="ShelfGuard.Domain.Entities.TenantRole"/> — ADR-020, TASK-346). Mirrors
/// <see cref="LegalEntityAuthorization"/>'s "permissions" claim parsing exactly.
/// Shared by <see cref="RoleOrCapabilityHandler"/> (the per-action policy OR on the 7
/// capability-enabled controllers) and <see cref="LegalEntityAuthorization.CanManage"/>
/// (its own separate imperative OR path, unchanged shape).
/// </summary>
public static class TenantRoleAuthorization
{
    public static bool HasCapability(ClaimsPrincipal user, string capability)
    {
        var capabilities = user.Claims.FirstOrDefault(c => c.Type == "capabilities")?.Value;
        if (string.IsNullOrEmpty(capabilities))
            return false;

        return capabilities.Split(',').Contains(capability);
    }
}
