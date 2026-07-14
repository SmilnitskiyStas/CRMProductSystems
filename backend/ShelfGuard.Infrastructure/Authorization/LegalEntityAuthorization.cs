using System.Security.Claims;
using ShelfGuard.Domain.Constants;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Write-access check for Legal Entities (TASK-322): enterprise_admin (and provider) and above,
/// OR any user granted the <c>legal_entities.manage</c> per-user permission override
/// (carried in the JWT "permissions" claim — see <c>JwtService.GenerateAccessToken</c>),
/// OR any user whose assigned TenantRole template grants the same <c>legal_entities.manage</c>
/// key as a capability (ADR-020, TASK-346 — carried in the JWT "capabilities" claim, see
/// <see cref="TenantRoleAuthorization"/>). This controller deliberately keeps the imperative
/// in-body check shape instead of moving to a RoleOrCapabilityRequirement policy — its
/// class-level policy (AtLeastStoreManager) is looser than every branch of this check, so it
/// never blocks anything this method would otherwise admit (unlike the 7 controllers where the
/// class-level policy had to be removed — see ADR-020 "the blocking discovery").
/// </summary>
public static class LegalEntityAuthorization
{
    public static bool CanManage(ClaimsPrincipal user)
    {
        if (AppPolicies.AtLeastEnterpriseAdminRoles.Any(user.IsInRole))
            return true;

        var permissions = user.Claims.FirstOrDefault(c => c.Type == "permissions")?.Value;
        if (!string.IsNullOrEmpty(permissions) &&
            permissions.Split(',').Contains(TenantUserPermissions.LegalEntitiesManage))
            return true;

        return TenantRoleAuthorization.HasCapability(user, TenantUserPermissions.LegalEntitiesManage);
    }
}
