using Microsoft.AspNetCore.Authorization;

namespace ShelfGuard.Infrastructure.Authorization;

/// <summary>
/// Authorization requirement satisfied when the caller's role is one of
/// <see cref="AllowedRoles"/> (same role-membership check <c>RequireRole()</c> /
/// <see cref="Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement"/>
/// already use — <c>ClaimsPrincipal.IsInRole</c>) OR the caller's JWT "capabilities" claim
/// contains <see cref="Capability"/> (see <see cref="TenantRoleAuthorization.HasCapability"/>).
///
/// ADR-020 (TASK-346): this — not an imperative in-body check — is what makes the OR real at
/// the ASP.NET Core authorization-middleware level. Multiple <c>[Authorize]</c> attributes on
/// the same endpoint (class-level + action-level) are combined with logical AND, so an
/// in-body check layered under a role-restrictive class-level policy can only ever narrow
/// what that class-level policy already let through — it can never admit a role the
/// class-level policy rejects. Every controller using this requirement had its class-level
/// policy removed (or, where every action ends up sharing the same OR-policy, replaced by it)
/// so the OR is evaluated for every affected action — see <c>AppPolicies.Configure</c>.
/// </summary>
public sealed class RoleOrCapabilityRequirement : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> AllowedRoles { get; }
    public string Capability { get; }

    public RoleOrCapabilityRequirement(IReadOnlyCollection<string> allowedRoles, string capability)
    {
        AllowedRoles = allowedRoles;
        Capability = capability;
    }
}
