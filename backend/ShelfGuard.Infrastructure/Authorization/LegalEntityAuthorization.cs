using System.Security.Claims;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Write-access check for Legal Entities (TASK-322): enterprise_admin (and provider) and above,
/// OR any user granted the <c>legal_entities.manage</c> per-user permission override
/// (carried in the JWT "permissions" claim — see <c>JwtService.GenerateAccessToken</c>).
/// </summary>
public static class LegalEntityAuthorization
{
    public static bool CanManage(ClaimsPrincipal user)
    {
        if (AppPolicies.AtLeastEnterpriseAdminRoles.Any(user.IsInRole))
            return true;

        var permissions = user.Claims.FirstOrDefault(c => c.Type == "permissions")?.Value;
        if (string.IsNullOrEmpty(permissions))
            return false;

        return permissions.Split(',').Contains(TenantUserPermissions.LegalEntitiesManage);
    }
}
